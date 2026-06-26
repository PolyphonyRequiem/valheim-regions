using System.Collections.Generic;
using UnityEngine;

namespace WorldZones.Mod.RegionOverlay.Overlay
{
    /// <summary>
    /// Builds the region fill palette indexed by the grid's int label (<c>RegionInfo.TransientId</c>).
    /// 🔴 COLOURBLIND-SAFE (AC-T3-CB-1): Daniel is colourblind, so adjacent regions are differentiated
    /// by LIGHTNESS ONLY — neutral greys on a deterministic lightness ramp, never hue. The ramp hops
    /// by the golden ratio so consecutive labels land far apart in lightness (maximising local
    /// contrast even when neighbouring regions have consecutive ids). This is the TWEAK-ME default;
    /// Daniel can swap the ramp / add hatch in-world. Alpha is applied per style by the controller
    /// (translucent for BordersTint, opaque for Parchment), so the palette here is alpha=255 base.
    /// </summary>
    public static class RegionPalette
    {
        private const float MinLightness = 0.34f;
        private const float MaxLightness = 0.86f;
        private const float GoldenHop = 0.6180339887f;

        /// <summary>
        /// A neutral-grey lightness ramp covering labels <c>0..count-1</c>. Index i (the grid label)
        /// maps to a grey whose lightness is a golden-ratio hop through [<see cref="MinLightness"/>,
        /// <see cref="MaxLightness"/>], so adjacent ids contrast. Alpha 255 (the controller scales it).
        /// </summary>
        public static List<Color32> BuildLightnessRamp(int count)
        {
            var palette = new List<Color32>(Mathf.Max(0, count));
            for (int i = 0; i < count; i++)
            {
                float t = Frac(i * GoldenHop);
                float l = Mathf.Lerp(MinLightness, MaxLightness, t);
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(l * 255f), 0, 255);
                palette.Add(new Color32(g, g, g, 255));
            }
            return palette;
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
