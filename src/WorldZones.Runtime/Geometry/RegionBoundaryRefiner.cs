using System;
using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>Tunables for <see cref="RegionBoundaryRefiner"/> sub-zone contour-hugging.</summary>
    public sealed class SegmentRefineOptions
    {
        /// <summary>Number of sub-intervals each 64 m boundary segment is split into before snapping.
        /// More = smoother curve, more samples. Default 4 (16 m steps).</summary>
        public int Subdivisions { get; set; } = 4;

        /// <summary>Max perpendicular distance (metres) a sub-point may move to reach the isoline.
        /// Bounded so refinement stays local to the boundary cell. Default 40 (a bit over one zone half).</summary>
        public double MaxDisplacement { get; set; } = 40.0;

        /// <summary>Perpendicular march step (metres) when searching for the iso crossing. Default 4.</summary>
        public double MarchStep { get; set; } = 4.0;

        public static SegmentRefineOptions Default => new SegmentRefineOptions();
    }

    /// <summary>
    /// A boundary segment refined to hug a real terrain contour at sub-64 m resolution — the
    /// "contour-hug" half of the border model (docs/design/region-borders.md). Carries the same durable
    /// key pair as its source <see cref="BorderSegment"/>, so a consumer strokes it identically; the
    /// difference is the <see cref="Polyline"/> traces the feature instead of the zone-edge staircase.
    /// </summary>
    public sealed class RefinedBorder
    {
        /// <summary>The refined world-space polyline (≥2 points), hugging the contour where one exists.</summary>
        public IReadOnlyList<WzVec2> Polyline { get; }

        /// <summary>Durable key of one bounding region (ordinally lesser). Never null.</summary>
        public string KeyA { get; }

        /// <summary>Durable key of the other bounding region, or null for a coastline (region-vs-void).</summary>
        public string KeyB { get; }

        /// <summary>True if this segment was actually displaced onto a contour (vs. left on the lattice).</summary>
        public bool Hugged { get; }

        public RefinedBorder(IReadOnlyList<WzVec2> polyline, string keyA, string keyB, bool hugged)
        {
            this.Polyline = polyline;
            this.KeyA = keyA;
            this.KeyB = keyB;
            this.Hugged = hugged;
        }
    }

    /// <summary>
    /// Refines coarse 64 m boundary segments into sub-zone contour-hugging polylines by snapping each
    /// segment's sample points onto a scalar isoline (e.g. the sea-level height contour for a coast).
    /// Pure Tier-1: the field is an <see cref="IScalarField"/> supplied by the consumer
    /// (<c>WorldZones.Runtime</c> wraps the world sampler), so the marching math is headless-testable
    /// with synthetic fields. This is an ADDITIVE detail layer — the coarse
    /// <see cref="RegionBoundaryGraph"/> (the deterministic 64 m substrate gameplay keys off) is
    /// unchanged; this just draws the experienced edge richer. See docs/design/region-render-seam.md.
    /// </summary>
    public static class RegionBoundaryRefiner
    {
        /// <summary>
        /// Refine every coastline segment (region-vs-void, <see cref="BorderSegment.IsCoastline"/>) to
        /// hug the given height isoline. The dominant visible case for an archipelago world: the coast
        /// staircase becomes the real shoreline.
        /// </summary>
        public static IReadOnlyList<RefinedBorder> RefineCoastlines(
            RegionBoundaryGraph graph, IScalarField heightField, SegmentRefineOptions options = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            options ??= SegmentRefineOptions.Default;

            var result = new List<RefinedBorder>();
            foreach (BorderSegment seg in graph.Segments)
            {
                if (!seg.IsCoastline) continue;
                result.Add(RefineSegment(seg, heightField, options));
            }
            return result;
        }

        /// <summary>
        /// Refine one segment by snapping its sub-points to the field's isoline along the segment normal.
        /// Where no crossing exists within <see cref="SegmentRefineOptions.MaxDisplacement"/>, the point
        /// stays on the lattice — the honest "arbitrary firm line where no feature" degenerate case.
        /// </summary>
        public static RefinedBorder RefineSegment(BorderSegment seg, IScalarField field, SegmentRefineOptions options)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            options ??= SegmentRefineOptions.Default;

            double ax = seg.A.X, az = seg.A.Z, bx = seg.B.X, bz = seg.B.Z;
            double dx = bx - ax, dz = bz - az;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-9)
                return new RefinedBorder(new[] { seg.A, seg.B }, seg.KeyA, seg.KeyB, false);

            // Unit segment direction, and the perpendicular (segment normal) to march along.
            double ux = dx / len, uz = dz / len;
            double nx = -uz, nz = ux;

            int n = Math.Max(1, options.Subdivisions);
            var pts = new List<WzVec2>(n + 1);
            bool anyHug = false;

            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double px = ax + t * dx;
                double pz = az + t * dz;

                if (TrySnapToIso(px, pz, nx, nz, field, options, out double s))
                {
                    pts.Add(new WzVec2(px + s * nx, pz + s * nz));
                    if (Math.Abs(s) > 1e-6) anyHug = true;
                }
                else
                {
                    pts.Add(new WzVec2(px, pz)); // no contour here → honest lattice point
                }
            }

            return new RefinedBorder(pts, seg.KeyA, seg.KeyB, anyHug);
        }

        // Find the signed perpendicular displacement to the nearest isoline crossing within ±MaxDisplacement.
        private static bool TrySnapToIso(
            double px, double pz, double nx, double nz, IScalarField field, SegmentRefineOptions opt, out double bestS)
        {
            bestS = 0.0;
            double iso = field.IsoLevel;
            double F(double s) => field.Sample(px + s * nx, pz + s * nz) - iso;

            double f0 = F(0.0);
            if (Math.Abs(f0) < 1e-9) return true; // already on the isoline

            double best = double.MaxValue;
            bool found = false;

            // Search both perpendicular directions; keep the crossing nearest the lattice point.
            foreach (int dir in new[] { 1, -1 })
            {
                double prevS = 0.0, prevF = f0;
                for (double step = opt.MarchStep; step <= opt.MaxDisplacement + 1e-9; step += opt.MarchStep)
                {
                    double s = dir * step;
                    double fs = F(s);
                    if (Math.Sign(fs) != Math.Sign(prevF) && Math.Sign(fs) != 0)
                    {
                        double sc = Bisect(F, prevS, s, prevF, fs);
                        if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; }
                        break;
                    }
                    if (Math.Abs(fs) < 1e-9) { if (Math.Abs(s) < best) { best = Math.Abs(s); bestS = s; found = true; } break; }
                    prevS = s; prevF = fs;
                }
            }
            return found;
        }

        // Bisection for the iso crossing between two bracketing perpendicular offsets.
        private static double Bisect(Func<double, double> f, double s0, double s1, double f0, double f1)
        {
            for (int i = 0; i < 24; i++) // ~ sub-millimetre on a 40 m bracket
            {
                double mid = 0.5 * (s0 + s1);
                double fm = f(mid);
                if (Math.Abs(fm) < 1e-9) return mid;
                if (Math.Sign(fm) == Math.Sign(f0)) { s0 = mid; f0 = fm; }
                else { s1 = mid; f1 = fm; }
            }
            return 0.5 * (s0 + s1);
        }
    }
}
