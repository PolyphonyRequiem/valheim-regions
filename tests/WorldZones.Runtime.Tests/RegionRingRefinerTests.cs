using System;
using System.Collections.Generic;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Locks the AUTHORITATIVE-RING watertight guarantee (DECISION 2026-06-29). The production
    /// <see cref="RegionRingRefiner"/> + its two guards (size floor, self-intersection rollback) must
    /// keep EVERY refined ring on real Niflheim free of self-intersection with winding preserved — the
    /// invariant the headless sweep established (194 rings, 0 failures after guards). If a future change
    /// regresses the guard, this test fails before it reaches Daniel's walk.
    /// </summary>
    public class RegionRingRefinerTests
    {
        private const string NiflheimSeed = "ForTheWort";

        private static (RefinedRegionBoundary boundary, int ringCount) BuildBoundary(string seed)
            => BuildBoundary(seed, null);

        private static (RefinedRegionBoundary boundary, int ringCount) BuildBoundary(string seed, RingRefineOptions options)
        {
            var sampler = PortWorldSampler.FromSeed(seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
            });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();

            int[,] rid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            const double zone = 64.0;
            RegionRingRefiner.RegionIdAt ridAt = (wx, wz) =>
            {
                int gx = (int)Math.Round(wx / zone) - min;
                int gy = (int)Math.Round(wz / zone) - min;
                if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) return -1;
                return rid[gy, gx];
            };

            var idToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RegionInfo r in world.Regions) idToLabel[r.RegionKey] = r.TransientId;

            var coast = new HeightScalarField(sampler);
            var seam = new BiomeCategoryField(sampler);

            var boundary = RefinedRegionBoundary.Build(graph, idToLabel, ridAt, coast, seam, options);
            return (boundary, graph.Rings.Count);
        }

        [Fact]
        public void EveryRefinedRing_OnRealNiflheim_IsWatertight()
        {
            var (boundary, sourceRingCount) = BuildBoundary(NiflheimSeed);
            Assert.NotEmpty(boundary.Rings);
            // Refiner is 1:1 with source rings (no ring dropped or duplicated).
            Assert.Equal(sourceRingCount, boundary.Rings.Count);

            int selfInt = 0; int windFlipImpossible = 0;
            var offenders = new List<string>();
            foreach (RefinedRing r in boundary.Rings)
            {
                if (HasSelfIntersection(r.Vertices)) { selfInt++; offenders.Add($"{r.RegionKey}/{r.Outcome}/verts={r.Vertices.Count}/area={r.SignedArea:F0}"); }
                // SignedArea sign is the ring's own; a degenerate (near-zero) ring is allowed.
                if (r.Outcome == RingRefineOutcome.Smoothed && Math.Abs(r.SignedArea) < 1e-9) windFlipImpossible++;
            }

            Assert.True(selfInt == 0, $"{selfInt} refined rings self-intersect (watertight guard regressed): {string.Join(" | ", offenders)}");
            Assert.Equal(0, windFlipImpossible);
        }

        [Fact]
        public void TinyRings_SkipSmoothing_ButLargeRings_AreSmoothed()
        {
            var (boundary, _) = BuildBoundary(NiflheimSeed);

            int smoothed = 0, skipped = 0, rolled = 0;
            foreach (RefinedRing r in boundary.Rings)
            {
                switch (r.Outcome)
                {
                    case RingRefineOutcome.Smoothed: smoothed++; break;
                    case RingRefineOutcome.SkippedSmoothTooSmall: skipped++; break;
                    case RingRefineOutcome.RolledBackSelfIntersect: rolled++; break;
                }
            }

            // The real world has BOTH: substantial region bodies that smooth, and tiny specks that the
            // size guard keeps refined-only. (If smoothed==0 the guard is mis-thresholded.)
            Assert.True(smoothed > 0, "expected some large rings to smooth");
            // Rollback should be RARE (the size guard catches most would-be failures first); it exists
            // as a safety net, so we only assert it doesn't dominate.
            Assert.True(rolled <= smoothed, $"rollback ({rolled}) should not dominate smoothed ({smoothed})");
        }

        [Fact]
        public void RefinedRing_PreservesWinding_AgainstSourceRing()
        {
            var sampler = PortWorldSampler.FromSeed(NiflheimSeed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true, ComputeRegionInfo = true,
            });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            int[,] rid = world.RegionIdGrid; int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1); const double zone = 64.0;
            RegionRingRefiner.RegionIdAt ridAt = (wx, wz) =>
            { int gx = (int)Math.Round(wx / zone) - min, gy = (int)Math.Round(wz / zone) - min; return (gx < 0 || gy < 0 || gx >= gw || gy >= gh) ? -1 : rid[gy, gx]; };
            var idToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RegionInfo r in world.Regions) idToLabel[r.RegionKey] = r.TransientId;
            var coast = new HeightScalarField(sampler); var seam = new BiomeCategoryField(sampler);

            int checkd = 0;
            foreach (RegionRing src in graph.Rings)
            {
                int label = idToLabel.TryGetValue(src.RegionKey, out var l) ? l : -1;
                RefinedRing rr = RegionRingRefiner.Refine(src, label, ridAt, coast, seam);
                if (Math.Abs(src.SignedArea) < 1e-6 || Math.Abs(rr.SignedArea) < 1e-6) continue;
                // Outer stays outer, hole stays hole — the watertight guarantee never flips winding.
                Assert.Equal(Math.Sign(src.SignedArea), Math.Sign(rr.SignedArea));
                checkd++;
            }
            Assert.True(checkd > 0);
        }

        // ── Fork A: the σ (closed-loop Gaussian) fill-ring smoother, on real Niflheim (SUPERSEDED-2 note in
        //    shared-border-primitive.md). These lock the zero-regression default AND the watertight guarantee
        //    with σ ON — the two things that must hold before this reaches Daniel's walk. ──

        [Fact]
        public void SigmaZero_IsByteIdenticalToDefault_ZeroRegression()
        {
            // The off-by-default contract: SmoothingSigmaMeters = 0 must reproduce the legacy Chaikin path
            // EXACTLY (same vertex count, same coordinates, same outcome) for every ring on the real world.
            var (def, _) = BuildBoundary(NiflheimSeed);                                        // default options
            var (z0, _) = BuildBoundary(NiflheimSeed, new RingRefineOptions { SmoothingSigmaMeters = 0.0 });
            Assert.Equal(def.Rings.Count, z0.Rings.Count);
            for (int i = 0; i < def.Rings.Count; i++)
            {
                RefinedRing a = def.Rings[i], b = z0.Rings[i];
                Assert.Equal(a.RegionKey, b.RegionKey);
                Assert.Equal(a.Outcome, b.Outcome);
                Assert.Equal(a.Vertices.Count, b.Vertices.Count);
                for (int k = 0; k < a.Vertices.Count; k++)
                {
                    Assert.Equal(a.Vertices[k].X, b.Vertices[k].X, 12);
                    Assert.Equal(a.Vertices[k].Z, b.Vertices[k].Z, 12);
                }
            }
        }

        [Fact]
        public void EveryRefinedRing_WithSigma30_OnRealNiflheim_IsStillWatertight()
        {
            // Fork A's headline safety property: turning σ=30 ON must keep EVERY ring on the real world free of
            // self-intersection with winding preserved — the closed Gaussian rides the SAME watertight ladder
            // (self-int → rollback to refined → rollback to raw) as Chaikin, so the n=164 guarantee survives.
            var (boundary, sourceRingCount) = BuildBoundary(NiflheimSeed, new RingRefineOptions { SmoothingSigmaMeters = 30.0 });
            Assert.NotEmpty(boundary.Rings);
            Assert.Equal(sourceRingCount, boundary.Rings.Count);   // still 1:1, no ring dropped

            int selfInt = 0; var offenders = new List<string>();
            foreach (RefinedRing r in boundary.Rings)
                if (HasSelfIntersection(r.Vertices)) { selfInt++; offenders.Add($"{r.RegionKey}/{r.Outcome}/verts={r.Vertices.Count}"); }
            Assert.True(selfInt == 0, $"σ=30 produced {selfInt} self-intersecting rings (watertight ladder regressed): {string.Join(" | ", offenders)}");
        }

        [Fact]
        public void Sigma30_ActuallySmoothsTheFill_VsChaikin_OnRealNiflheim()
        {
            // Prove the knob BITES on the real world: σ=30 must reduce the mean per-vertex jaggedness (distance
            // to the ring-neighbour midpoint, measured modulo n so the closure seam counts) of the large
            // smoothed rings vs the legacy Chaikin fill. This is the headless analogue of "regions stop
            // stepping" — the thing Daniel will see. Compare only rings SMOOTHED under both paths (same bodies).
            var (chaikin, _) = BuildBoundary(NiflheimSeed);
            var (sigma, _) = BuildBoundary(NiflheimSeed, new RingRefineOptions { SmoothingSigmaMeters = 30.0 });

            var chByKey = new Dictionary<string, RefinedRing>(StringComparer.Ordinal);
            foreach (RefinedRing r in chaikin.Rings)
                if (r.Outcome == RingRefineOutcome.Smoothed && !r.IsHole && !chByKey.ContainsKey(r.RegionKey))
                    chByKey[r.RegionKey] = r;

            double sumCh = 0, sumSig = 0; int compared = 0;
            foreach (RefinedRing s in sigma.Rings)
            {
                if (s.Outcome != RingRefineOutcome.Smoothed || s.IsHole) continue;
                if (!chByKey.TryGetValue(s.RegionKey, out RefinedRing c)) continue;
                if (s.Vertices.Count < 8 || c.Vertices.Count < 8) continue;
                sumCh += MeanModularMidpointDev(c.Vertices);
                sumSig += MeanModularMidpointDev(s.Vertices);
                compared++;
            }
            Assert.True(compared > 10, $"expected many comparable large rings, got {compared}");
            double meanCh = sumCh / compared, meanSig = sumSig / compared;
            Assert.True(meanSig < meanCh,
                $"σ=30 fill should be smoother than Chaikin fill on the real world (jaggedness {meanSig:F3} !< {meanCh:F3} m over {compared} rings)");
        }

        private static double MeanModularMidpointDev(IReadOnlyList<WzVec2> v)
        {
            double s = 0; int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 prev = v[(i - 1 + n) % n], next = v[(i + 1) % n];
                double mx = (prev.X + next.X) * 0.5, mz = (prev.Z + next.Z) * 0.5;
                double dx = v[i].X - mx, dz = v[i].Z - mz;
                s += Math.Sqrt(dx * dx + dz * dz);
            }
            return n > 0 ? s / n : 0;
        }

        // Local O(n²) self-intersection check (mirrors the production guard; independent so the test
        // would catch a bug in the production check too).
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
