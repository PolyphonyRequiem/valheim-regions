using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Locks the SHARED-SEAM PRIMITIVE (fork B, docs/design/spike-004-shared-seam-primitive.md) on real
    /// Niflheim. The primitive decomposes the coarse border into one arc per region-pair, split at
    /// junctions, and refines each ONCE with junction endpoints pinned — so fill and ink can read the SAME
    /// curve. These tests promote the spike's measured properties into permanent invariants: coverage,
    /// gap-0 junction meeting, endpoint pinning, determinism, watertight refined seams.
    /// </summary>
    public class SharedSeamSetTests
    {
        private const string NiflheimSeed = "ForTheWort";
        private const double Zone = 64.0;

        private static (RegionBoundaryGraph graph, PortWorldSampler sampler, RegionWorld world) BuildGraph(string seed)
        {
            var sampler = PortWorldSampler.FromSeed(seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
            });
            return (world.BuildBoundaryGraph(), sampler, world);
        }

        private static SharedSeamSet BuildSeams(string seed, SharedSeamRefineOptions opt = null)
        {
            var (graph, sampler, _) = BuildGraph(seed);
            var coast = new HeightScalarField(sampler);
            var flip = new BiomeCategoryField(sampler);
            return SharedSeamSet.Build(graph, coast, flip, opt);
        }

        [Fact]
        public void Build_OnRealNiflheim_CoversEverySegmentExactlyOnce()
        {
            var (graph, sampler, _) = BuildGraph(NiflheimSeed);
            var coast = new HeightScalarField(sampler);
            var flip = new BiomeCategoryField(sampler);
            SharedSeamSet set = SharedSeamSet.Build(graph, coast, flip);

            Assert.NotEmpty(set.Seams);

            // Every coarse 64 m segment (as an unordered node-pair + region-pair key) must appear in exactly
            // one seam's coarse polyline. No segment dropped, none double-counted.
            var fromGraph = new HashSet<(long, long, string)>();
            foreach (BorderSegment s in graph.Segments)
                fromGraph.Add(SegKey(s.A, s.B, s.KeyA, s.KeyB));

            var fromSeams = new Dictionary<(long, long, string), int>();
            foreach (SharedSeam seam in set.Seams)
            {
                var pts = seam.Coarse;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var k = SegKey(pts[i], pts[i + 1], seam.KeyA, seam.KeyB);
                    fromSeams[k] = fromSeams.GetValueOrDefault(k) + 1;
                }
            }

            // Same set of segments, each exactly once.
            Assert.Equal(fromGraph.Count, fromSeams.Count);
            Assert.True(fromSeams.Values.All(c => c == 1),
                $"{fromSeams.Values.Count(c => c != 1)} segments appear in != 1 seam");
            Assert.True(fromGraph.SetEquals(fromSeams.Keys), "seam segment set != graph segment set");
        }

        [Fact]
        public void RefinedSeams_ShareJunctionEndpoints_WithZeroGap()
        {
            // THE headline property (spike-004 SPIKE 2): every seam ending at a junction pins that endpoint
            // to the EXACT lattice node, so all seams meeting there are coincident — gap = 0, by construction.
            SharedSeamSet set = BuildSeams(NiflheimSeed);

            double maxGap = 0; int junctionEndsChecked = 0;
            foreach (SharedSeam seam in set.Seams)
            {
                foreach (long nodeId in new[] { seam.Node0, seam.Node1 })
                {
                    if (!set.JunctionNodes.Contains(nodeId)) continue;
                    WzVec2 expected = SharedSeamSet.NodePos(nodeId, Zone);
                    WzVec2 actual = nodeId == seam.Node0 ? seam.Refined[0] : seam.Refined[seam.Refined.Count - 1];
                    maxGap = Math.Max(maxGap, Dist(expected, actual));
                    junctionEndsChecked++;
                }
            }
            Assert.True(junctionEndsChecked > 100, $"expected many junction ends on real Niflheim, got {junctionEndsChecked}");
            Assert.True(maxGap < 1e-9, $"refined seam endpoints drifted off their junction node (max gap {maxGap:E3} m — pinning broke)");
        }

        [Fact]
        public void RefinedSeams_PinBothEndpoints_ExactlyToCoarse()
        {
            // Endpoints must equal the coarse endpoints exactly (they are the shared junctions / seam ends).
            SharedSeamSet set = BuildSeams(NiflheimSeed);
            foreach (SharedSeam seam in set.Seams)
            {
                Assert.True(seam.Refined.Count >= 2);
                Assert.Equal(seam.Coarse[0].X, seam.Refined[0].X, 9);
                Assert.Equal(seam.Coarse[0].Z, seam.Refined[0].Z, 9);
                Assert.Equal(seam.Coarse[^1].X, seam.Refined[^1].X, 9);
                Assert.Equal(seam.Coarse[^1].Z, seam.Refined[^1].Z, 9);
            }
        }

        [Fact]
        public void Build_IsDeterministic()
        {
            // Same seed + same options ⇒ identical seam set (count, keys, endpoints, refined vertices).
            SharedSeamSet a = BuildSeams(NiflheimSeed);
            SharedSeamSet b = BuildSeams(NiflheimSeed);
            Assert.Equal(a.Seams.Count, b.Seams.Count);
            for (int i = 0; i < a.Seams.Count; i++)
            {
                SharedSeam sa = a.Seams[i], sb = b.Seams[i];
                Assert.Equal(sa.KeyA, sb.KeyA);
                Assert.Equal(sa.KeyB, sb.KeyB);
                Assert.Equal(sa.Node0, sb.Node0);
                Assert.Equal(sa.Node1, sb.Node1);
                Assert.Equal(sa.Refined.Count, sb.Refined.Count);
                for (int k = 0; k < sa.Refined.Count; k++)
                {
                    Assert.Equal(sa.Refined[k].X, sb.Refined[k].X, 12);
                    Assert.Equal(sa.Refined[k].Z, sb.Refined[k].Z, 12);
                }
            }
        }

        [Fact]
        public void RefinedSeams_OnRealNiflheim_AreWatertight_NoSelfIntersection()
        {
            // Every refined seam (open arc) must be free of self-intersection — the σ ladder rolls back any
            // that would cross (spike-004: the 5 first-run self-ints were this same class, ladder → 0).
            SharedSeamSet set = BuildSeams(NiflheimSeed);
            int offenders = 0; var examples = new List<string>();
            foreach (SharedSeam seam in set.Seams)
                if (HasSelfIntersectionOpen(seam.Refined))
                { offenders++; if (examples.Count < 8) examples.Add($"{seam.KeyA}|{seam.KeyB ?? "~"} verts={seam.Refined.Count}"); }
            Assert.True(offenders == 0, $"{offenders} refined seams self-intersect (watertight ladder regressed): {string.Join(", ", examples)}");
        }

        [Fact]
        public void Build_SigmaZero_LeavesSeamsUnsmoothed_ButStillCovers()
        {
            // σ=0 is the despike-only path (no Gaussian). Still a valid decomposition; endpoints still pinned.
            SharedSeamSet set = BuildSeams(NiflheimSeed, new SharedSeamRefineOptions { SmoothingSigmaMeters = 0.0 });
            Assert.NotEmpty(set.Seams);
            foreach (SharedSeam seam in set.Seams.Take(200))
            {
                Assert.Equal(seam.Coarse[0].X, seam.Refined[0].X, 9);
                Assert.Equal(seam.Coarse[^1].X, seam.Refined[^1].X, 9);
            }
        }

        [Fact]
        public void SeamsFor_ReturnsOnlySeamsTouchingThatRegion()
        {
            var (graph, sampler, world) = BuildGraph(NiflheimSeed);
            var coast = new HeightScalarField(sampler);
            var flip = new BiomeCategoryField(sampler);
            SharedSeamSet set = SharedSeamSet.Build(graph, coast, flip);

            // Pick a real region with a substantial border.
            RegionInfo region = world.Regions.OrderByDescending(r => r.AreaKm2).First();
            var seams = set.SeamsFor(region.RegionKey);
            Assert.NotEmpty(seams);
            Assert.True(seams.All(s => s.Touches(region.RegionKey)), "SeamsFor returned a seam not touching the region");
        }

        // ── helpers ──
        private static (long, long, string) SegKey(WzVec2 a, WzVec2 b, string kA, string kB)
        {
            long na = SharedSeamSet.NodeId(a, Zone), nb = SharedSeamSet.NodeId(b, Zone);
            string pair = kB == null ? kA + "|~" : (string.CompareOrdinal(kA, kB) <= 0 ? kA + "|" + kB : kB + "|" + kA);
            return (Math.Min(na, nb), Math.Max(na, nb), pair);
        }

        private static double Dist(WzVec2 a, WzVec2 b) { double dx = a.X - b.X, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dz * dz); }

        private static bool HasSelfIntersectionOpen(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count;
            for (int i = 0; i < n - 1; i++)
                for (int j = i + 2; j < n - 1; j++)
                    if (SegInt(v[i], v[i + 1], v[j], v[j + 1])) return true;
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
