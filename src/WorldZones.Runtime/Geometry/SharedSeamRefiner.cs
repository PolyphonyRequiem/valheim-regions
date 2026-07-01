using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>Tunables for <see cref="SharedSeamRefiner"/>. Mirror <see cref="RingRefineOptions"/> so a
    /// seam-built fill ring lands on the same curve the per-region ring path would — the whole point of the
    /// shared primitive is that the two agree.</summary>
    public sealed class SharedSeamRefineOptions
    {
        /// <summary>Max perpendicular distance (m) a seam vertex may move to reach its feature. 40 m.</summary>
        public double MaxDisplacement { get; set; } = 40.0;

        /// <summary>Perpendicular march step (m) when searching for the crossing. 4 m.</summary>
        public double MarchStep { get; set; } = 4.0;

        /// <summary>Despike threshold (m): a snapped point further than this from its neighbour midpoint is a
        /// snap spur, pulled back. 24 m. 0 disables.</summary>
        public double DespikeThreshold { get; set; } = 24.0;

        /// <summary>Terrain-scale Gaussian σ (m) — the shipped smoothing dial. 30 m. 0 ⇒ despike-only (no
        /// Gaussian). The seam is an OPEN arc between pinned junctions, so this uses the OPEN
        /// <see cref="PolylineSmoother.SmoothGaussian"/> (endpoints pinned), NOT the closed-ring variant.</summary>
        public double SmoothingSigmaMeters { get; set; } = 30.0;

        /// <summary>Seams shorter than this many coarse vertices skip smoothing (kept snapped-only) — a stub
        /// between two nearby junctions has nothing to smooth and can only wander. 4.</summary>
        public int MinVertsToSmooth { get; set; } = 4;

        public static SharedSeamRefineOptions Default => new SharedSeamRefineOptions();
    }

    /// <summary>
    /// Refines ONE <see cref="SharedSeam"/> exactly once — the shared-primitive analogue of
    /// <see cref="RegionRingRefiner"/>, but for an OPEN arc between two pinned junction endpoints instead of
    /// a closed ring. Interior seams snap to a biome-category flip; coast seams snap to the height iso; then
    /// the arc is despiked and σ-smoothed with its endpoints PINNED, through a watertight ladder (σ
    /// self-intersects ⇒ fall back to despiked ⇒ fall back to raw coarse). Because the endpoints never move,
    /// two seams meeting at a junction stay coincident (spike-004 SPIKE 2: gap = 0). Pure Tier-1, no Unity.
    ///
    /// <para>Why open, not closed: a seam runs junction→junction, and the junctions are shared with the
    /// OTHER seams meeting there — they must not drift, so they are pinned. The closed-ring smoother
    /// (<see cref="PolylineSmoother.SmoothGaussianClosed"/>) is for a whole region loop with no junctions;
    /// this is its open sibling.</para>
    /// </summary>
    public static class SharedSeamRefiner
    {
        /// <summary>Refine one seam's coarse polyline into its authoritative refined polyline (Node0 → Node1,
        /// endpoints pinned). Returns the input coarse polyline unchanged when there is nothing to hug/smooth.</summary>
        public static IReadOnlyList<WzVec2> RefineOnce(SharedSeam seam,
            IScalarField coastField, ICategoryField seamField, SharedSeamRefineOptions options = null)
        {
            if (seam == null) throw new ArgumentNullException(nameof(seam));
            options ??= SharedSeamRefineOptions.Default;

            var raw = seam.Coarse;
            int n = raw.Count;
            if (n < 2) return raw;

            bool coast = seam.IsCoast;
            IScalarField iso = coast ? coastField : null;
            ICategoryField flip = coast ? null : seamField;
            // No field to hug ⇒ leave the coarse arc as-is (still a valid, watertight staircase).
            bool canSnap = coast ? (iso != null) : (flip != null);

            // ── Stage 1: snap each INTERIOR vertex along its local normal (endpoints are junctions — PINNED) ──
            var snapped = new List<WzVec2>(n) { raw[0] };
            for (int i = 1; i < n - 1; i++)
            {
                if (!canSnap) { snapped.Add(raw[i]); continue; }
                WzVec2 a = raw[i - 1], b = raw[i + 1];
                double tx = b.X - a.X, tz = b.Z - a.Z, tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-9) { snapped.Add(raw[i]); continue; }
                double nx = -tz / tl, nz = tx / tl;
                bool ok = coast
                    ? SnapToIso(iso, raw[i].X, raw[i].Z, nx, nz, options, out double s)
                    : SnapToFlip(flip, raw[i].X, raw[i].Z, nx, nz, options, out s);
                snapped.Add(ok && Math.Abs(s) > 1e-6 ? new WzVec2(raw[i].X + s * nx, raw[i].Z + s * nz) : raw[i]);
            }
            snapped.Add(raw[n - 1]);

            // ── Stage 2 (separable, LAST): despike → σ-smooth (open, endpoints pinned) → watertight ladder ──
            IReadOnlyList<WzVec2> despiked = options.DespikeThreshold > 0
                ? PolylineSmoother.Despike(snapped, options.DespikeThreshold)
                : snapped;

            if (options.SmoothingSigmaMeters <= 0 || n < options.MinVertsToSmooth)
                return NonCrossingOrRaw(despiked, raw);

            var smoothed = PolylineSmoother.SmoothGaussian(despiked, options.SmoothingSigmaMeters);
            if (!HasSelfIntersectionOpen(smoothed)) return smoothed;
            if (!HasSelfIntersectionOpen(despiked)) return despiked;
            return raw;   // total backstop: the axis-aligned coarse arc cannot self-cross
        }

        private static IReadOnlyList<WzVec2> NonCrossingOrRaw(IReadOnlyList<WzVec2> despiked, IReadOnlyList<WzVec2> raw)
            => HasSelfIntersectionOpen(despiked) ? raw : despiked;

        // ── Snap marches (same algorithm as RegionRingRefiner; local copy keeps the seam path decoupled) ──
        private static bool SnapToIso(IScalarField f, double px, double pz, double nx, double nz,
                                      SharedSeamRefineOptions opt, out double bestS)
        {
            bestS = 0; double iso = f.IsoLevel;
            double F(double s) => f.Sample(px + s * nx, pz + s * nz) - iso;
            double f0 = F(0); if (Math.Abs(f0) < 1e-9) return true;
            double best = double.MaxValue; bool found = false;
            foreach (int dir in new[] { 1, -1 })
            {
                double prevS = 0, prevF = f0;
                for (double step = opt.MarchStep; step <= opt.MaxDisplacement + 1e-9; step += opt.MarchStep)
                {
                    double s = dir * step, fs = F(s);
                    if (Math.Sign(fs) != Math.Sign(prevF) && Math.Sign(fs) != 0)
                    { double sc = Bisect(F, prevS, s, prevF, fs); if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; } break; }
                    if (Math.Abs(fs) < 1e-9) { if (Math.Abs(s) < best) { best = Math.Abs(s); bestS = s; found = true; } break; }
                    prevS = s; prevF = fs;
                }
            }
            return found;
        }

        private static bool SnapToFlip(ICategoryField f, double px, double pz, double nx, double nz,
                                       SharedSeamRefineOptions opt, out double bestS)
        {
            bestS = 0; int c0 = f.CategoryAt(px, pz);
            double best = double.MaxValue; bool found = false;
            foreach (int dir in new[] { 1, -1 })
            {
                int prevCat = c0; double prevS = 0;
                for (double step = opt.MarchStep; step <= opt.MaxDisplacement + 1e-9; step += opt.MarchStep)
                {
                    double s = dir * step; int cat = f.CategoryAt(px + s * nx, pz + s * nz);
                    if (cat != prevCat)
                    {
                        double s0 = prevS, s1 = s;
                        for (int it = 0; it < 20; it++) { double mid = 0.5 * (s0 + s1); int cm = f.CategoryAt(px + mid * nx, pz + mid * nz); if (cm == c0) s0 = mid; else s1 = mid; }
                        double sc = 0.5 * (s0 + s1);
                        if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; }
                        break;
                    }
                    prevCat = cat; prevS = s;
                }
            }
            return found;
        }

        private static double Bisect(Func<double, double> f, double s0, double s1, double f0, double f1)
        {
            for (int i = 0; i < 24; i++)
            { double mid = 0.5 * (s0 + s1), fm = f(mid); if (Math.Abs(fm) < 1e-9) return mid; if (Math.Sign(fm) == Math.Sign(f0)) { s0 = mid; f0 = fm; } else { s1 = mid; f1 = fm; } }
            return 0.5 * (s0 + s1);
        }

        // Self-intersection of an OPEN polyline (no wrap edge n-1→0). O(n²); seams are short, runs once at build.
        private static bool HasSelfIntersectionOpen(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count;
            for (int i = 0; i < n - 1; i++)
                for (int j = i + 2; j < n - 1; j++)   // j = i+2 skips the adjacent segment that shares a vertex
                {
                    if (SegInt(v[i], v[i + 1], v[j], v[j + 1])) return true;
                }
            return false;
        }

        private static bool SegInt(WzVec2 p, WzVec2 p2, WzVec2 q, WzVec2 q2)
        {
            double d1 = Cross(q, q2, p), d2 = Cross(q, q2, p2), d3 = Cross(p, p2, q), d4 = Cross(p, p2, q2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        private static double Cross(WzVec2 a, WzVec2 b, WzVec2 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
    }
}
