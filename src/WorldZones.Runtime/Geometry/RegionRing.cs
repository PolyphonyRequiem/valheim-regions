using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// A closed boundary loop for one region, as an ordered list of world-space vertices on the
    /// zone-corner lattice. Used for FILLS and outlines (the "borders+tint" / parchment styles).
    ///
    /// <para>A region has one <b>outer</b> ring (wound counter-clockwise, <see cref="IsHole"/> =
    /// false) and zero or more <b>hole</b> rings (wound clockwise, <see cref="IsHole"/> = true) where
    /// inland water or an enclosed other-region sits inside it. This CCW-outer / CW-hole convention
    /// is the standard polygon-with-holes input a triangulator or a UI fill expects. The loop is
    /// implicitly closed: the last vertex connects back to the first (not duplicated).</para>
    /// </summary>
    public sealed class RegionRing
    {
        /// <summary>Durable key of the region this ring bounds.</summary>
        public string RegionKey { get; }

        /// <summary>Ordered loop vertices (world metres). Implicitly closed; first != last.</summary>
        public IReadOnlyList<WzVec2> Vertices { get; }

        /// <summary>
        /// Signed area (world m²) under the shoelace formula with +X/+Z axes. Positive =
        /// counter-clockwise (outer), negative = clockwise (hole).
        /// </summary>
        public double SignedArea { get; }

        /// <summary>True if this is a hole (clockwise winding) inside the region's outer ring.</summary>
        public bool IsHole => this.SignedArea < 0.0;

        public RegionRing(string regionKey, IReadOnlyList<WzVec2> vertices, double signedArea)
        {
            this.RegionKey = regionKey;
            this.Vertices = vertices;
            this.SignedArea = signedArea;
        }
    }
}
