using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Pins <see cref="RegionFillMaskBaker"/> behaviour after the 2026-06-29 render-model decisions:
    /// the terrestrial fill clips at the TRUE 30 m waterline (not the 25 m coast iso), so it never
    /// overhangs into water; and the swamp-floor rescue keeps walkable sub-waterline swamp painting as
    /// solid fill (the anti-hole guard, since the coastal FADE is only a band and won't cover inland
    /// sub-waterline swamp). Synthetic sampler so the geometry is exact and deterministic — no world data.
    /// </summary>
    public class RegionFillMaskBakerTests
    {
        // A synthetic world: a vertical coastline at world X = 0. Land (height 40) for X ≥ 0; water that
        // shoals from −∞ up toward the shore so we can place texels at known heights around 30 m and 25 m.
        // height(x) = 30 + x/10  → x=0 is exactly 30 (waterline); x=−50 is 25; x=+100 is 40 (solid land).
        // Optionally a swamp strip (biome=Swamp) on the wet side to exercise the rescue.
        private sealed class RampSampler : IWorldSampler
        {
            private readonly bool swampOnWetSide;
            public RampSampler(bool swampOnWetSide) { this.swampOnWetSide = swampOnWetSide; }
            public string WorldId => "ramp-test";
            public float GetHeight(float worldX, float worldZ) => 30f + worldX / 10f;
            public BiomeType GetBiome(float worldX, float worldZ)
                => (swampOnWetSide && worldX < 0f) ? BiomeType.Swamp : BiomeType.Meadows;
        }

        // One region (label 0) covering the whole grid; minIndex 0. Build a small region-id grid so every
        // zone is "in region 0", then bake the fine mask and read texels by world position.
        private static int[,] AllOneRegion(int zonesW, int zonesH)
        {
            var g = new int[zonesH, zonesW];
            for (int y = 0; y < zonesH; y++)
                for (int x = 0; x < zonesW; x++)
                    g[y, x] = 0;
            return g;
        }

        [Fact]
        public void Fill_ClipsAtThe30mWaterline_NotThe25mCoastIso()
        {
            // Default ctor → coastIso = SeaLevel (30). No swamp rescue.
            var baker = new RegionFillMaskBaker(new RampSampler(swampOnWetSide: false));
            // Grid spanning world X roughly [-32, 32*… ]; minIndex 0 → origin = 0*64 - 32 = -32.
            int[,] region = AllOneRegion(zonesW: 4, zonesH: 1);   // 4 zones wide → fine raster 16 texels at 16 m
            int[,] mask = baker.Bake(region, minIndex: 0, subdivisions: 4);

            int fh = mask.GetLength(0), fw = mask.GetLength(1);
            const double texel = 16.0, origin = -32.0;   // minIndex 0 → origin = 0 - 32
            int landAt30 = 0, waterAt30 = 0, landBetween25and30 = 0;
            for (int fx = 0; fx < fw; fx++)
            {
                double wx = origin + (fx + 0.5) * texel;
                double h = 30.0 + wx / 10.0;
                int label = mask[0, fx];
                if (h >= 30.0) { if (label >= 0) landAt30++; else waterAt30++; }
                // texels with 25 ≤ h < 30 are BELOW the waterline: at the OLD 25 m clip they'd be land;
                // at the new 30 m clip they MUST be water (label −1). This is the "no overhang" guard.
                else if (h >= 25.0 && h < 30.0 && label >= 0) landBetween25and30++;
            }

            Assert.True(landAt30 > 0, "land above the waterline must paint");
            Assert.True(waterAt30 == 0, "no above-waterline texel should be water");
            Assert.Equal(0, landBetween25and30); // ← the 25→30 cleanup: nothing between 25 and 30 fills
        }

        [Fact]
        public void Fill_AboveWaterline_IsSolid_OnInteriorLand()
        {
            var baker = new RegionFillMaskBaker(new RampSampler(swampOnWetSide: false));
            int[,] region = AllOneRegion(8, 1);
            int[,] mask = baker.Bake(region, minIndex: 0, subdivisions: 4);
            const double texel = 16.0, origin = -32.0;
            int fw = mask.GetLength(1);
            // Every texel whose height ≥ 30 must be region 0 (solid interior land, no holes).
            for (int fx = 0; fx < fw; fx++)
            {
                double wx = origin + (fx + 0.5) * texel;
                double h = 30.0 + wx / 10.0;
                if (h >= 30.0) Assert.Equal(0, mask[0, fx]);
            }
        }

        [Fact]
        public void SwampRescue_KeepsSubWaterlineSwamp_AsFill()
        {
            // Swamp floor at 20 m: a swamp texel with 20 ≤ h < 30 is rescued as fill, even below waterline.
            var withRescue = new RegionFillMaskBaker(new RampSampler(swampOnWetSide: true),
                                                     coastIso: HeightScalarField.SeaLevel,
                                                     swampLandFloor: 20.0);
            var noRescue = new RegionFillMaskBaker(new RampSampler(swampOnWetSide: true),
                                                   coastIso: HeightScalarField.SeaLevel,
                                                   swampLandFloor: null);
            int[,] region = AllOneRegion(4, 1);
            int[,] mRescue = withRescue.Bake(region, minIndex: 0, subdivisions: 4);
            int[,] mNone = noRescue.Bake(region, minIndex: 0, subdivisions: 4);

            const double texel = 16.0, origin = -32.0;
            int fw = mRescue.GetLength(1);
            int rescued = 0;
            for (int fx = 0; fx < fw; fx++)
            {
                double wx = origin + (fx + 0.5) * texel;
                double h = 30.0 + wx / 10.0;
                bool swampWet = wx < 0 && h >= 20.0 && h < 30.0;   // swamp side, sub-waterline, above floor
                if (swampWet)
                {
                    Assert.Equal(0, mRescue[0, fx]);   // rescued → fills
                    Assert.Equal(-1, mNone[0, fx]);    // without rescue → water (would be a HOLE)
                    rescued++;
                }
            }
            Assert.True(rescued > 0, "test world must contain at least one sub-waterline swamp texel");
        }
    }
}
