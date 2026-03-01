using System;
using System.Collections.Generic;

namespace WorldZones.Regions
{
    public sealed class RegionLookupService : IRegionLookupService
    {
        private readonly ZoneGrid grid;
        private readonly int[,] regionIdGrid;
        private readonly string worldId;
        private readonly HashSet<int> knownRegionIds;

        public RegionLookupService(ZoneGrid grid, int[,] regionIdGrid, string worldId, IEnumerable<int> knownRegionIds)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.regionIdGrid = regionIdGrid ?? throw new ArgumentNullException(nameof(regionIdGrid));
            this.worldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
            this.knownRegionIds = knownRegionIds == null
                ? throw new ArgumentNullException(nameof(knownRegionIds))
                : new HashSet<int>(knownRegionIds);
        }

        public RegionLookupResult ResolveCurrent(float worldX, float worldZ)
        {
            var coord = ZoneGrid.WorldToZoneCoord(worldX, worldZ);
            if (!this.grid.InBounds(coord))
            {
                return new RegionLookupResult
                {
                    HasRegion = false,
                    ResolutionReason = RegionResolutionReason.OutOfBounds
                };
            }

            int regionId = this.regionIdGrid[coord.y - this.grid.MinIndex, coord.x - this.grid.MinIndex];
            if (regionId < 0)
            {
                return new RegionLookupResult
                {
                    HasRegion = false,
                    ResolutionReason = RegionResolutionReason.Unassigned
                };
            }

            if (!this.knownRegionIds.Contains(regionId))
            {
                return new RegionLookupResult
                {
                    HasRegion = false,
                    RegionId = regionId,
                    ResolutionReason = RegionResolutionReason.DataUnavailable
                };
            }

            return new RegionLookupResult
            {
                HasRegion = true,
                RegionId = regionId,
                RegionName = RegionGuidNameService.CreateDeterministicName(this.worldId, regionId),
                ResolutionReason = RegionResolutionReason.Resolved
            };
        }
    }
}
