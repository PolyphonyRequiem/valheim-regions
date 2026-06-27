using System;
using WorldZones.WorldGen;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// Tier-1 biome → render-colour mapping for the Atlas overlay style. Pure (no Unity): returns
    /// packed RGB bytes so both the headless test net and the Unity baker consume the same source of
    /// truth. Two palettes:
    /// <list type="bullet">
    ///   <item><see cref="Wash"/> — the region-fill tint (slightly punchier than Valheim's flat
    ///     minimap colours so a region reads as a biome territory, but applied at low alpha by the
    ///     consumer so terrain shows through).</item>
    ///   <item><see cref="Glow"/> — the coast-halo colour: the wash colour with an HSV saturation
    ///     FLOOR applied, so muted biomes (Mountain S≈0.10, Mistlands S≈0.27, DeepNorth S≈0.36) still
    ///     glow visibly at the coast. Punchy biomes (Ashlands, Plains) are unaffected.</item>
    /// </list>
    /// Locked 2026-06-26, validated offline on real Niflheim. See docs/design/region-atlas-render.md.
    /// </summary>
    public static class BiomeRenderPalette
    {
        /// <summary>HSV saturation floor applied to the GLOW colour only (validated value).</summary>
        public const double GlowSaturationFloor = 0.55;

        /// <summary>The region-fill wash colour (R,G,B 0..255) for a biome. Alpha is the consumer's.</summary>
        public static (byte r, byte g, byte b) Wash(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.Meadows:     return (150, 200, 90);
                case BiomeType.BlackForest: return (40, 120, 70);
                case BiomeType.Swamp:       return (120, 150, 70);
                case BiomeType.Mountain:    return (225, 235, 250);
                case BiomeType.Plains:      return (225, 200, 80);
                case BiomeType.Mistlands:   return (120, 110, 150);
                case BiomeType.AshLands:    return (230, 70, 50);
                case BiomeType.DeepNorth:   return (150, 210, 235);
                default:                    return (150, 150, 150); // Ocean/None: neutral (unused on land)
            }
        }

        /// <summary>The coast-glow colour for a biome — the wash colour lifted to
        /// <see cref="GlowSaturationFloor"/> minimum HSV saturation so muted biomes still read.</summary>
        public static (byte r, byte g, byte b) Glow(BiomeType biome)
        {
            var (r, g, b) = Wash(biome);
            return SaturationFloor(r, g, b, GlowSaturationFloor);
        }

        /// <summary>
        /// Return (r,g,b) with HSV saturation raised to at least <paramref name="floor"/>, hue and
        /// value preserved. Pure integer-in/integer-out; matches the offline renderer's colorsys path.
        /// </summary>
        public static (byte r, byte g, byte b) SaturationFloor(byte r, byte g, byte b, double floor)
        {
            double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
            double max = Math.Max(rf, Math.Max(gf, bf));
            double min = Math.Min(rf, Math.Min(gf, bf));
            double v = max;
            double s = max <= 0 ? 0 : (max - min) / max;
            if (s >= floor || v <= 0) return (r, g, b);

            // Recover hue, then rebuild at the floored saturation and same value.
            double h;
            double delta = max - min;
            if (delta <= 0) h = 0;
            else if (max == rf) h = ((gf - bf) / delta) % 6.0;
            else if (max == gf) h = (bf - rf) / delta + 2.0;
            else h = (rf - gf) / delta + 4.0;
            h *= 60.0;
            if (h < 0) h += 360.0;

            double sNew = floor;
            double c = v * sNew;
            double x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
            double m = v - c;
            double r2, g2, b2;
            if (h < 60)      { r2 = c; g2 = x; b2 = 0; }
            else if (h < 120){ r2 = x; g2 = c; b2 = 0; }
            else if (h < 180){ r2 = 0; g2 = c; b2 = x; }
            else if (h < 240){ r2 = 0; g2 = x; b2 = c; }
            else if (h < 300){ r2 = x; g2 = 0; b2 = c; }
            else             { r2 = c; g2 = 0; b2 = x; }
            return (Clamp((r2 + m) * 255), Clamp((g2 + m) * 255), Clamp((b2 + m) * 255));
        }

        private static byte Clamp(double v) => (byte)(v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5));
    }
}
