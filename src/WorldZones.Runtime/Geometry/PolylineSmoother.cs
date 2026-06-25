using System;
using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// Pure polyline smoothing for sub-zone boundary detail — removes the jaggies left by per-segment
    /// contour snapping. Two operators, composable: <see cref="Despike"/> (kills the perpendicular
    /// spurs the snapper produces at sharp inlets) and <see cref="Chaikin"/> (corner-cutting that
    /// rounds the residual 64 m staircase). Tier-1, no Unity, deterministic, local (short-window) so
    /// the macro shape survives. See docs/design/region-render-seam.md.
    /// </summary>
    public static class PolylineSmoother
    {
        private static WzVec2 Lerp(WzVec2 a, WzVec2 b, double t)
            => new WzVec2(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

        /// <summary>
        /// Pull outlier points back toward the midpoint of their neighbours. A point is a "spike" when
        /// its distance from the neighbour-midpoint exceeds <paramref name="maxDeviation"/> metres —
        /// exactly the per-segment snapping spur (one point that darted to a far contour crossing while
        /// its neighbours stayed on the real coast). One deterministic pass; endpoints are preserved.
        /// </summary>
        public static IReadOnlyList<WzVec2> Despike(IReadOnlyList<WzVec2> pts, double maxDeviation)
        {
            int n = pts.Count;
            if (n < 3) return pts;
            double thr2 = maxDeviation * maxDeviation;
            var outp = new List<WzVec2>(n) { pts[0] };
            for (int i = 1; i < n - 1; i++)
            {
                WzVec2 m = Lerp(pts[i - 1], pts[i + 1], 0.5);
                double dx = pts[i].X - m.X, dz = pts[i].Z - m.Z;
                outp.Add(dx * dx + dz * dz > thr2 ? m : pts[i]);
            }
            outp.Add(pts[n - 1]);
            return outp;
        }

        /// <summary>
        /// Chaikin corner-cutting. Each iteration replaces every edge with its 1/4 and 3/4 points,
        /// rounding the staircase; open-curve endpoints are preserved. Output stays within the convex
        /// hull of the input — it can ONLY cut corners, never add a new excursion, so a smoothed border
        /// cannot wander off the feature it hugs. 2 iterations is usually enough. Each iteration ~2×
        /// the point count, so keep it small for per-frame consumers.
        /// </summary>
        public static IReadOnlyList<WzVec2> Chaikin(IReadOnlyList<WzVec2> pts, int iterations = 2)
        {
            IReadOnlyList<WzVec2> cur = pts;
            for (int it = 0; it < iterations; it++)
            {
                int n = cur.Count;
                if (n < 3) break;
                var outp = new List<WzVec2>(n * 2) { cur[0] };
                for (int i = 0; i < n - 1; i++)
                {
                    outp.Add(Lerp(cur[i], cur[i + 1], 0.25));
                    outp.Add(Lerp(cur[i], cur[i + 1], 0.75));
                }
                outp.Add(cur[n - 1]);
                cur = outp;
            }
            return cur;
        }

        /// <summary>Despike then Chaikin — the standard cleanup for a refined boundary polyline.</summary>
        public static IReadOnlyList<WzVec2> Smooth(IReadOnlyList<WzVec2> pts, double maxSpike = 24.0, int chaikinIterations = 2)
            => Chaikin(Despike(pts, maxSpike), chaikinIterations);
    }
}
