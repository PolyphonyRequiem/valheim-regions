using System;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards the swamp land-floor fix (<see cref="RegionBuildOptions.SwampLandFloorMeters"/>). Swamp
    /// terrain straddles the 30 m waterline, so the depth-only classifier dropped ~64% of swamp zones
    /// from regions on real Niflheim. The fix rescues a swamp zone as Land when its height ≥ the floor.
    /// These tests pin: (1) the fix materially reduces swamp drop vs the disabled (null) baseline;
    /// (2) it is gated to swamp — NO non-swamp zone changes class between the two runs (zero blast
    /// radius); (3) the rescued world has at least as many land zones (monotonic — the rule only ever
    /// ADDS land). Real Niflheim (seed ForTheWort). See docs/design/region-borders.md.
    /// </summary>
    public class SwampLandFloorTests
    {
        private const string NiflheimSeed = "ForTheWort";

        private static (ZoneGrid grid, int land, int swampDropped) ClassifyWorld(float? floor)
        {
            var sampler = PortWorldSampler.FromSeed(NiflheimSeed);
            var grid = new ZoneGrid();
            if (floor.HasValue)
                ZoneClassifier.ClassifyWithSwampFloor(
                    grid,
                    (wx, wz) => sampler.GetHeight(wx, wz),
                    (wx, wz) => sampler.GetBiome(wx, wz) == BiomeType.Swamp,
                    floor);
            else
                ZoneClassifier.Classify(grid, new TestProvider(sampler));

            int land = 0, swampDropped = 0;
            foreach (var coord in grid.AllCoords())
            {
                var c = ZoneGrid.ZoneCenter(coord);
                bool isLand = grid[coord] == DepthClass.Land;
                if (isLand) land++;
                if (!isLand && sampler.GetBiome(c.worldX, c.worldZ) == BiomeType.Swamp) swampDropped++;
            }
            return (grid, land, swampDropped);
        }

        [Fact]
        public void SwampFloor_RescuesMostDroppedSwamp_VsDisabledBaseline()
        {
            var off = ClassifyWorld(floor: null);          // legacy depth-only
            var on = ClassifyWorld(floor: 22f);            // shipped default

            // The baseline must actually exhibit the bug (lots of swamp dropped), else the test is vacuous.
            Assert.True(off.swampDropped > 200,
                $"expected the depth-only baseline to drop many swamp zones, got {off.swampDropped}");

            // The fix must rescue the large majority of them.
            Assert.True(on.swampDropped * 5 < off.swampDropped,
                $"swamp floor should cut drop by >5x: baseline={off.swampDropped} fixed={on.swampDropped}");
        }

        [Fact]
        public void SwampFloor_OnlyAddsLand_NeverRemoves()
        {
            var off = ClassifyWorld(floor: null);
            var on = ClassifyWorld(floor: 22f);
            // The rule only ever promotes a zone TO Land, so land count is monotonic non-decreasing.
            Assert.True(on.land >= off.land,
                $"swamp floor must not remove land: baseline={off.land} fixed={on.land}");
        }

        [Fact]
        public void SwampFloor_ChangesNoNonSwampZone_ZeroBlastRadius()
        {
            var sampler = PortWorldSampler.FromSeed(NiflheimSeed);
            var gOff = new ZoneGrid();
            ZoneClassifier.Classify(gOff, new TestProvider(sampler));
            var gOn = new ZoneGrid();
            ZoneClassifier.ClassifyWithSwampFloor(
                gOn,
                (wx, wz) => sampler.GetHeight(wx, wz),
                (wx, wz) => sampler.GetBiome(wx, wz) == BiomeType.Swamp,
                22f);

            long nonSwampChanged = 0;
            foreach (var coord in gOff.AllCoords())
            {
                var c = ZoneGrid.ZoneCenter(coord);
                if (sampler.GetBiome(c.worldX, c.worldZ) == BiomeType.Swamp) continue;
                if (gOff[coord] != gOn[coord]) nonSwampChanged++;
            }
            Assert.Equal(0, nonSwampChanged);   // gated to swamp: nothing else may move
        }

        [Fact]
        public void NullFloor_IsByteIdenticalToDepthOnlyClassify()
        {
            var sampler = PortWorldSampler.FromSeed(NiflheimSeed);
            var gProvider = new ZoneGrid();
            ZoneClassifier.Classify(gProvider, new TestProvider(sampler));
            var gNullFloor = new ZoneGrid();
            ZoneClassifier.ClassifyWithSwampFloor(
                gNullFloor,
                (wx, wz) => sampler.GetHeight(wx, wz),
                (wx, wz) => sampler.GetBiome(wx, wz) == BiomeType.Swamp,
                swampLandFloor: null);

            foreach (var coord in gProvider.AllCoords())
                Assert.Equal(gProvider[coord], gNullFloor[coord]);   // null floor == legacy behaviour
        }

        private sealed class TestProvider : IWorldDataProvider
        {
            private readonly PortWorldSampler s;
            public TestProvider(PortWorldSampler s) { this.s = s; }
            public string WorldId => s.WorldId;
            public float WaterLevel => ZoneClassifier.DefaultWaterLevel;
            public float GetTerrainHeight(float wx, float wz) => s.GetHeight(wx, wz);
        }
    }
}
