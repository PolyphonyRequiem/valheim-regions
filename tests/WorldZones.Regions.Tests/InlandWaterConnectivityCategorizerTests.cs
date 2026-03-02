using System.Linq;
using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterConnectivityCategorizerTests
    {
        [Fact]
        public void Categorize_marks_inland_and_ocean_connected_water()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            grid[0, 0] = DepthClass.Shallow;
            grid[0, 1] = DepthClass.Shallow;

            int max = grid.MaxIndex;
            grid[max, 0] = DepthClass.Deep;
            grid[max - 1, 0] = DepthClass.Deep;

            var (connectivity, inlandBodies) = InlandWaterConnectivityCategorizer.Categorize(grid);

            Assert.Equal(WaterConnectivityKind.InlandWater, connectivity[0 - grid.MinIndex, 0 - grid.MinIndex]);
            Assert.Equal(WaterConnectivityKind.InlandWater, connectivity[1 - grid.MinIndex, 0 - grid.MinIndex]);
            Assert.Equal(WaterConnectivityKind.OceanConnectedWater, connectivity[0 - grid.MinIndex, max - grid.MinIndex]);
            Assert.Equal(WaterConnectivityKind.OceanConnectedWater, connectivity[0 - grid.MinIndex, (max - 1) - grid.MinIndex]);

            Assert.Single(inlandBodies);
            Assert.Equal(2, inlandBodies[0].ZoneCount);
            Assert.Equal(WaterConnectivityKind.InlandWater, inlandBodies[0].WaterConnectivity);
        }

        [Fact]
        public void Categorize_returns_empty_inland_bodies_when_all_water_is_ocean_connected()
        {
            var grid = InlandWaterTestFixtures.SmallGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Deep);

            var (connectivity, inlandBodies) = InlandWaterConnectivityCategorizer.Categorize(grid);

            Assert.Empty(inlandBodies);
            foreach (var coordinate in grid.AllCoords())
            {
                Assert.Equal(
                    WaterConnectivityKind.OceanConnectedWater,
                    connectivity[coordinate.y - grid.MinIndex, coordinate.x - grid.MinIndex]);
            }
        }
    }
}
