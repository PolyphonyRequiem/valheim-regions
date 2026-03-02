using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterNoOpSummaryTests
    {
        [Fact]
        public void Enabled_attribution_keeps_summary_unchanged_when_no_inland_water_exists()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            var baseline = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 10,
                seedRng: 44,
                out _,
                out _);

            var enabled = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 10,
                seedRng: 44,
                out _,
                out _,
                new InlandWaterAttributionOptions { Enabled = true });

            Assert.Equal(baseline.RegionCount, enabled.RegionCount);
            Assert.Equal(0, enabled.AttributedWaterZoneCount);
            Assert.Equal(0, enabled.UnassignedInlandWaterZoneCount);

            for (int i = 0; i < baseline.Regions.Count; i++)
            {
                var baselineRegion = baseline.Regions[i];
                var enabledRegion = enabled.Regions[i];

                Assert.Equal(baselineRegion.Id, enabledRegion.Id);
                Assert.Equal(baselineRegion.AreaZones, enabledRegion.AreaZones);
                Assert.Equal(baselineRegion.AreaZones, enabledRegion.LandAreaZones);
                Assert.Equal(0, enabledRegion.InlandWaterAreaZones);
                Assert.Equal(enabledRegion.LandAreaZones, enabledRegion.TotalAreaZones);
            }
        }
    }
}
