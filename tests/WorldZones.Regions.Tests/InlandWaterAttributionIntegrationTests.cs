using System.Collections.Generic;
using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterAttributionIntegrationTests
    {
        [Fact]
        public void Enabled_attribution_keeps_land_ownership_unchanged()
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
                seedRng: 99,
                out int[,] baselineRegionIdGrid,
                out _);

            var enabled = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 99,
                out int[,] enabledRegionIdGrid,
                out _,
                new InlandWaterAttributionOptions { Enabled = true });

            foreach (var coordinate in grid.AllCoords())
            {
                if (grid[coordinate] != DepthClass.Land)
                {
                    continue;
                }

                int gy = coordinate.y - grid.MinIndex;
                int gx = coordinate.x - grid.MinIndex;
                Assert.Equal(baselineRegionIdGrid[gy, gx], enabledRegionIdGrid[gy, gx]);
            }

            Assert.True(enabled.AttributedWaterZoneCount > 0);
        }

        [Fact]
        public void Disabled_mode_matches_baseline_grid_exactly()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Shallow;
            grid[0, 1] = DepthClass.Deep;
            grid[1, 1] = DepthClass.Deep;

            InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 105,
                out int[,] baselineRegionIdGrid,
                out _);

            InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 105,
                out int[,] disabledRegionIdGrid,
                out _,
                new InlandWaterAttributionOptions { Enabled = false });

            for (int y = 0; y < grid.Size; y++)
            {
                for (int x = 0; x < grid.Size; x++)
                {
                    Assert.Equal(baselineRegionIdGrid[y, x], disabledRegionIdGrid[y, x]);
                }
            }
        }

        [Fact]
        public void Enabled_attribution_safe_fails_when_inland_body_has_no_adjacent_assigned_region()
        {
            var grid = InlandWaterTestFixtures.SmallGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Deep);

            grid[0, 0] = DepthClass.Shallow;
            grid[-1, 0] = DepthClass.Land;
            grid[1, 0] = DepthClass.Land;
            grid[0, -1] = DepthClass.Land;
            grid[0, 1] = DepthClass.Land;

            var result = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 4,
                seedRng: 7,
                out int[,] regionIdGrid,
                out List<Vector2i> seeds,
                new InlandWaterAttributionOptions { Enabled = true });

            Assert.Empty(seeds);
            Assert.Equal(-1, regionIdGrid[0 - grid.MinIndex, 0 - grid.MinIndex]);
            Assert.Equal(0, result.AttributedWaterBodyCount);
            Assert.Equal(0, result.AttributedWaterZoneCount);
            Assert.Equal(1, result.UnassignedWaterBodyCount);
            Assert.Equal(1, result.UnassignedInlandWaterZoneCount);
        }
    }
}
