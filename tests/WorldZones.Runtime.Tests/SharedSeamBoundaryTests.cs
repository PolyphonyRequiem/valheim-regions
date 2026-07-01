using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Locks fork B's CONSUMER side (docs/design/spike-004-shared-seam-primitive.md): reassembling every
    /// region's fill ring from the SHARED seams. The payoff invariants, on real Niflheim:
    ///   • every region reassembles into watertight closed loop(s) — no open chain, no self-intersection;
    ///   • the reassembled outer ring keeps the coarse ring's winding (CCW outer);
    ///   • FILL == INK: a reassembled ring and the shared seams it is built from are the SAME curve
    ///     (0 separation), which is the entire reason B exists (kills the ~16 m weave).
    /// </summary>
    public class SharedSeamBoundaryTests
    {
        private const string NiflheimSeed = "ForTheWort";

        private static (SharedSeamSet seams, RegionBoundaryGraph graph, RegionWorld world) Build(string seed)
        {
            var sampler = PortWorldSampler.FromSeed(seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
            });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var coast = new HeightScalarField(sampler);
            var flip = new BiomeCategoryField(sampler);
            SharedSeamSet seams = SharedSeamSet.Build(graph, coast, flip);
            return (seams, graph, world);
        }

        [Fact]
        public void EveryRegion_ReassemblesFromSharedSeams_Watertight()
        {
            var (seams, graph, world) = Build(NiflheimSeed);
            IReadOnlyList<SharedSeamRing> rings = SharedSeamBoundary.Build(seams, graph, out var failed);

            // No region should fail to chain into closed loops.
            Assert.True(failed.Count == 0, $"{failed.Count} regions failed to reassemble: {string.Join(", ", failed.Take(12))}");

            // Every reassembled loop must be free of self-intersection (watertight fill).
            int selfInt = 0; var examples = new List<string>();
            foreach (SharedSeamRing r in rings)
                if (HasSelfIntersection(r.Vertices))
                { selfInt++; if (examples.Count < 8) examples.Add($"{r.RegionKey} verts={r.Vertices.Count}"); }
            Assert.True(selfInt == 0, $"{selfInt} reassembled rings self-intersect: {string.Join(", ", examples)}");

            // Sanity: we actually produced rings for the bulk of regions with a border.
            int regionsWithRings = rings.Select(r => r.RegionKey).Distinct().Count();
            Assert.True(regionsWithRings > 100, $"expected most of the ~149 regions to reassemble, got {regionsWithRings}");
        }

        [Fact]
        public void ReassembledOuterRing_PreservesCoarseWinding()
        {
            var (seams, graph, world) = Build(NiflheimSeed);
            IReadOnlyList<SharedSeamRing> rings = SharedSeamBoundary.Build(seams, graph);

            var byRegion = rings.GroupBy(r => r.RegionKey).ToDictionary(g => g.Key, g => g.ToList());
            int checkedOuter = 0, ccwOuter = 0;
            foreach (RegionInfo region in world.Regions)
            {
                if (!byRegion.TryGetValue(region.RegionKey, out var loops)) continue;
                RegionRing coarseOuter = graph.OuterRing(region.RegionKey);
                if (coarseOuter == null) continue;
                // The largest reassembled loop is the region's outer ring; it must wind the same way (CCW).
                SharedSeamRing biggest = loops.OrderByDescending(l => Math.Abs(l.SignedArea)).First();
                checkedOuter++;
                if (Math.Sign(biggest.SignedArea) == Math.Sign(coarseOuter.SignedArea)) ccwOuter++;
            }
            Assert.True(checkedOuter > 100);
            // Winding must be preserved for EVERY outer ring (the fill baker's point-in-polygon depends on it).
            Assert.Equal(checkedOuter, ccwOuter);
        }

        [Fact]
        public void FillEqualsInk_SharedSeamVerticesLieExactlyOnTheReassembledRing()
        {
            // THE reason B exists: fill and ink are the SAME curve. The ink for an interior border IS the
            // shared seam's refined polyline; the fill ring is chained FROM those same polylines. So every
            // shared-seam vertex must lie EXACTLY on the reassembled ring of each region the seam bounds —
            // separation 0, not the ~16 m weave of the three-times-derived path.
            var (seams, graph, world) = Build(NiflheimSeed);
            IReadOnlyList<SharedSeamRing> rings = SharedSeamBoundary.Build(seams, graph);
            var ringVertsByRegion = rings.GroupBy(r => r.RegionKey)
                .ToDictionary(g => g.Key, g => new HashSet<(double, double)>(
                    g.SelectMany(r => r.Vertices).Select(v => (Round(v.X), Round(v.Z)))));

            int sampled = 0, offRing = 0; double maxSep = 0;
            foreach (SharedSeam seam in seams.Seams)
            {
                if (seam.IsCoast) continue;   // interior seams are the fill==ink claim
                foreach (string key in new[] { seam.KeyA, seam.KeyB })
                {
                    if (key == null || !ringVertsByRegion.TryGetValue(key, out var vset)) continue;
                    foreach (WzVec2 p in seam.Refined)
                    {
                        sampled++;
                        if (!vset.Contains((Round(p.X), Round(p.Z))))
                        {
                            offRing++;
                            // measure how far off (should never happen — same list of points)
                            // (cheap: only compute on the rare miss)
                        }
                    }
                }
            }
            Assert.True(sampled > 1000, $"expected many interior seam vertices, got {sampled}");
            // Every interior seam vertex is a vertex of both bounding regions' reassembled rings — exactly.
            Assert.True(offRing == 0, $"{offRing}/{sampled} shared-seam vertices are NOT on the reassembled ring (fill != ink!) maxSep={maxSep:F3}");
        }

        // ── helpers ──
        private static double Round(double v) => Math.Round(v, 3);   // rings are built from the SAME points; exact to mm

        private static bool HasSelfIntersection(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 a1 = v[i], a2 = v[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1) continue;
                    if (i == 0 && j == n - 1) continue;
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
