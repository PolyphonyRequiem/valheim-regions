using System.Collections.Generic;
using WorldZones.WorldGen;

namespace WorldZones.Regions
{
    /// <summary>
    /// A connected component of Land ∪ Shallow zones (the continental shelf).
    /// Each shelf component may contain zero or more <see cref="LandComponent"/>s.
    /// </summary>
    public class ShelfComponent
    {
        /// <summary>Zero-based component identifier.</summary>
        public int Id { get; }

        /// <summary>All zone coordinates belonging to this shelf component.</summary>
        public List<Vector2i> Zones { get; } = new List<Vector2i>();

        /// <summary>IDs of <see cref="LandComponent"/>s fully contained within this shelf.</summary>
        public List<int> ContainedLandComponentIds { get; } = new List<int>();

        public ShelfComponent(int id) => Id = id;
    }
}
