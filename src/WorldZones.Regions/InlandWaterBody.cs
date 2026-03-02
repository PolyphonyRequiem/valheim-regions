using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// A connected inland-water component considered as a single attribution unit.
    /// </summary>
    public sealed class InlandWaterBody
    {
        /// <summary>Deterministic identifier for the water body within one categorization pass.</summary>
        public int WaterBodyId { get; set; }

        /// <summary>Total number of zones in this connected water body.</summary>
        public int ZoneCount => this.Zones.Count;

        /// <summary>Connectivity category for all zones in this body.</summary>
        public WaterConnectivityKind WaterConnectivity { get; set; }

        /// <summary>All zone coordinates in this connected body.</summary>
        public List<Vector2i> Zones { get; } = new List<Vector2i>();

        /// <summary>Set of region identifiers touching this water body via 4-neighbor borders.</summary>
        public HashSet<int> AdjacentRegionIds { get; } = new HashSet<int>();
    }
}
