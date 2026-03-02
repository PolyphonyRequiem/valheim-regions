using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// A connected inland-water component considered as a single attribution unit.
    /// </summary>
    public sealed class InlandWaterBody
    {
        public int WaterBodyId { get; set; }

        public int ZoneCount => this.Zones.Count;

        public WaterConnectivityKind WaterConnectivity { get; set; }

        public List<Vector2i> Zones { get; } = new List<Vector2i>();

        public HashSet<int> AdjacentRegionIds { get; } = new HashSet<int>();
    }
}
