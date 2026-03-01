using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// Result of connected-component labeling for land zones.
    /// </summary>
    public class LandComponent
    {
        /// <summary>Zero-based component identifier.</summary>
        public int Id { get; }

        /// <summary>All zone coordinates belonging to this component.</summary>
        public List<Vector2i> Zones { get; } = new List<Vector2i>();

        public LandComponent(int id) => Id = id;
    }
}
