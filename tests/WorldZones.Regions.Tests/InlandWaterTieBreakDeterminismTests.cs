using WorldZones.Regions;
using Xunit;

namespace WorldZones.Regions.Tests
{
    public class InlandWaterTieBreakDeterminismTests
    {
        [Fact]
        public void Tie_scenario_chooses_same_owner_across_repeated_runs()
        {
            var grid = InlandWaterTestFixtures.SmallGrid();
            InlandWaterTestFixtures.FillAll(grid, DepthClass.Land);

            grid[0, 0] = DepthClass.Shallow;

            var options = new InlandWaterAttributionOptions { Enabled = true };

            InlandWaterTestFixtures.GenerateLand(
                grid,
                targetZonesPerRegion: 1,
                seedRng: 55,
                out int[,] baseline,
                out _,
                options,
                minRegionZones: 0,
                minComponentZonesForProto: 1);

            int baselineOwner = baseline[0 - grid.MinIndex, 0 - grid.MinIndex];
            Assert.True(baselineOwner >= 0);

            for (int i = 0; i < 6; i++)
            {
                InlandWaterTestFixtures.GenerateLand(
                    grid,
                    targetZonesPerRegion: 1,
                    seedRng: 55,
                    out int[,] next,
                    out _,
                    options,
                    minRegionZones: 0,
                    minComponentZonesForProto: 1);

                Assert.Equal(baselineOwner, next[0 - grid.MinIndex, 0 - grid.MinIndex]);
                OwnershipGridAssertions.AssertEqual(baseline, next);
            }
        }
    }
}
