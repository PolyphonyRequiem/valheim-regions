namespace WorldZones.Regions
{
    /// <summary>
    /// Coordinate-derived durable identity for a region (Option B: lowest-coordinate-keyed).
    ///
    /// A region's identity is the MIN seed zone coordinate among all seeds that ended up in it,
    /// under a fixed total order (x then y). This is stable under seed-list reordering, seed-count
    /// changes elsewhere, and merge-order changes — unlike the transient integer index, which is
    /// just the seed's position in the seeds list. See docs/design/region-identity.md.
    /// </summary>
    public static class RegionKey
    {
        /// <summary>
        /// Total order over zone coordinates: x ascending, then y ascending.
        /// Returns &lt;0 if a precedes b, 0 if equal, &gt;0 if a follows b.
        /// </summary>
        public static int Compare(Vector2i a, Vector2i b)
        {
            int cx = a.x.CompareTo(b.x);
            return cx != 0 ? cx : a.y.CompareTo(b.y);
        }

        /// <summary>Returns the lesser of two coordinates under <see cref="Compare"/>.</summary>
        public static Vector2i Min(Vector2i a, Vector2i b) => Compare(a, b) <= 0 ? a : b;

        /// <summary>
        /// Canonical string form of a region identity coordinate, e.g. "r.-3.7".
        /// World-independent (worldId is a separate axis in naming + persistence) and
        /// human-debuggable.
        /// </summary>
        public static string From(Vector2i identityCoord)
        {
            return "r." + identityCoord.x.ToString(System.Globalization.CultureInfo.InvariantCulture)
                 + "." + identityCoord.y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
