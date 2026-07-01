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

        /// <summary>
        /// Arc-length Gaussian low-pass — the terrain-scale smoothing dial (docs/design/
        /// region-boundary-negotiation.md). Unlike <see cref="Chaikin"/> (whose effect depends on vertex
        /// spacing, a grid artifact), the width <paramref name="sigmaMeters"/> is a REAL METRIC distance:
        /// the curve is first resampled to a uniform arc-length step, then each interior point is replaced
        /// by a Gaussian-weighted average of its neighbours with standard deviation σ metres. Physical
        /// meaning: wiggles smaller than ~σ m (Perlin/pixel noise) are smoothed away; features bigger than
        /// ~σ m (real headlands) survive. So σ means the same thing across seeds and vertex densities —
        /// the honest "filter at the scale of terrain, not the scale of the grid" knob.
        ///
        /// Endpoints are PINNED (junctions where borders meet must not drift). σ ≤ 0 → identity (the
        /// off-by-default legacy path). The kernel is truncated at ±3σ. Deterministic: same input +
        /// same σ + same resampleStep ⇒ same output (pure double arithmetic, fixed traversal order).
        ///
        /// Shipped default σ = 30 m: organic without lying about where the feature is (σ ≳ 40 m starts
        /// cutting across the real biome edge — the banned "invented curve"). Walk-tunable.
        /// </summary>
        /// <param name="pts">The refined (already contour-snapped + despiked) polyline.</param>
        /// <param name="sigmaMeters">Gaussian width in metres of real terrain. ≤ 0 returns the input.</param>
        /// <param name="resampleStep">Uniform arc-length resample step (metres) applied before filtering,
        /// so σ is measured in true distance regardless of the input's vertex spacing. Default 2 m.</param>
        public static IReadOnlyList<WzVec2> SmoothGaussian(
            IReadOnlyList<WzVec2> pts, double sigmaMeters = 30.0, double resampleStep = 2.0)
        {
            if (sigmaMeters <= 0 || pts.Count < 3 || resampleStep <= 0) return pts;

            IReadOnlyList<WzVec2> uni = ResampleUniform(pts, resampleStep);
            int n = uni.Count;
            if (n < 3) return pts;

            // Truncated Gaussian kernel, indices in resample-steps (so σ/step converts metres → taps).
            int half = (int)Math.Ceiling(3.0 * sigmaMeters / resampleStep);
            if (half < 1) return uni;
            var kernel = new double[2 * half + 1];
            double twoSigma2 = 2.0 * sigmaMeters * sigmaMeters;
            for (int j = -half; j <= half; j++)
            {
                double d = j * resampleStep;
                kernel[j + half] = Math.Exp(-(d * d) / twoSigma2);
            }

            var outp = new List<WzVec2>(n) { uni[0] };   // pinned start
            for (int i = 1; i < n - 1; i++)
            {
                double sx = 0, sz = 0, wsum = 0;
                int lo = -Math.Min(half, i);
                int hi = Math.Min(half, n - 1 - i);
                for (int j = lo; j <= hi; j++)
                {
                    double w = kernel[j + half];
                    WzVec2 q = uni[i + j];
                    sx += q.X * w; sz += q.Z * w; wsum += w;
                }
                outp.Add(new WzVec2(sx / wsum, sz / wsum));
            }
            outp.Add(uni[n - 1]);   // pinned end
            return outp;
        }

        /// <summary>
        /// Resample a polyline to a uniform arc-length step (metres), preserving both endpoints. Used by
        /// <see cref="SmoothGaussian"/> so the Gaussian width is a true metric distance independent of the
        /// input's (non-uniform) vertex spacing. Points closer than <paramref name="step"/> are absorbed;
        /// long segments are subdivided. Pure + deterministic.
        /// </summary>
        public static IReadOnlyList<WzVec2> ResampleUniform(IReadOnlyList<WzVec2> pts, double step)
        {
            if (pts.Count < 2 || step <= 0) return pts;
            var outp = new List<WzVec2> { pts[0] };
            double carried = 0;   // arc length accumulated since the last emitted point
            for (int i = 1; i < pts.Count; i++)
            {
                WzVec2 a = pts[i - 1], b = pts[i];
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double seg = Math.Sqrt(dx * dx + dz * dz);
                if (seg < 1e-9) continue;
                double pos = 0;   // distance advanced along THIS segment
                while (carried + (seg - pos) >= step)
                {
                    double need = step - carried;
                    pos += need;
                    double t = pos / seg;
                    outp.Add(new WzVec2(a.X + t * dx, a.Z + t * dz));
                    carried = 0;
                }
                carried += seg - pos;
            }
            outp.Add(pts[pts.Count - 1]);
            return outp;
        }

        /// <summary>
        /// CLOSED-LOOP arc-length Gaussian low-pass — the fill-ring sibling of <see cref="SmoothGaussian"/>
        /// (docs/design/region-boundary-negotiation.md, "SUPERSEDED-2" note). A region fill ring is a CLOSED
        /// membership loop (implicitly closed, first != last); the OPEN <see cref="SmoothGaussian"/> pins its
        /// endpoints and would KINK at the closure seam. This version pins NOTHING and wraps the Gaussian
        /// window MODULO the vertex count, so the closure seam is smoothed exactly like every other point and
        /// the loop stays organic all the way around. Same physical knob: <paramref name="sigmaMeters"/> is a
        /// REAL METRIC width (metres of terrain) — noise below ~σ is filtered, real headlands above ~σ survive
        /// — because the loop is first resampled to a uniform arc-length step (<see cref="ResampleUniformClosed"/>).
        ///
        /// σ ≤ 0 → identity (the off-by-default legacy path; the caller keeps its closed Chaikin). The kernel is
        /// truncated at ±3σ and clamped to at most ⌊n/2⌋ taps so the wrapping window never double-counts a
        /// vertex on a small loop. Deterministic: same input + same σ + same step ⇒ same output (pure double
        /// arithmetic, fixed traversal order). Winding is preserved (a convex, positive-weight average cannot
        /// flip orientation for a sane σ; the caller's self-intersection ladder is the total backstop).
        ///
        /// Shipped tuning target σ = 30 m (matches the ink), walk-gated. Output keeps the first != last closed
        /// convention (no duplicated closure vertex).
        /// </summary>
        /// <param name="pts">The refined + closed-despiked ring loop (implicitly closed, first != last).</param>
        /// <param name="sigmaMeters">Gaussian width in metres of real terrain. ≤ 0 returns the input.</param>
        /// <param name="resampleStep">Uniform arc-length resample step (m) applied before filtering, so σ is a
        /// true metric distance regardless of the ring's (non-uniform) vertex spacing. Default 2 m.</param>
        public static IReadOnlyList<WzVec2> SmoothGaussianClosed(
            IReadOnlyList<WzVec2> pts, double sigmaMeters = 30.0, double resampleStep = 2.0)
        {
            if (sigmaMeters <= 0 || pts.Count < 3 || resampleStep <= 0) return pts;

            IReadOnlyList<WzVec2> uni = ResampleUniformClosed(pts, resampleStep);
            int n = uni.Count;
            if (n < 3) return pts;

            // Kernel half-width in resample-steps (σ/step converts metres → taps). Clamp to ⌊n/2⌋ so the
            // wrapping window never samples the same vertex twice on a small loop.
            int half = (int)Math.Ceiling(3.0 * sigmaMeters / resampleStep);
            if (half < 1) return uni;
            if (half > n / 2) half = n / 2;
            var kernel = new double[2 * half + 1];
            double twoSigma2 = 2.0 * sigmaMeters * sigmaMeters;
            for (int j = -half; j <= half; j++)
            {
                double d = j * resampleStep;
                kernel[j + half] = Math.Exp(-(d * d) / twoSigma2);
            }

            var outp = new List<WzVec2>(n);
            for (int i = 0; i < n; i++)   // EVERY point smoothed — no pinned endpoints (closed loop)
            {
                double sx = 0, sz = 0, wsum = 0;
                for (int j = -half; j <= half; j++)
                {
                    double w = kernel[j + half];
                    WzVec2 q = uni[((i + j) % n + n) % n];   // modular wrap across the closure seam
                    sx += q.X * w; sz += q.Z * w; wsum += w;
                }
                outp.Add(new WzVec2(sx / wsum, sz / wsum));
            }
            return outp;
        }

        /// <summary>
        /// Resample a CLOSED loop to a uniform arc-length step (metres), keeping the first != last convention.
        /// The closing edge (last→first) is included in the walk, so the spacing is uniform all the way around
        /// (the residual sub-step gap folds into the closure and is smoothed over by the wrapping Gaussian — it
        /// never becomes a kink). Anchored at <c>pts[0]</c> for determinism. Used by
        /// <see cref="SmoothGaussianClosed"/> so σ is a true metric width independent of vertex spacing.
        /// </summary>
        public static IReadOnlyList<WzVec2> ResampleUniformClosed(IReadOnlyList<WzVec2> pts, double step)
        {
            int n = pts.Count;
            if (n < 3 || step <= 0) return pts;
            var outp = new List<WzVec2> { pts[0] };
            double carried = 0;   // arc length accumulated since the last emitted point
            for (int i = 0; i < n; i++)   // edges i → i+1, INCLUDING the closing edge n-1 → 0
            {
                WzVec2 a = pts[i], b = pts[(i + 1) % n];
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double seg = Math.Sqrt(dx * dx + dz * dz);
                if (seg < 1e-9) continue;
                double pos = 0;   // distance advanced along THIS edge
                while (carried + (seg - pos) >= step)
                {
                    double need = step - carried;
                    pos += need;
                    double t = pos / seg;
                    outp.Add(new WzVec2(a.X + t * dx, a.Z + t * dz));
                    carried = 0;
                }
                carried += seg - pos;
            }
            // The walk ends near pts[0] (the closure). Drop a trailing near-duplicate of the start so the
            // output keeps first != last cleanly (the Gaussian smooths the tiny residual gap regardless).
            if (outp.Count > 3)
            {
                WzVec2 last = outp[outp.Count - 1];
                double dx = last.X - outp[0].X, dz = last.Z - outp[0].Z;
                double halfStep = step * 0.5;
                if (dx * dx + dz * dz < halfStep * halfStep) outp.RemoveAt(outp.Count - 1);
            }
            return outp;
        }
    }
}
