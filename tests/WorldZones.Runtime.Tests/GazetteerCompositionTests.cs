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

                // ...and a LAND region's minimum elevation must sit at/above the water line. The fringe
                // bug pulled min down to ~shore height (≈ water level); land-only keeps it on land.
                // ZoneClassifier.DefaultWaterLevel is the shore reference (30m in Valheim terms).
                Assert.True(
                    r.MinElevation >= ZoneClassifier.DefaultWaterLevel - 1e-3,
                    $"Region {r.RegionKey} ({r.Name}): MinElevation={r.MinElevation:F1} is below the water line " +
                    $"({ZoneClassifier.DefaultWaterLevel:F1}) — shallow fringe is contaminating elevation.");
            }
        }

        [Fact]
        public void OriginRegion_HasExpectedCorrectedValues()
        {
            // Locks the specific corrected numbers for Niflheim's spawn region (Lindeid), so a
            // regression in the land-gating shows up as a concrete, readable diff rather than a
            // whole-world statistical drift. (Values are post-fix; pre-fix these were wrong.)
            RegionWorld world = BuildNiflheim();
            RegionInfo origin = world.RegionAt(0f, 0f);

            Assert.NotNull(origin);
            Assert.Equal("r.3.-10", origin.RegionKey);
            Assert.Equal(BiomeType.Meadows, origin.DominantBiome);
            Assert.Equal(origin.LandZones, origin.SampledLandZones); // 267 == 267, the invariant

            // Meadows fraction is ~66% land-only (was a diluted ~64% under the fringe bug).
            float meadows = origin.BiomeComposition[BiomeType.Meadows];
            Assert.True(meadows > 0.64f && meadows < 0.69f, $"origin Meadows fraction {meadows:F3} off expected ~0.663");

            // min elevation sits on land (≈30m), not dragged to the waterline (~20m pre-fix).
            Assert.True(origin.MinElevation >= 29f, $"origin MinElevation {origin.MinElevation:F1} should be land-floor ~30m");
        }
    }
}
