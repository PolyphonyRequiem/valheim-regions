using System.Collections.Generic;
using UnityEngine;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Unity
{
    /// <summary>
    /// Tier-2 baker for the soft COAST HALO fill: turns a Tier-1 <see cref="CoastHaloField"/> + a
    /// <see cref="CoastHaloMode"/> + a halo colour into an RGBA32 <see cref="Texture2D"/> whose alpha is
    /// the per-texel fade (<see cref="CoastHaloField.Alpha"/>). Because the edge is a FADE, there is no
    /// hard boundary to staircase — this is the soft-fill answer to the "blocky fill" gap that needs no
    /// triangulation and no asset bundle. The texture drops into the SAME map-quad consumer path as
    /// <see cref="RegionTextureBaker"/> (same world-aligned <c>uvRect</c> math via
    /// <see cref="WorldAlignedUvRect"/>), so a Tier-3 consumer pans/zooms it under the map identically.
    ///
    /// <para>UnityEngine-only (plus the pure Tier-1 field); the halo colour is supplied by Tier-3, so
    /// colour policy lives there and the baker is hue-agnostic. <see cref="FilterMode.Bilinear"/> is the
    /// right filter here (unlike the region-id fill's <see cref="FilterMode.Point"/>): the halo is a
    /// smooth alpha ramp with no per-region palette to keep pure, so bilinear softens texel edges into
    /// a cleaner gradient. See docs/design/region-render-seam.md.</para>
    /// </summary>
    public sealed class CoastHaloBaker
    {
        private double bakedOriginX;
        private double bakedOriginZ;
        private double bakedSpanX;
        private double bakedSpanZ;
        private bool baked;

        /// <summary>
        /// Bake the halo field into a texture under a mode. Texel <c>[gy,gx]</c> gets
        /// <paramref name="haloColor"/> with alpha = <c>field.Alpha(mode, gy, gx) · haloColor.a/255</c>
        /// (the colour's own alpha scales the whole halo so Tier-3 controls peak opacity). Returns a
        /// fully-transparent-everywhere texture for <see cref="CoastHaloMode.Off"/> (the consumer
        /// normally just hides the layer instead, but this keeps the call total).
        /// </summary>
        /// <param name="field">The Tier-1 halo field (carries lattice origin/cell + signed distances).</param>
        /// <param name="mode">Which side fades (<see cref="CoastHaloMode.Seaward"/> /
        ///   <see cref="CoastHaloMode.Inland"/>).</param>
        /// <param name="haloColor">Halo RGB; its A is the peak alpha multiplier (255 = full).</param>
        public Texture2D Bake(CoastHaloField field, CoastHaloMode mode, Color32 haloColor)
        {
            int w = field.Width, h = field.Height;

            // Same lattice contract as RegionTextureBaker: the field already carries its world origin +
            // cell, so the texture covers [OriginX, OriginX + Width·Cell] × [OriginZ, OriginZ + Height·Cell].
            this.bakedOriginX = field.OriginX;
            this.bakedOriginZ = field.OriginZ;
            this.bakedSpanX = w * field.Cell;
            this.bakedSpanZ = h * field.Cell;
            this.baked = true;

            double peak = haloColor.a / 255.0;
            var pixels = new Color32[w * h];
            for (int gy = 0; gy < h; gy++)
            {
                int row = gy * w;   // row gy → texture-Y gy (no flip), matching RegionTextureBaker
                bool edgeRow = gy == 0 || gy == h - 1;
                for (int gx = 0; gx < w; gx++)
                {
                    // Force the outermost texel RING fully transparent so WrapMode.Clamp has only alpha-0
                    // to repeat past the texture edge (the ±8192 m map texture is smaller than the world,
                    // so zoom-out/pan runs the uvRect past [0,1] routinely). Without this the rim glow
                    // Clamp-smears into long biome-coloured BARS across the void — the "fill/halo beams" bug.
                    double a = (edgeRow || gx == 0 || gx == w - 1) ? 0.0 : field.Alpha(mode, gy, gx) * peak;
                    byte alpha = (byte)(a <= 0 ? 0 : a >= 1 ? 255 : (int)(a * 255 + 0.5));
                    pixels[row + gx] = new Color32(haloColor.r, haloColor.g, haloColor.b, alpha);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WZ_CoastHalo",
                wrapMode = TextureWrapMode.Clamp,    // rim texel doesn't wrap
                filterMode = FilterMode.Bilinear,    // smooth the alpha ramp (no palette purity to guard)
            };
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>
        /// Bake the halo with PER-REGION biome colour (Atlas): texel <c>[gy,gx]</c> gets the glow colour
        /// of the region whose coast is nearest (<see cref="CoastHaloField.NearestRegionIdAt"/>), looked
        /// up in <paramref name="glowColorByLabel"/> (indexed by <c>RegionInfo.TransientId</c>), with
        /// alpha = <c>field.Alpha(mode, gy, gx) · peakAlpha/255</c>. A texel with no nearest region (−1)
        /// or an out-of-range label falls back to <paramref name="fallback"/>. Same lattice/origin
        /// contract as <see cref="Bake"/>, so the consumer's uvRect math is identical.
        /// </summary>
        public Texture2D BakeBiome(CoastHaloField field, CoastHaloMode mode,
                                   IReadOnlyList<Color32> glowColorByLabel, Color32 fallback, byte peakAlpha)
        {
            int w = field.Width, h = field.Height;
            this.bakedOriginX = field.OriginX;
            this.bakedOriginZ = field.OriginZ;
            this.bakedSpanX = w * field.Cell;
            this.bakedSpanZ = h * field.Cell;
            this.baked = true;

            double peak = peakAlpha / 255.0;
            int paletteCount = glowColorByLabel?.Count ?? 0;
            var pixels = new Color32[w * h];
            for (int gy = 0; gy < h; gy++)
            {
                int row = gy * w;
                bool edgeRow = gy == 0 || gy == h - 1;
                for (int gx = 0; gx < w; gx++)
                {
                    // Outermost texel RING forced transparent — Clamp repeats alpha-0 past the edge, so the
                    // Atlas biome glow can't smear into coloured BARS across the void (see Bake()).
                    double a = (edgeRow || gx == 0 || gx == w - 1) ? 0.0 : field.Alpha(mode, gy, gx) * peak;
                    byte alpha = (byte)(a <= 0 ? 0 : a >= 1 ? 255 : (int)(a * 255 + 0.5));
                    Color32 rgb = fallback;
                    if (alpha > 0)
                    {
                        int rid = field.NearestRegionIdAt(gy, gx);
                        if (rid >= 0 && rid < paletteCount) rgb = glowColorByLabel[rid];
                        // UNINCORPORATED coast (rid<0, or label out of palette range): no region owns this
                        // coast, so nothing should glow here. Decided 2026-06-29 (Daniel): the deep-separated
                        // islands/archipelagos are CORRECTLY unincorporated — the old gold-fallback halo that
                        // smeared around them was a cosmetic wart, not data they want to keep. Force alpha 0 so
                        // unincorporated coast is dark in Atlas, instead of painting the gold HaloColor. (Flat
                        // F7 gold mode in Bake() is untouched — it's the per-region-agnostic debug fallback.)
                        else alpha = 0;
                    }
                    pixels[row + gx] = new Color32(rgb.r, rgb.g, rgb.b, alpha);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WZ_CoastHaloBiome",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>
        /// The <c>uvRect</c> for the halo <c>RawImage</c> so the baked texture registers under the same
        /// world window <paramref name="mapFrame"/> describes — identical contract to
        /// <see cref="RegionTextureBaker.WorldAlignedUvRect"/> (so the halo tracks vanilla's pan/zoom and
        /// sits under the ink). Returns <c>0,0,1,1</c> if called before <see cref="Bake"/>.
        /// </summary>
        public Rect WorldAlignedUvRect(MapFrame mapFrame)
        {
            if (!this.baked || mapFrame == null) return new Rect(0f, 0f, 1f, 1f);

            double frameMinX = mapFrame.CenterX - mapFrame.SpanX * 0.5;
            double frameMinZ = mapFrame.CenterZ - mapFrame.SpanZ * 0.5;

            float uMin = (float)((frameMinX - this.bakedOriginX) / this.bakedSpanX);
            float vMin = (float)((frameMinZ - this.bakedOriginZ) / this.bakedSpanZ);
            float uW = (float)(mapFrame.SpanX / this.bakedSpanX);
            float vH = (float)(mapFrame.SpanZ / this.bakedSpanZ);
            return new Rect(uMin, vMin, uW, vH);
        }
    }
}
