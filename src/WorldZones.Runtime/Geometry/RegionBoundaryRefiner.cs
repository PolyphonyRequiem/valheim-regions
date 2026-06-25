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

        /// <summary>
        /// When refining, chain contiguous boundary segments into continuous arcs BEFORE snapping +
        /// smoothing, so jaggies at segment junctions are smoothed across (not frozen between
        /// independently-snapped segments). Default true. Only used by the chained refine path.
        /// </summary>
        public bool ChainSegments { get; set; } = true;

        /// <summary>Despike threshold (metres): a snapped point further than this from its neighbour
        /// midpoint is a per-segment spur and gets pulled back. Default 24 (a bit under one zone half +
        /// the max displacement). Set 0 to disable despiking.</summary>
        public double DespikeThreshold { get; set; } = 24.0;

        /// <summary>Chaikin corner-cutting iterations applied after despiking. Default 2. Set 0 to disable.</summary>
        public int SmoothIterations { get; set; } = 2;

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
        /// Coastline refinement that CHAINS contiguous segments into continuous arcs, snaps every arc
        /// vertex to the isoline, then despikes + Chaikin-smooths the whole arc. This is the fix for the
        /// junction jaggies: per-segment refinement freezes a kink between every independently-snapped
        /// 64 m segment, whereas smoothing a continuous chain rounds across those joins. Returns one
        /// <see cref="RefinedBorder"/> per chained arc. See docs/design/region-render-seam.md.
        /// </summary>
        public static IReadOnlyList<RefinedBorder> RefineCoastlinesSmoothed(
            RegionBoundaryGraph graph, IScalarField heightField, SegmentRefineOptions options = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            options ??= SegmentRefineOptions.Default;

            // Group coastline segments by their region (KeyA — coastlines have null KeyB), then chain
            // each group into arcs by shared endpoints. Chaining per region keeps distinct islands /
            // coast runs separate (a region's coast can be several disjoint loops).
            var byRegion = new Dictionary<string, List<BorderSegment>>(StringComparer.Ordinal);
            foreach (BorderSegment seg in graph.Segments)
            {
                if (!seg.IsCoastline) continue;
                if (!byRegion.TryGetValue(seg.KeyA, out var list))
                {
                    list = new List<BorderSegment>();
                    byRegion[seg.KeyA] = list;
                }
                list.Add(seg);
            }

            var result = new List<RefinedBorder>();
            foreach (var kv in byRegion)
            {
                foreach (List<WzVec2> chain in ChainSegments(kv.Value))
                {
                    // Snap every chain vertex to the isoline along the local chain normal.
                    var snapped = new List<WzVec2>(chain.Count);
                    bool anyHug = false;
                    for (int i = 0; i < chain.Count; i++)
                    {
                        WzVec2 prev = chain[Math.Max(0, i - 1)];
                        WzVec2 next = chain[Math.Min(chain.Count - 1, i + 1)];
                        double tx = next.X - prev.X, tz = next.Z - prev.Z;
                        double tl = Math.Sqrt(tx * tx + tz * tz);
                        if (tl < 1e-9) { snapped.Add(chain[i]); continue; }
                        double nx = -tz / tl, nz = tx / tl; // local normal
                        if (TrySnapToIso(chain[i].X, chain[i].Z, nx, nz, heightField, options, out double s))
                        {
                            snapped.Add(new WzVec2(chain[i].X + s * nx, chain[i].Z + s * nz));
                            if (Math.Abs(s) > 1e-6) anyHug = true;
                        }
                        else snapped.Add(chain[i]);
                    }

                    IReadOnlyList<WzVec2> poly = snapped;
                    if (options.DespikeThreshold > 0)
                        poly = PolylineSmoother.Despike(poly, options.DespikeThreshold);
                    if (options.SmoothIterations > 0)
                        poly = PolylineSmoother.Chaikin(poly, options.SmoothIterations);

                    result.Add(new RefinedBorder(poly, kv.Key, null, anyHug));
                }
            }
            return result;
        }

        // Chain a bag of undirected segments into maximal polylines by walking shared endpoints.
        private static IEnumerable<List<WzVec2>> ChainSegments(List<BorderSegment> segs)
        {
            // Adjacency by quantised endpoint (segments meet exactly on the lattice, so exact keys work).
            (long, long) Key(WzVec2 p) => ((long)Math.Round(p.X * 100), (long)Math.Round(p.Z * 100));
            var adj = new Dictionary<(long, long), List<int>>();
            var used = new bool[segs.Count];
            for (int i = 0; i < segs.Count; i++)
            {
                foreach (var k in new[] { Key(segs[i].A), Key(segs[i].B) })
                {
                    if (!adj.TryGetValue(k, out var l)) { l = new List<int>(); adj[k] = l; }
                    l.Add(i);
                }
            }

            for (int start = 0; start < segs.Count; start++)
            {
                if (used[start]) continue;
                used[start] = true;
                var chain = new LinkedList<WzVec2>();
                chain.AddLast(segs[start].A);
                chain.AddLast(segs[start].B);

                // Extend forward off the tail, then backward off the head.
                ExtendChain(chain, adj, segs, used, fromTail: true);
                ExtendChain(chain, adj, segs, used, fromTail: false);
                yield return new List<WzVec2>(chain);
            }

            static void ExtendChain(LinkedList<WzVec2> chain,
                Dictionary<(long, long), List<int>> adj, List<BorderSegment> segs, bool[] used, bool fromTail)
            {
                (long, long) Key(WzVec2 p) => ((long)Math.Round(p.X * 100), (long)Math.Round(p.Z * 100));
                while (true)
                {
                    WzVec2 endp = fromTail ? chain.Last.Value : chain.First.Value;
                    if (!adj.TryGetValue(Key(endp), out var cand)) break;
                    int next = -1;
                    foreach (int si in cand) { if (!used[si]) { next = si; break; } }
                    if (next < 0) break;
                    used[next] = true;
                    // Append the far endpoint of the chosen segment.
                    WzVec2 a = segs[next].A, b = segs[next].B;
                    WzVec2 far = (Key(a) == Key(endp)) ? b : a;
                    if (fromTail) chain.AddLast(far); else chain.AddFirst(far);
                }
            }
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
