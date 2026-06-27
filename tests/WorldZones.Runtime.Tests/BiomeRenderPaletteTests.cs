using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards <see cref="BiomeRenderPalette"/> — the Atlas overlay's biome → colour mapping. The
    /// load-bearing guarantee is the GLOW saturation floor: muted biomes (Mountain, Mistlands,
    /// DeepNorth) must come back from <see cref="BiomeRenderPalette.Glow"/> with HSV saturation
    /// ≥ the floor so they read at the coast, while already-punchy biomes are returned unchanged.
    /// </summary>
    public class BiomeRenderPaletteTests
    {
        private static double Saturation(byte r, byte g, byte b)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = System.Math.Max(rf, System.Math.Max(gf, bf));
            double min = System.Math.Min(rf, System.Math.Min(gf, bf));
            return max <= 0 ? 0 : (max - min) / max;
        }

        [Theory]
        [InlineData(BiomeType.Mountain)]   // wash S≈0.10
        [InlineData(BiomeType.Mistlands)]  // wash S≈0.27
        [InlineData(BiomeType.DeepNorth)]  // wash S≈0.36
        public void Glow_LiftsMutedBiomes_ToTheSaturationFloor(BiomeType biome)
        {
            var (wr, wg, wb) = BiomeRenderPalette.Wash(biome);
            var (gr, gg, gb) = BiomeRenderPalette.Glow(biome);

            // The wash for these is below the floor; the glow must be raised to (at least) the floor.
            Assert.True(Saturation(wr, wg, wb) < BiomeRenderPalette.GlowSaturationFloor,
                "precondition: this biome's wash is a muted (sub-floor) colour");
            Assert.True(Saturation(gr, gg, gb) >= BiomeRenderPalette.GlowSaturationFloor - 0.02,
                "glow must lift a muted biome to the saturation floor");
        }

        [Theory]
        [InlineData(BiomeType.AshLands)]   // wash S≈0.78
        [InlineData(BiomeType.Plains)]     // wash S≈0.64
        public void Glow_LeavesPunchyBiomes_Unchanged(BiomeType biome)
        {
            var (wr, wg, wb) = BiomeRenderPalette.Wash(biome);
            var (gr, gg, gb) = BiomeRenderPalette.Glow(biome);
            // Already above the floor → returned byte-identical.
            Assert.Equal((wr, wg, wb), (gr, gg, gb));
        }

        [Fact]
        public void SaturationFloor_PreservesHueAndValue()
        {
            // Mistlands wash (120,110,150) — violet, low saturation. After flooring, the max channel
            // (blue) stays the brightest and the min channel can only drop (saturation rises), so the
            // hue family (violet: B ≥ R ≥ G) is preserved.
            var (r, g, b) = BiomeRenderPalette.SaturationFloor(120, 110, 150, 0.55);
            Assert.True(b >= r && r >= g, "violet hue ordering (B≥R≥G) must survive the floor");
            Assert.True(b >= 150 - 2, "value (max channel) is preserved");
        }
    }
}
