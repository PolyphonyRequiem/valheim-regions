using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards the per-region CHARACTER aggregation in <see cref="GazetteerBuilder"/> against the
    /// shallow-fringe contamination bug: a region's id tags its one-zone coastal shallow fringe, and
    /// those water cells must NOT be counted into land-character aggregates (composition denominator,
    /// centroid, elevation, bounds). Pre-fix, every coastal region over-counted, breaking the
    /// SampledLandZones &lt;= LandZones invariant and dragging min-elevation to the waterline.
    ///
    /// These run the real Niflheim world (seed ForTheWort) through the production façade.
    /// </summary>
    public class GazetteerCompositionTests
    {
        private const string NiflheimSeed = "ForTheWort";

        private static RegionWorld BuildNiflheim() =>
            WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions { IncludeInlandWater = true });

        [Fact]
        public void SampledLandZones_NeverExceeds_LandZones()
        {
            RegionWorld world = BuildNiflheim();
            Assert.NotEmpty(world.Regions);

            // The invariant the fringe bug violated: you cannot sample more land than the region has.
            // With the fix, sampled-land == official land for every region.
            foreach (var r in world.Regions)
            {
                Assert.True(
                    r.SampledLandZones <= r.LandZones,
                    $"Region {r.RegionKey} ({r.Name}): SampledLandZones={r.SampledLandZones} > LandZones={r.LandZones} " +
                    "— shallow-fringe cells are being counted as land character.");
            }
        }

        [Fact]
        public void BiomeComposition_DenominatorIsLandOnly_And_CountsSumToSampledLand()
        {
            RegionWorld world = BuildNiflheim();

            foreach (var r in world.Regions)
            {
                int countSum = r.BiomeZoneCounts.Values.Sum();

                // Every land zone contributes exactly one biome tally, so the (ocean-excluded) biome
                // counts must sum to the sampled-land count. If a shallow fringe leaked in, this sum
                // would exceed SampledLandZones (or SampledLandZones would exceed LandZones).
                Assert.Equal(r.SampledLandZones, countSum);

                // And the fix makes sampled-land equal the official land area.
                Assert.Equal(r.LandZones, r.SampledLandZones);
            }
        }

        [Fact]
        public void BiomeComposition_Fractions_SumToOne()
        {
            RegionWorld world = BuildNiflheim();

            foreach (var r in world.Regions)
            {
                double sum = r.BiomeComposition.Values.Sum();
                // Float rounding tolerance; the point is the denominator is consistent with the counts.
                Assert.True(
                    sum > 0.999 && sum < 1.001,
                    $"Region {r.RegionKey}: biome fractions sum to {sum:F4}, expected ~1.0.");
            }
        }

        [Fact]
        public void Elevation_And_Centroid_AreLandOnly_NotDraggedToWaterline()
        {
            RegionWorld world = BuildNiflheim();

            foreach (var r in world.Regions)
            {
                // min/mean/max elevation must be internally consistent...
                Assert.True(r.MinElevation <= r.MeanElevation + 1e-3, $"{r.RegionKey}: min > mean");
                Assert.True(r.MeanElevation <= r.MaxElevation + 1e-3, $"{r.RegionKey}: mean > max");

                // ...and a land region's minimum elevation must not be dragged below its true land floor
                // by a shallow water fringe (the contamination bug this test guards). The floor depends
                // on the biome rescue: a SWAMP-bearing region legitimately includes sub-waterline swamp
                // terrain (the swamp land-floor, ~22 m — RegionBuildOptions.SwampLandFloorMeters), so its
                // floor is that floor; a region with NO swamp must still sit at/above the 30 m waterline
                // (any sub-30 there WOULD be leaked shallow fringe). This keeps the original fringe guard
                // intact for non-swamp regions while admitting the intended swamp rescue.
                bool hasSwamp = r.BiomeZoneCounts.TryGetValue(BiomeType.Swamp, out int sc) && sc > 0;
                float floor = hasSwamp ? SwampFloor : ZoneClassifier.DefaultWaterLevel;
                Assert.True(
                    r.MinElevation >= floor - 1e-3,
                    $"Region {r.RegionKey} ({r.Name}): MinElevation={r.MinElevation:F1} is below its land floor " +
                    $"({floor:F1}; hasSwamp={hasSwamp}) — shallow fringe is contaminating elevation.");
            }
        }

        /// <summary>The shipped swamp land-floor (RegionBuildOptions default). A swamp region's terrain
        /// legitimately reaches this far below the waterline; below it would be leaked water.</summary>
        private const float SwampFloor = 22f;

        [Fact]
        public void OriginRegion_HasExpectedCorrectedValues()
        {
            // Locks the specific values for Niflheim's spawn region, so a regression in the land-gating
            // shows up as a concrete, readable diff rather than a whole-world statistical drift.
            // NOTE: these are the SWAMP-LAND-FLOOR values (RegionBuildOptions default SwampLandFloorMeters=22):
            // rescuing sub-waterline swamp into regions shifts seeding/growth, so spawn now resolves to a
            // different, larger region (r.3.11 "the Highlands of Jarnfjord", BlackForest) than the pre-swamp
            // r.3.-10 Meadows. This RegionKey renumber is the documented, accepted consequence of the
            // classification change (keys are seed-coordinate-derived). The SampledLand==Land invariant
            // and the land-only character guarantees still hold.
            RegionWorld world = BuildNiflheim();
            RegionInfo origin = world.RegionAt(0f, 0f);

            Assert.NotNull(origin);
            Assert.Equal("r.3.11", origin.RegionKey);
            Assert.Equal(BiomeType.BlackForest, origin.DominantBiome);
            Assert.Equal(origin.LandZones, origin.SampledLandZones); // 351 == 351, the invariant

            // BlackForest dominates ~41%, Meadows ~36% (land-only denominator).
            float blackForest = origin.BiomeComposition[BiomeType.BlackForest];
            Assert.True(blackForest > 0.39f && blackForest < 0.44f,
                $"origin BlackForest fraction {blackForest:F3} off expected ~0.413");

            // This spawn region carries no swamp, so its min elevation still sits on the 30 m land floor
            // (the swamp rescue did not drag it down — the land-only character guarantee holds).
            Assert.True(origin.MinElevation >= 29f, $"origin MinElevation {origin.MinElevation:F1} should be land-floor ~30m");
        }
    }
}
