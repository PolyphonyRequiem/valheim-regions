using System.Linq;
using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterRegionSummaryTests
    {
        [Fact]
        public void Enabled_attribution_adds_inland_water_summary_without_changing_land_summary()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Shallow;
            grid[0, 1] = DepthClass.Deep;
            grid[1, 1] = DepthClass.Deep;

            var baseline = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 31,
                out _,
                out _);

            var enabled = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 31,
                out _,
                out _,
                new InlandWaterAttributionOptions { Enabled = true });

            var baselineById = baseline.Regions.ToDictionary(region => region.Id);
            var enabledById = enabled.Regions.ToDictionary(region => region.Id);

            Assert.Equal(baselineById.Keys.OrderBy(id => id), enabledById.Keys.OrderBy(id => id));

            foreach (var kv in baselineById)
            {
                int regionId = kv.Key;
                ProtoRegion baselineRegion = kv.Value;
                ProtoRegion enabledRegion = enabledById[regionId];

                Assert.Equal(baselineRegion.AreaZones, enabledRegion.LandAreaZones);
                Assert.Equal(enabledRegion.AreaZones, enabledRegion.LandAreaZones);
                Assert.Equal(
                    enabledRegion.LandAreaZones + enabledRegion.InlandWaterAreaZones,
                    enabledRegion.TotalAreaZones);
            }

            Assert.Contains(enabled.Regions, region => region.InlandWaterAreaZones > 0);
        }
    }
}
