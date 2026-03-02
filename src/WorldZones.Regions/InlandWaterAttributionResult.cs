using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// Summary output from inland-water attribution.
    /// </summary>
    public sealed class InlandWaterAttributionResult
    {
        public int AttributedWaterZoneCount { get; set; }

        public int UnassignedInlandWaterZoneCount { get; set; }

        public int AttributedWaterBodyCount { get; set; }

        public int UnassignedWaterBodyCount { get; set; }

        public HashSet<int> ChangedRegionIds { get; } = new HashSet<int>();

        public static InlandWaterAttributionResult Empty { get; } = new InlandWaterAttributionResult();
    }
}
