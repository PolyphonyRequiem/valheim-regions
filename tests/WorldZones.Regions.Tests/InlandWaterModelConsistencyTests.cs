using System.Linq;
using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterModelConsistencyTests
    {
        [Fact]
        public void Enabled_attribution_preserves_model_invariants_for_land_inland_and_total_areas()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Shallow;
            grid[0, 1] = DepthClass.Deep;
            grid[1, 1] = DepthClass.Deep;

            var result = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 221,
                out int[,] regionIdGrid,
                out _,
                new InlandWaterAttributionOptions { Enabled = true });

            int landAreaSum = result.Regions.Sum(region => region.LandAreaZones);
            int inlandAreaSum = result.Regions.Sum(region => region.InlandWaterAreaZones);
            int totalAreaSum = result.Regions.Sum(region => region.TotalAreaZones);

            Assert.Equal(result.LandZoneCount - result.UnassignedLandCount, landAreaSum);
            Assert.Equal(result.AttributedWaterZoneCount, inlandAreaSum);
            Assert.Equal(result.TotalInlandWaterAreaZones, inlandAreaSum);
            Assert.Equal(totalAreaSum, landAreaSum + inlandAreaSum);
            Assert.Equal(result.TotalRegionTerritoryAreaZones, totalAreaSum);

            var (connectivityGrid, inlandBodies) = InlandWaterConnectivityCategorizer.Categorize(grid);
            int allInlandZones = inlandBodies.Sum(body => body.ZoneCount);
            Assert.Equal(allInlandZones, result.AttributedWaterZoneCount + result.UnassignedInlandWaterZoneCount);

            foreach (var body in inlandBodies)
            {
                int representativeRegion = regionIdGrid[body.Zones[0].y - grid.MinIndex, body.Zones[0].x - grid.MinIndex];
                if (representativeRegion < 0)
                {
                    continue;
                }

                Assert.All(body.Zones, zone =>
                {
                    Assert.Equal(WaterConnectivityKind.InlandWater, connectivityGrid[zone.y - grid.MinIndex, zone.x - grid.MinIndex]);
                    Assert.Equal(representativeRegion, regionIdGrid[zone.y - grid.MinIndex, zone.x - grid.MinIndex]);
                });
            }
        }

        [Fact]
        public void Disabled_attribution_keeps_inland_summary_totals_at_zero()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);
            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Shallow;

            var result = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 222,
                out _,
                out _,
                new InlandWaterAttributionOptions { Enabled = false });

            Assert.Equal(0, result.AttributedWaterZoneCount);
            Assert.Equal(0, result.UnassignedInlandWaterZoneCount);
            Assert.Equal(0, result.TotalInlandWaterAreaZones);
            Assert.Equal(result.LandZoneCount - result.UnassignedLandCount, result.TotalRegionTerritoryAreaZones);
            Assert.All(result.Regions, region => Assert.Equal(0, region.InlandWaterAreaZones));
        }
    }
}
