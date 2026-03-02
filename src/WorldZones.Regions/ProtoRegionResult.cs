using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// Summary statistics from proto-region generation.
    /// </summary>
    public class ProtoRegionResult
    {
        /// <summary>All generated proto-regions, sorted by descending area.</summary>
        public List<ProtoRegion> Regions { get; set; } = new List<ProtoRegion>();

        /// <summary>Total number of land zones in the grid.</summary>
        public int LandZoneCount { get; set; }

        /// <summary>Number of proto-regions created (after merge).</summary>
        public int RegionCount { get; set; }

        /// <summary>Smallest region area (zone count).</summary>
        public int MinAreaZones { get; set; }

        /// <summary>Average region area (zone count).</summary>
        public float AvgAreaZones { get; set; }

        /// <summary>Largest region area (zone count).</summary>
        public int MaxAreaZones { get; set; }

        /// <summary>
        /// Number of land zones not assigned to any proto-region.
        /// This equals the total zone count of all minor islets.
        /// </summary>
        public int UnassignedLandCount { get; set; }

        /// <summary>Number of regions merged in the tiny-region merge pass.</summary>
        public int MergedRegionCount { get; set; }

        /// <summary>
        /// Land components too small to get proto-regions
        /// (AreaZones &lt; MinComponentZonesForProto).
        /// </summary>
        public List<MinorIslet> MinorIslets { get; set; } = new List<MinorIslet>();

        /// <summary>Number of minor islets.</summary>
        public int MinorIsletCount { get; set; }

        /// <summary>Total zone count across all minor islets.</summary>
        public int MinorIsletTotalArea { get; set; }

        /// <summary>Number of land components that received proto-region seeding.</summary>
        public int SeededComponentCount { get; set; }

        /// <summary>Number of inland-water zones attributed to regions.</summary>
        public int AttributedWaterZoneCount { get; set; }

        /// <summary>Number of inland-water zones left unassigned by safe-fail behavior.</summary>
        public int UnassignedInlandWaterZoneCount { get; set; }

        /// <summary>Number of inland-water bodies attributed to a region.</summary>
        public int AttributedWaterBodyCount { get; set; }

        /// <summary>Number of inland-water bodies with no valid owning region.</summary>
        public int UnassignedWaterBodyCount { get; set; }

        /// <summary>Total inland-water zones attributed across all regions.</summary>
        public int TotalInlandWaterAreaZones { get; set; }

        /// <summary>Total territory zones across all regions (land + inland water).</summary>
        public int TotalRegionTerritoryAreaZones { get; set; }
    }
}
