using System;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// A parameterised description of a map view: where it is centred in the world, how many world
    /// metres it spans across each normalised axis, and its rotation. This is the seam that lets the
    /// projection be a PURE function with no live game read.
    ///
    /// <para>A consumer that owns a map surface supplies its OWN frame: the vanilla full map, a
    /// Trailborne circular disc, or a future regional-map item each build a <see cref="MapFrame"/>
    /// from their surface's geometry (Tier-2 <c>ValheimMapConventions</c> builds one from a live
    /// <c>Minimap</c>). Tier-1 never reads <c>m_pixelSize</c>/<c>m_textureSize</c>/<c>uvRect</c>
    /// itself — it consumes the frame. See docs/design/region-render-seam.md.</para>
    /// </summary>
    public sealed class MapFrame
    {
        /// <summary>World-X (metres) at the centre of the map view (UV 0.5).</summary>
        public readonly double CenterX;

        /// <summary>World-Z (metres) at the centre of the map view (UV 0.5).</summary>
        public readonly double CenterZ;

        /// <summary>World metres covered across the full U range [0,1] (i.e. the view's world width).</summary>
        public readonly double SpanX;

        /// <summary>World metres covered across the full V range [0,1] (i.e. the view's world height).</summary>
        public readonly double SpanZ;

        /// <summary>
        /// View rotation in radians, counter-clockwise. At 0, world +X maps along +U and world +Z
        /// along +V. A rotating minimap (heading-up) supplies the camera yaw here.
        /// </summary>
        public readonly double RotationRadians;

        public MapFrame(double centerX, double centerZ, double spanX, double spanZ, double rotationRadians = 0.0)
        {
            if (spanX <= 0.0) throw new ArgumentOutOfRangeException(nameof(spanX), "span must be positive");
            if (spanZ <= 0.0) throw new ArgumentOutOfRangeException(nameof(spanZ), "span must be positive");
            this.CenterX = centerX;
            this.CenterZ = centerZ;
            this.SpanX = spanX;
            this.SpanZ = spanZ;
            this.RotationRadians = rotationRadians;
        }

        /// <summary>
        /// A square, axis-aligned frame centred on the world origin covering ±<paramref name="worldRadiusMetres"/>
        /// on each axis — the "whole world on one map" case (offline renders, a full-world overlay).
        /// </summary>
        public static MapFrame WholeWorld(double worldRadiusMetres = 10000.0)
            => new MapFrame(0.0, 0.0, worldRadiusMetres * 2.0, worldRadiusMetres * 2.0, 0.0);
    }
}
