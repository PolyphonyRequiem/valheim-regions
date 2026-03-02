using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// Summary output from inland-water attribution.
    /// </summary>
    public sealed class InlandWaterAttributionResult
    {
        /// <summary>Number of inland-water zones attributed to regions.</summary>
        public int AttributedWaterZoneCount { get; set; }

        /// <summary>Number of inland-water zones left unassigned by safe-fail behavior.</summary>
        public int UnassignedInlandWaterZoneCount { get; set; }

        /// <summary>Number of inland-water bodies attributed to regions.</summary>
        public int AttributedWaterBodyCount { get; set; }

        /// <summary>Number of inland-water bodies left unassigned due to no adjacent owning region.</summary>
        public int UnassignedWaterBodyCount { get; set; }

        /// <summary>Region IDs whose inland-water area changed during attribution.</summary>
        public HashSet<int> ChangedRegionIds { get; } = new HashSet<int>();

        /// <summary>Shared immutable empty result used when attribution is disabled.</summary>
        public static InlandWaterAttributionResult Empty { get; } = new InlandWaterAttributionResult();
    }
}
