using WorldZones.Runtime.Geometry;

namespace WorldZones.Unity
{
    /// <summary>
    /// The Valheim world-map constants as DATA — not a live game read, not a reference to
    /// <c>assembly_valheim</c>. Decomp-verified against <c>Minimap</c> (sbpr-corpus
    /// <c>subsystems/Minimap.cs</c>); a Tier-3 consumer that holds a live <c>Minimap</c> passes the
    /// live <c>m_pixelSize</c>/<c>m_textureSize</c> in, and these are the documented defaults + the
    /// world→map-UV formula encoded as a <see cref="MapFrame"/> the Tier-1 projector consumes. This
    /// is the "valheim-modding-informed" knowledge encoded, not coupled. See
    /// docs/design/region-render-seam.md (<c>## Steps 2–3 lock</c>).
    /// </summary>
    public static class ValheimMapConventions
    {
        /// <summary>Metres per fog/map texel — <c>Minimap.m_pixelSize</c> (Minimap.cs:213).</summary>
        public const float DefaultPixelSize = 64f;

        /// <summary>Fog/map texture is 256² — <c>Minimap.m_textureSize</c> (Minimap.cs:211).</summary>
        public const int DefaultTextureSize = 256;

        /// <summary>
        /// Region-GEN grid radius (<c>ZoneGrid.WorldRadius</c>). NOTE the real WALLED world is ±10500
        /// (EnvMan edge-of-world; player pushback @10420), and real ForTheWort regions reach centroid
        /// ~10008 m / bounds-corner ~10637 m at the diagonals — so this is the generation radius, not
        /// the texture reach (see <see cref="FullMapWorldSpan"/>).
        /// </summary>
        public const float WorldRadius = 10000f;

        /// <summary>
        /// Full-map world span on each axis = <c>pixelSize · textureSize</c> = 64 · 256 = <b>16384 m</b>
        /// (±8192 m on-axis around the world origin).
        ///
        /// <para>🔴 LOAD-BEARING (the thing that bites): when drawing OVER the vanilla M map, build the
        /// <see cref="MapFrame"/> from THIS span (16384), NOT <see cref="MapFrame.WholeWorld"/> (span
        /// 20000). They are different frames — <c>WholeWorld(10000)</c> is the OFFLINE-render frame
        /// (whole world on one synthetic map); the vanilla M map is the TEXTURE frame. Cross them and
        /// every border renders at 16384/20000 = 0.82× scale, mis-registered against the terrain
        /// underneath. The overlay shares vanilla's WorldToMapPoint, so it clips coincident with
        /// vanilla automatically — do NOT special-case rim clipping.</para>
        ///
        /// <para>The texture is a SQUARE over a CIRCULAR world, so coverage is non-uniform (NOT a clean
        /// rim): on-axis (N/S/E/W) the texture edge 8192 m under-reaches the ~10500 m world wall by
        /// ~2308 m; the diagonal corners reach 8192·√2 ≈ 11585 m, over-reaching the wall by ~1085 m
        /// (ocean). This is vanilla's own behaviour; our borders inherit it for free by sharing the
        /// projection.</para>
        /// </summary>
        public static float FullMapWorldSpan(float pixelSize = DefaultPixelSize, int textureSize = DefaultTextureSize)
            => pixelSize * textureSize;

        /// <summary>
        /// The vanilla <c>WorldToMapPoint</c> projection (Minimap.cs:1496) expressed as a
        /// <see cref="MapFrame"/> the Tier-1 <see cref="MapProjector"/> consumes: centre = world origin
        /// (UV 0.5,0.5); span = <c>pixelSize · textureSize</c> on both axes; rotation 0 (the M map is
        /// north-up). A consumer with a live <c>Minimap</c> passes <c>minimap.m_pixelSize</c> /
        /// <c>minimap.m_textureSize</c> here so the frame tracks the actual runtime values.
        ///
        /// <para>This round-trips the vanilla inverse transform exactly: vanilla's
        /// <c>TryGetHoverWorldPosition</c> computes <c>world = (mapUv − 0.5) · pixelSize · textureSize</c>,
        /// which is precisely <see cref="MapProjector.Unproject"/> under this frame.</para>
        /// </summary>
        public static MapFrame FullMapFrame(float pixelSize = DefaultPixelSize, int textureSize = DefaultTextureSize)
        {
            double span = (double)pixelSize * textureSize;
            return new MapFrame(0.0, 0.0, span, span, 0.0);
        }
    }
}
