using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>One reassembled region fill loop, built by chaining the region's <see cref="SharedSeam"/>s
    /// (each refined ONCE and consumed by both bounding regions) instead of refining the region in
    /// isolation. Because the seam is shared, this loop and the neighbour's loop trace the SAME curve along
    /// their common border — and the ink is that same curve too. Fill == ink by construction.</summary>
    public sealed class SharedSeamRing
    {
        public string RegionKey { get; }
        /// <summary>Closed loop vertices (world m), implicitly closed (first != last).</summary>
        public IReadOnlyList<WzVec2> Vertices { get; }
        /// <summary>Signed area (m²); + = CCW outer, − = CW hole (same convention as <see cref="RegionRing"/>).</summary>
        public double SignedArea { get; }
        public bool IsHole => this.SignedArea < 0.0;

        internal SharedSeamRing(string regionKey, IReadOnlyList<WzVec2> verts, double signedArea)
        {
            this.RegionKey = regionKey;
            this.Vertices = verts;
            this.SignedArea = signedArea;
        }
    }

    /// <summary>
    /// Reassembles every region's fill ring from the <see cref="SharedSeamSet"/> — fork B's consumer side.
    /// Each region's border is chained from its shared seams (matched head-to-tail by junction node), and
    /// the winding is oriented to the coarse ring's convention (CCW outer / CW hole). The result is a fill
    /// ring that is byte-identical to the ink along every shared seam, because both read the same refined
    /// <see cref="SharedSeam.Refined"/> polyline — eliminating the ~16 m fill/ink weave (spike-004).
    ///
    /// <para><b>Off by default.</b> Nothing calls this until a consumer opts in; the live overlay still runs
    /// its independent per-region <see cref="RegionRingRefiner"/>. Pure Tier-1, headless-testable.</para>
    /// </summary>
    public static class SharedSeamBoundary
    {
        private const double Zone = 64.0;

        /// <summary>
        /// Build every region's reassembled ring(s) from the shared seams. Keyed by region; a region may
        /// yield several loops (outer + holes). Regions whose seams fail to chain into closed loops are
        /// reported in <paramref name="failedRegions"/> rather than throwing — the caller decides policy.
        /// </summary>
        public static IReadOnlyList<SharedSeamRing> Build(SharedSeamSet seams, RegionBoundaryGraph coarseGraph,
            out IReadOnlyList<string> failedRegions)
        {
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            if (coarseGraph == null) throw new ArgumentNullException(nameof(coarseGraph));

            var rings = new List<SharedSeamRing>();
            var failed = new List<string>();

            // Distinct region keys that appear as a seam's KeyA/KeyB (skip the null void side).
            var regionKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SharedSeam s in seams.Seams)
            {
                if (s.KeyA != null) regionKeys.Add(s.KeyA);
                if (s.KeyB != null) regionKeys.Add(s.KeyB);
            }

            foreach (string region in regionKeys.OrderBy(k => k, StringComparer.Ordinal))
            {
                var loops = ChainRegionLoops(region, seams.SeamsFor(region));
                if (loops == null) { failed.Add(region); continue; }

                // Orient each assembled loop to the coarse-ring winding convention. The coarse rings for
                // this region carry the authoritative CCW-outer / CW-hole signs; the assembled loop covers
                // the same border, so we adopt the sign of the coarse ring it best matches (by |area|).
                var coarseRings = coarseGraph.RingsFor(region);
                foreach (var loop in loops)
                {
                    double area = SignedArea(loop);
                    double wantSign = MatchCoarseWindingSign(loop, area, coarseRings);
                    IReadOnlyList<WzVec2> outLoop = loop;
                    if (wantSign != 0 && Math.Sign(area) != Math.Sign(wantSign))
                    {
                        outLoop = loop.AsEnumerable().Reverse().ToList();
                        area = -area;
                    }
                    rings.Add(new SharedSeamRing(region, outLoop, area));
                }
            }

            failedRegions = failed;
            return rings;
        }

        /// <summary>Convenience overload when the caller does not care about the failure list.</summary>
        public static IReadOnlyList<SharedSeamRing> Build(SharedSeamSet seams, RegionBoundaryGraph coarseGraph)
            => Build(seams, coarseGraph, out _);

        // ── Chain one region's seams into closed loops by shared junction-node endpoints ──────────────────
        // Greedy head-to-tail: proven on 3 real seeds to reassemble 149/156/152 regions watertight once the
        // seams themselves are watertight (spike-004 SPIKE 3). Multi-wedge junctions resolve because a
        // closed loop always exists; greedy finds SOME valid partition into loops, exactly as the shipped
        // RegionBoundaryExtractor.StitchLoops does. Returns null if any chain fails to close.
        private static List<List<WzVec2>> ChainRegionLoops(string region, IReadOnlyList<SharedSeam> rSeams)
        {
            if (rSeams.Count == 0) return null;

            // node → this region's seams incident there (each seam listed under BOTH its endpoint nodes).
            var byNode = new Dictionary<long, List<SharedSeam>>();
            void Add(long n, SharedSeam s)
            {
                if (!byNode.TryGetValue(n, out var l)) { l = new(); byNode[n] = l; }
                l.Add(s);
            }
            foreach (SharedSeam s in rSeams) { Add(s.Node0, s); if (!s.IsClosedLoop) Add(s.Node1, s); }

            var used = new HashSet<SharedSeam>();
            var loops = new List<List<WzVec2>>();

            foreach (SharedSeam seed in rSeams)
            {
                if (used.Contains(seed)) continue;

                // A closed-loop seam (Node0==Node1) is a whole loop by itself.
                if (seed.IsClosedLoop)
                {
                    used.Add(seed);
                    var solo = new List<WzVec2>(seed.Refined);
                    if (solo.Count >= 3) loops.Add(solo);
                    continue;
                }

                var loop = new List<WzVec2>();
                SharedSeam cur = seed;
                long atNode = cur.Node0;                 // we stand at Node0, will walk toward Node1
                used.Add(cur);
                int guard = rSeams.Count + 4;
                bool closed = false;

                while (guard-- > 0)
                {
                    bool forward = cur.Node0 == atNode;   // emit Node0→Node1 if we entered at Node0
                    var pts = forward ? cur.Refined : Reversed(cur.Refined);
                    // Skip the first vertex on every seam after the first — it duplicates the shared junction.
                    for (int i = (loop.Count > 0 ? 1 : 0); i < pts.Count; i++) loop.Add(pts[i]);

                    long far = forward ? cur.Node1 : cur.Node0;
                    if (far == seed.Node0) { closed = true; break; }

                    // next unused seam of this region at 'far'
                    SharedSeam next = null;
                    if (byNode.TryGetValue(far, out var cand))
                        next = cand.FirstOrDefault(s => s != cur && !used.Contains(s));
                    if (next == null) break;              // open chain — reassembly failed
                    used.Add(next);
                    cur = next;
                    atNode = far;
                }

                if (!closed) return null;
                if (loop.Count >= 3) loops.Add(loop);
            }

            return loops.Count > 0 ? loops : null;
        }

        // Pick the winding sign to orient an assembled loop to: the coarse ring of this region whose |area|
        // is closest to the loop's, adopting its sign (CCW outer / CW hole). 0 = no coarse ring to match.
        private static double MatchCoarseWindingSign(IReadOnlyList<WzVec2> loop, double loopArea, IReadOnlyList<RegionRing> coarseRings)
        {
            if (coarseRings == null || coarseRings.Count == 0) return 0;
            double target = Math.Abs(loopArea);
            RegionRing best = null; double bestDiff = double.MaxValue;
            foreach (RegionRing r in coarseRings)
            {
                double diff = Math.Abs(Math.Abs(r.SignedArea) - target);
                if (diff < bestDiff) { bestDiff = diff; best = r; }
            }
            return best == null ? 0 : Math.Sign(best.SignedArea);
        }

        private static List<WzVec2> Reversed(IReadOnlyList<WzVec2> v)
        {
            var o = new List<WzVec2>(v.Count);
            for (int i = v.Count - 1; i >= 0; i--) o.Add(v[i]);
            return o;
        }

        private static double SignedArea(IReadOnlyList<WzVec2> v)
        {
            double s = 0; int n = v.Count;
            for (int i = 0; i < n; i++) { WzVec2 a = v[i], b = v[(i + 1) % n]; s += a.X * b.Z - b.X * a.Z; }
            return s / 2.0;
        }
    }
}
