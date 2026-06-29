using System;
using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// Tunables for <see cref="RegionRingRefiner"/>. Defaults are the spike-validated values
    /// (2026-06-29, seed bkpcEynZXm3 sweep over 194 rings).
    /// </summary>
    public sealed class RingRefineOptions
    {
        /// <summary>Max perpendicular distance (m) a ring vertex may move to reach its iso/flip. 40 m
        /// (a bit over one zone half) — same bound the ink uses.</summary>
        public double MaxDisplacement { get; set; } = 40.0;

        /// <summary>Perpendicular march step (m) when searching for the crossing. 4 m.</summary>
        public double MarchStep { get; set; } = 4.0;

        /// <summary>Despike threshold (m): a smoothed point further than this from its neighbour midpoint
        /// is a snap spur, pulled back. 24 m. 0 disables despiking.</summary>
        public double DespikeThreshold { get; set; } = 24.0;

        /// <summary>Chaikin corner-cut iterations (the LAST, separable jaggies-removal stage). 2.
        /// 0 disables smoothing entirely (refine-only authoritative ring).</summary>
        public int SmoothIterations { get; set; } = 2;

        /// <summary>GUARD 1a — a ring with fewer raw vertices than this skips smoothing (kept refined).
        /// The sweep showed failures concentrate at tiny rings (median 4 verts) where Chaikin has nothing
        /// to cut and self-intersects. 8 keeps every speck watertight as a refined ring.</summary>
        public int MinVertsToSmooth { get; set; } = 8;

        /// <summary>GUARD 1b — a ring whose |area| (m²) is below this skips smoothing (kept refined).
        /// Backstop for a high-vert-count but physically tiny sliver. 20000 m² = 0.02 km².</summary>
        public double MinAreaToSmooth { get; set; } = 20000.0;

        public static RingRefineOptions Default => new RingRefineOptions();
    }

    /// <summary>Which path produced a <see cref="RefinedRing"/> — for auditability + tests.</summary>
    public enum RingRefineOutcome
    {
        /// <summary>Refined + smoothed cleanly (the common case for real region bodies).</summary>
        Smoothed,
        /// <summary>Smoothing was skipped by the size guard (tiny ring) — vertices are refined only.</summary>
        SkippedSmoothTooSmall,
        /// <summary>Smoothing self-intersected and was ROLLED BACK to the refined (unsmoothed) ring.</summary>
        RolledBackSelfIntersect,
        /// <summary>Both smoothed AND refined self-intersected; rolled back to the RAW source ring (the
        /// axis-aligned zone-edge loop, which is simple by construction). The total-watertight backstop.</summary>
        RolledBackToRaw,
    }

    /// <summary>The authoritative refined boundary loop for one region ring (outer or hole).</summary>
    public sealed class RefinedRing
    {
        /// <summary>Durable region key this ring bounds.</summary>
        public string RegionKey { get; }

        /// <summary>The final authoritative loop vertices (world m, implicitly closed, first != last).</summary>
        public IReadOnlyList<WzVec2> Vertices { get; }

        /// <summary>Signed area (m²); sign matches the source ring (CCW outer / CW hole) — guaranteed by
        /// the watertight guarantee (winding is never flipped).</summary>
        public double SignedArea { get; }

        /// <summary>True if this is a hole (CW) inside the region's outer ring.</summary>
        public bool IsHole => this.SignedArea < 0.0;

        /// <summary>How this ring was produced (see <see cref="RingRefineOutcome"/>).</summary>
        public RingRefineOutcome Outcome { get; }

        /// <summary>Count of vertices actually displaced onto a contour (vs left on the lattice).</summary>
        public int HuggedVertices { get; }

        public RefinedRing(string regionKey, IReadOnlyList<WzVec2> vertices, double signedArea,
                           RingRefineOutcome outcome, int huggedVertices)
        {
            this.RegionKey = regionKey;
            this.Vertices = vertices;
            this.SignedArea = signedArea;
            this.Outcome = outcome;
            this.HuggedVertices = huggedVertices;
        }
    }

    /// <summary>
    /// Promotes a coarse 64 m <see cref="RegionRing"/> into the AUTHORITATIVE refined boundary loop:
    /// each ring vertex is snapped along its local normal onto the contour its adjacent edges imply
    /// (a COAST edge → a height isoline via <see cref="IScalarField"/>; a region-vs-region SEAM edge →
    /// a biome-category flip via <see cref="ICategoryField"/>), then despiked + Chaikin-smoothed as a
    /// SEPARABLE LAST stage. Two guards keep EVERY ring watertight (validated 2026-06-29 over all 194
    /// rings of seed bkpcEynZXm3): a size floor that skips smoothing on tiny specks (where Chaikin
    /// self-intersects), and a self-intersection ROLLBACK to the refined-but-unsmoothed ring (which the
    /// sweep proved never self-intersects). Closure is topological so refinement can never open a gap.
    ///
    /// <para>Per DECISION 2026-06-29 (Daniel), this ring is the source of truth for region MEMBERSHIP
    /// (point-in-polygon) and the gazetteer; the raster fill mask is a 2D-map render CONSUMER of it.
    /// Pure Tier-1 (consumes the field seams; no Unity / no sampler import) so it is headless-testable.
    /// See docs/design/region-render-seam.md "DECISION 2026-06-29".</para>
    /// </summary>
    public static class RegionRingRefiner
    {
        /// <summary>
        /// Classify ring edge <c>i</c> (vertices[i]→vertices[i+1]) as coast / seam / interior by sampling
        /// the region-id field 32 m to EACH side of the edge midpoint. The side that is NOT this region is
        /// the exterior: exterior &lt; 0 ⇒ COAST (region-vs-void), exterior is another region ⇒ SEAM.
        /// Sampling both sides removes any winding-convention dependency.
        /// </summary>
        /// <param name="regionIdAt">World-space region-id lookup (the coarse 64 m grid is fine here —
        ///   classification only needs to know which side is exterior).</param>
        public delegate int RegionIdAt(double worldX, double worldZ);

        private enum EdgeKind { Coast, Seam, Interior }

        /// <summary>
        /// Refine one ring into its authoritative refined loop.
        /// </summary>
        /// <param name="ring">The coarse 64 m source ring (outer or hole).</param>
        /// <param name="regionLabel">The ring's region int label (TransientId) for edge classification.</param>
        /// <param name="regionIdAt">Region-id world lookup for edge classification.</param>
        /// <param name="coastField">Height field whose <see cref="IScalarField.IsoLevel"/> the coast hugs.</param>
        /// <param name="seamField">Biome-category field a region-vs-region seam hugs.</param>
        /// <param name="options">Tunables (null = <see cref="RingRefineOptions.Default"/>).</param>
        public static RefinedRing Refine(RegionRing ring, int regionLabel, RegionIdAt regionIdAt,
            IScalarField coastField, ICategoryField seamField, RingRefineOptions options = null)
        {
            if (ring == null) throw new ArgumentNullException(nameof(ring));
            if (regionIdAt == null) throw new ArgumentNullException(nameof(regionIdAt));
            if (coastField == null) throw new ArgumentNullException(nameof(coastField));
            if (seamField == null) throw new ArgumentNullException(nameof(seamField));
            options ??= RingRefineOptions.Default;

            var raw = ring.Vertices;
            int n = raw.Count;
            if (n < 3)
                return new RefinedRing(ring.RegionKey, new List<WzVec2>(raw), ring.SignedArea,
                                       RingRefineOutcome.SkippedSmoothTooSmall, 0);

            // ── Stage 1: classify edges, then snap each vertex along its local normal ──────────────────
            EdgeKind[] kind = ClassifyEdges(raw, regionLabel, regionIdAt);
            var refined = new List<WzVec2>(n);
            int hugged = 0;
            for (int i = 0; i < n; i++)
            {
                EdgeKind prev = kind[(i - 1 + n) % n], cur = kind[i];
                bool coast = prev == EdgeKind.Coast || cur == EdgeKind.Coast;       // coast wins at junctions
                bool seam = !coast && (prev == EdgeKind.Seam || cur == EdgeKind.Seam);

                WzVec2 a = raw[(i - 1 + n) % n], b = raw[(i + 1) % n];
                double tx = b.X - a.X, tz = b.Z - a.Z;
                double tl = Math.Sqrt(tx * tx + tz * tz);
                if (tl < 1e-9) { refined.Add(raw[i]); continue; }
                double nx = -tz / tl, nz = tx / tl;

                double s; bool snapped;
                if (coast) snapped = SnapToIso(coastField, raw[i].X, raw[i].Z, nx, nz, options, out s);
                else if (seam) snapped = SnapToFlip(seamField, raw[i].X, raw[i].Z, nx, nz, options, out s);
                else { s = 0; snapped = false; }

                if (snapped && Math.Abs(s) > 1e-6) { refined.Add(new WzVec2(raw[i].X + s * nx, raw[i].Z + s * nz)); hugged++; }
                else refined.Add(raw[i]);
            }

            double areaRefined = SignedArea(refined);

            // ── Stage 2 (LAST, separable): smooth — GUARDED ───────────────────────────────────────────
            // GUARD 1: size floor. Tiny rings (specks) skip smoothing; Chaikin has nothing to round and
            // self-intersects (sweep: failures median 4 verts / 0.008 km²). Refined ring is authoritative.
            bool tooSmall = n < options.MinVertsToSmooth || Math.Abs(areaRefined) < options.MinAreaToSmooth;
            if (options.SmoothIterations <= 0 || tooSmall)
                return new RefinedRing(ring.RegionKey, refined, areaRefined,
                                       RingRefineOutcome.SkippedSmoothTooSmall, hugged);

            var smoothed = Chaikin(Despike(refined, options.DespikeThreshold), options.SmoothIterations);

            // GUARD 2: watertight ladder. Prefer smoothed; if it self-intersects or flips winding, fall
            // back to refined; if the REFINED ring ALSO self-intersects (rare: a large ring where the snap
            // pushed two coastline vertices past each other — seen on ForTheWort r.-19.-1, NOT in the
            // bkpcEynZXm3 sweep), fall back to the RAW source ring, which is axis-aligned zone edges and
            // CANNOT self-intersect by construction. The raw ring is the bedrock that always holds, so the
            // watertight guarantee is total — never just "usually."
            bool smoothOk = (Math.Sign(SignedArea(smoothed)) == Math.Sign(areaRefined) || Math.Abs(areaRefined) < 1e-6)
                            && !HasSelfIntersection(smoothed);
            if (smoothOk)
                return new RefinedRing(ring.RegionKey, smoothed, SignedArea(smoothed),
                                       RingRefineOutcome.Smoothed, hugged);

            if (!HasSelfIntersection(refined))
                return new RefinedRing(ring.RegionKey, refined, areaRefined,
                                       RingRefineOutcome.RolledBackSelfIntersect, hugged);

            // Refined also crosses — drop to the raw source ring (guaranteed simple).
            var rawCopy = new List<WzVec2>(raw);
            return new RefinedRing(ring.RegionKey, rawCopy, ring.SignedArea,
                                   RingRefineOutcome.RolledBackToRaw, 0);
        }

        // ── Edge classification ────────────────────────────────────────────────────────────────────────
        private static EdgeKind[] ClassifyEdges(IReadOnlyList<WzVec2> v, int label, RegionIdAt ridAt)
        {
            int n = v.Count; var kinds = new EdgeKind[n];
            for (int i = 0; i < n; i++)
            {
                WzVec2 a = v[i], b = v[(i + 1) % n];
                double mx = (a.X + b.X) * 0.5, mz = (a.Z + b.Z) * 0.5;
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-9) { kinds[i] = EdgeKind.Interior; continue; }
                double nx = -dz / l, nz = dx / l;
                const double step = 32.0;                          // into the adjacent zone centre
                int sideP = ridAt(mx + nx * step, mz + nz * step);
                int sideN = ridAt(mx - nx * step, mz - nz * step);
                int exterior;
                if (sideP == label && sideN != label) exterior = sideN;
                else if (sideN == label && sideP != label) exterior = sideP;
                else exterior = (sideP != label) ? sideP : sideN; // fallback
                kinds[i] = exterior < 0 ? EdgeKind.Coast : (exterior != label ? EdgeKind.Seam : EdgeKind.Interior);
            }
            return kinds;
        }

        // ── Snap marches (shared math with RegionBoundaryRefiner; kept local to keep the ring path pure) ─
        private static bool SnapToIso(IScalarField f, double px, double pz, double nx, double nz,
                                      RingRefineOptions opt, out double bestS)
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
                                       RingRefineOptions opt, out double bestS)
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

        // ── Closed-ring despike + Chaikin (the shipped PolylineSmoother is OPEN-curve; a ring needs these
        //    or it kinks at the closure seam — finding from the 2026-06-29 spike) ─────────────────────────
        private static WzVec2 Lerp(WzVec2 a, WzVec2 b, double t) => new WzVec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        private static List<WzVec2> Despike(IReadOnlyList<WzVec2> p, double maxDev)
        {
            int n = p.Count; var o = new List<WzVec2>(n); if (n < 3) { o.AddRange(p); return o; }
            if (maxDev <= 0) { o.AddRange(p); return o; }
            double thr2 = maxDev * maxDev;
            for (int i = 0; i < n; i++)
            {
                WzVec2 m = Lerp(p[(i - 1 + n) % n], p[(i + 1) % n], 0.5);
                double dx = p[i].X - m.X, dz = p[i].Z - m.Z;
                o.Add(dx * dx + dz * dz > thr2 ? m : p[i]);
            }
            return o;
        }

        private static List<WzVec2> Chaikin(IReadOnlyList<WzVec2> p, int iterations)
        {
            var cur = new List<WzVec2>(p);
            for (int it = 0; it < iterations; it++)
            {
                int n = cur.Count; if (n < 3) break;
                var o = new List<WzVec2>(n * 2);
                for (int i = 0; i < n; i++) { WzVec2 a = cur[i], b = cur[(i + 1) % n]; o.Add(Lerp(a, b, 0.25)); o.Add(Lerp(a, b, 0.75)); }
                cur = o;
            }
            return cur;
        }

        // ── Geometry: signed area + self-intersection (O(n²); rings are small, runs at world-build only) ─
        private static double SignedArea(IReadOnlyList<WzVec2> v)
        { double s = 0; int n = v.Count; for (int i = 0; i < n; i++) { WzVec2 a = v[i], b = v[(i + 1) % n]; s += a.X * b.Z - b.X * a.Z; } return s / 2.0; }

        private static bool HasSelfIntersection(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 a1 = v[i], a2 = v[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1) continue;                 // adjacent share a vertex
                    if (i == 0 && j == n - 1) continue;       // wrap-adjacent share a vertex
                    if (SegInt(a1, a2, v[j], v[(j + 1) % n])) return true;
                }
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
