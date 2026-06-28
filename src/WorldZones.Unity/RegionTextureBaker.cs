using System.Collections.Generic;
using UnityEngine;
using WorldZones.Regions;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Unity
{
    /// <summary>
    /// The area-FILL primitive (baked texture: one quad, pans + zooms free via <c>uvRect</c>, soft
    /// drawn-map edge). Consumes the runtime's <c>int[,] regionIdGrid</c>
    /// (<c>RegionWorld.RegionIdGrid</c>, indexed <c>[gy, gx]</c>, <c>&lt; 0</c> = unassigned) + a
    /// region-label→palette map, and bakes a <see cref="Texture2D"/> a consumer samples under the map
    /// quad. Feeds the borders+tint and parchment dial stops.
    ///
    /// <para>🔴 The grid orientation + origin are LOAD-BEARING. This baker MUST use the IDENTICAL
    /// indexing (<c>[gy, gx]</c>) and lattice origin as Tier-1's <c>RegionBoundaryExtractor.Corner</c>
    /// (<c>world = (c + minIndex)·64 − 32</c>), so the fill texture and the ink seams share ONE
    /// coordinate frame with no half-texel drift (AC-T2-FILL-2). The cell size is bound to
    /// <see cref="ZoneGrid.ZoneSize"/> — the same const the extractor uses — not a re-typed literal.</para>
    ///
    /// <para>UnityEngine-only (plus our own pure topology const). The palette is supplied by Tier-3,
    /// so colour policy (lightness-stepped, colourblind-safe) lives there; the baker is hue-agnostic
    /// (AC-T2-FILL-3).</para>
    /// See docs/design/region-render-seam.md.
    /// </summary>
    public sealed class RegionTextureBaker
    {
        // Lattice geometry captured from the last Bake, so WorldAlignedUvRect can position the baked
        // texture's world window without re-passing the grid.
        private double bakedOriginX;
        private double bakedOriginZ;
        private double bakedSpanX;
        private double bakedSpanZ;
        private bool baked;

        /// <summary>
        /// Bake the region-id grid into an RGBA32 texture: texel <c>[gy,gx]</c> paints
        /// <c>paletteByLabel[regionIdGrid[gy,gx]]</c>; an unassigned cell (<c>id &lt; 0</c>) or a label
        /// outside the palette bakes to <paramref name="unassigned"/> (transparent for an overlay fill
        /// — AC-T2-FILL-1). The texture row index equals <c>gy</c> (no flip): increasing <c>gy</c> is
        /// increasing world-Z is increasing texture-V, matching the projection.
        /// </summary>
        /// <param name="regionIdGrid">Per-zone region int label, indexed <c>[gy, gx]</c>; <c>&lt; 0</c> = unassigned.</param>
        /// <param name="minIndex">The grid's minimum zone coordinate on each axis (<c>RegionWorld.Grid.MinIndex</c>).</param>
        /// <param name="paletteByLabel">Colour per int label (the SAME label the grid carries, i.e.
        ///   <c>RegionInfo.TransientId</c>). Indexed directly by the grid value.</param>
        /// <param name="unassigned">Colour for unassigned / out-of-palette cells (transparent for an overlay).</param>
        public Texture2D Bake(int[,] regionIdGrid, int minIndex, IReadOnlyList<Color32> paletteByLabel,
                              Color32 unassigned)
        {
            int gridH = regionIdGrid.GetLength(0); // gy extent (rows)
            int gridW = regionIdGrid.GetLength(1); // gx extent (cols)

            // 🔴 Bake a ONE-TEXEL TRANSPARENT BORDER around the grid (texture = grid + 2). The texture is
            // sampled WrapMode.Clamp, so when the displayed uvRect runs past [0,1] (zoom-out / pan, and the
            // ±8192 m vanilla map texture is SMALLER than the ~±10500 m world so this happens routinely),
            // Clamp repeats the OUTERMOST texel outward. Without the border, a region that reaches the grid
            // edge (ForTheWort regions reach ±10016 m) bakes a COLOURED edge texel, and Clamp smears it into
            // long biome-coloured BARS across the void — the old "fill beams" bug. With a transparent border
            // the repeated texel is always alpha-0, so the smear is invisible. Universal: independent of map
            // extent or any clip shape. Cost: one 64 m ring (ocean / world-wall), no real territory lost.
            const int pad = 1;
            int width = gridW + 2 * pad;
            int height = gridH + 2 * pad;

            // Identical lattice origin/span as RegionBoundaryExtractor.Corner, shifted OUT by the pad ring:
            // grid corner (0,0) sits at world ((0+minIndex)·64 − 32); the padded texture's corner sits one
            // cell further out so the grid maps to the texture interior with no drift.
            const double cell = ZoneGrid.ZoneSize;            // 64 — single source of truth
            const double half = ZoneGrid.ZoneSize / 2.0;      // 32 — corner offset off the zone centre
            this.bakedOriginX = (minIndex - pad) * cell - half;
            this.bakedOriginZ = (minIndex - pad) * cell - half;
            this.bakedSpanX = width * cell;
            this.bakedSpanZ = height * cell;
            this.baked = true;

            var pixels = new Color32[width * height];
            // Seed EVERYTHING (interior + the border ring) to the transparent unassigned colour; the loop
            // below overwrites only the interior cells that carry a real region label.
            for (int i = 0; i < pixels.Length; i++) pixels[i] = unassigned;

            IReadOnlyList<Color32> palette = paletteByLabel ?? System.Array.Empty<Color32>();
            int paletteCount = palette.Count;
            for (int gy = 0; gy < gridH; gy++)
            {
                int row = (gy + pad) * width; // grid row gy → texture row gy+pad (inside the border)
                for (int gx = 0; gx < gridW; gx++)
                {
                    int label = regionIdGrid[gy, gx];
                    // Texture2D pixel index = y*width + x with y=0 the BOTTOM row; gy already increases
                    // with world-Z, so row gy maps directly to texture-Y gy+pad (no flip).
                    pixels[row + (gx + pad)] = (label >= 0 && label < paletteCount)
                        ? palette[label]
                        : unassigned;
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WZ_RegionFill",
                // Clamp so the (now TRANSPARENT) rim texel doesn't wrap; Point so adjacent region colours
                // stay pure (no cross-seam bleed under colourblind lightness stepping). Tier-3 may override
                // to Bilinear for a softer drawn-map edge — a styling choice, reversible.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Point,
            };
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        /// <summary>
        /// The <c>uvRect</c> a consumer sets on its fill <c>RawImage</c> so the baked texture's world
        /// coverage registers under the SAME world window the given <paramref name="mapFrame"/>
        /// describes (so fills line up under the ink strokes — AC-T2-FILL-2). Pass
        /// <see cref="ValheimMapConventions.FullMapFrame"/> to align the whole baked texture to the
        /// vanilla full-map extent; pass a frame matching the currently-displayed sub-window (centre +
        /// span of vanilla's panned/zoomed view) to track pan/zoom. Returns <c>0,0,1,1</c> if called
        /// before <see cref="Bake"/>.
        /// </summary>
        public Rect WorldAlignedUvRect(MapFrame mapFrame)
        {
            if (!this.baked || mapFrame == null) return new Rect(0f, 0f, 1f, 1f);

            // World window the frame shows, in baked-texture UV.
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
