using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// A shelf component flagged as an archipelago candidate — it contains
    /// multiple small land components with no single dominant landmass.
    /// Metadata only; does not create regions in v0.
    /// </summary>
    public class ArchipelagoCandidate
    {
        /// <summary>Zero-based candidate identifier.</summary>
        public int Id { get; }

        /// <summary>ID of the parent <see cref="ShelfComponent"/> that was flagged.</summary>
        public int ShelfComponentId { get; }

        /// <summary>IDs of the <see cref="LandComponent"/>s within this archipelago.</summary>
        public List<int> LandComponentIds { get; }

        /// <summary>
        /// Total land area across all member land components, in zone counts.
        /// Multiply by 64² (4096) to get square metres.
        /// </summary>
        public int TotalLandZoneCount { get; }

        /// <summary>
        /// Fraction of <see cref="TotalLandZoneCount"/> occupied by the single largest
        /// land component. Range [0, 1]. A value close to 1 means one island dominates.
        /// </summary>
        public float DominantLandShare { get; }

        public ArchipelagoCandidate(
            int id,
            int shelfComponentId,
            List<int> landComponentIds,
            int totalLandZoneCount,
            float dominantLandShare)
        {
            Id = id;
            ShelfComponentId = shelfComponentId;
            LandComponentIds = landComponentIds;
            TotalLandZoneCount = totalLandZoneCount;
            DominantLandShare = dominantLandShare;
        }
    }
}
