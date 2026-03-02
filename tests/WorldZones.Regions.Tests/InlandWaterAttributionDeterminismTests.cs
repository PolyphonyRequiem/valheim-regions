using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterAttributionDeterminismTests
    {
        [Fact]
        public void Repeated_runs_with_same_seed_and_options_produce_identical_grids_and_counts()
        {
            var grid = InlandWaterTestFixtures.MediumGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Shallow;
            grid[0, 1] = DepthClass.Deep;
            grid[1, 1] = DepthClass.Deep;

            var options = new InlandWaterAttributionOptions { Enabled = true };

            ProtoRegionResult first = InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 12,
                seedRng: 212,
                out int[,] firstGrid,
                out _,
                options);

            for (int run = 0; run < 4; run++)
            {
                ProtoRegionResult next = InlandWaterTestFixtures.GenerateLand(
                    grid,
                    targetZonesPerRegion: 12,
                    seedRng: 212,
                    out int[,] nextGrid,
                    out _,
                    options);

                OwnershipGridAssertions.AssertEqual(firstGrid, nextGrid);
                Assert.Equal(first.AttributedWaterZoneCount, next.AttributedWaterZoneCount);
                Assert.Equal(first.UnassignedInlandWaterZoneCount, next.UnassignedInlandWaterZoneCount);
                Assert.Equal(first.AttributedWaterBodyCount, next.AttributedWaterBodyCount);
                Assert.Equal(first.UnassignedWaterBodyCount, next.UnassignedWaterBodyCount);
            }
        }
    }
}
