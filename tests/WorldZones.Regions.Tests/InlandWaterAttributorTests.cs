using System.Collections.Generic;
using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterAttributorTests
    {
        [Fact]
        public void Attribute_uses_lowest_region_id_when_shared_border_votes_tie()
        {
            var grid = InlandWaterTestFixtures.SmallGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);
            grid[0, 0] = DepthClass.Shallow;

            int size = grid.Size;
            int min = grid.MinIndex;
            var regionIdGrid = new int[size, size];
            var connectivityGrid = new WaterConnectivityKind[size, size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    regionIdGrid[y, x] = -1;
                    connectivityGrid[y, x] = WaterConnectivityKind.NotWater;
                }
            }

            connectivityGrid[0 - min, 0 - min] = WaterConnectivityKind.InlandWater;

            regionIdGrid[0 - min, -1 - min] = 1;
            regionIdGrid[0 - min, 1 - min] = 2;
            regionIdGrid[-1 - min, 0 - min] = 2;
            regionIdGrid[1 - min, 0 - min] = 1;

            var bodies = new List<InlandWaterBody>
            {
                new InlandWaterBody
                {
                    WaterBodyId = 0,
                    WaterConnectivity = WaterConnectivityKind.InlandWater,
                    Zones = { new Vector2i(0, 0) }
                }
            };

            var result = InlandWaterAttributor.Attribute(grid, regionIdGrid, connectivityGrid, bodies);

            Assert.Equal(1, regionIdGrid[0 - min, 0 - min]);
            Assert.Equal(1, result.AttributedWaterBodyCount);
            Assert.Equal(1, result.AttributedWaterZoneCount);
            Assert.Contains(1, result.ChangedRegionIds);
        }

        [Fact]
        public void Attribute_leaves_body_unassigned_when_no_adjacent_assigned_region_exists()
        {
            var grid = InlandWaterTestFixtures.SmallGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);
            grid[0, 0] = DepthClass.Shallow;

            int size = grid.Size;
            int min = grid.MinIndex;
            var regionIdGrid = new int[size, size];
            var connectivityGrid = new WaterConnectivityKind[size, size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    regionIdGrid[y, x] = -1;
                    connectivityGrid[y, x] = WaterConnectivityKind.NotWater;
                }
            }

            connectivityGrid[0 - min, 0 - min] = WaterConnectivityKind.InlandWater;
            var bodies = new List<InlandWaterBody>
            {
                new InlandWaterBody
                {
                    WaterBodyId = 1,
                    WaterConnectivity = WaterConnectivityKind.InlandWater,
                    Zones = { new Vector2i(0, 0) }
                }
            };

            var result = InlandWaterAttributor.Attribute(grid, regionIdGrid, connectivityGrid, bodies);

            Assert.Equal(-1, regionIdGrid[0 - min, 0 - min]);
            Assert.Equal(0, result.AttributedWaterBodyCount);
            Assert.Equal(0, result.AttributedWaterZoneCount);
            Assert.Equal(1, result.UnassignedWaterBodyCount);
            Assert.Equal(1, result.UnassignedInlandWaterZoneCount);
        }
    }
}
