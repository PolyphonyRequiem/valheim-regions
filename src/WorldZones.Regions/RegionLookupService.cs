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
        private readonly Dictionary<int, Vector2i> identityById;

        public RegionLookupService(ZoneGrid grid, int[,] regionIdGrid, string worldId, IEnumerable<int> knownRegionIds)
            : this(grid, regionIdGrid, worldId, knownRegionIds, null)
        {
        }

        /// <summary>
        /// Constructs a lookup service that resolves durable region identity (RegionKey) via the
        /// supplied <paramref name="identityById"/> map (region int ID → its <see cref="RegionKey"/>
        /// identity coordinate). When the map is null, names fall back to the legacy int-ID hash —
        /// retained for tests and callers that don't yet thread identity through.
        /// </summary>
        public RegionLookupService(ZoneGrid grid, int[,] regionIdGrid, string worldId,
            IEnumerable<int> knownRegionIds, Dictionary<int, Vector2i> identityById)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.regionIdGrid = regionIdGrid ?? throw new ArgumentNullException(nameof(regionIdGrid));
            this.worldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
            this.knownRegionIds = knownRegionIds == null
                ? throw new ArgumentNullException(nameof(knownRegionIds))
                : new HashSet<int>(knownRegionIds);
            this.identityById = identityById;
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

            // Durable identity: derive the name from the coordinate-stable RegionKey when the
            // identity map is available; otherwise fall back to the legacy int-ID hash.
            if (this.identityById != null && this.identityById.TryGetValue(regionId, out var identityCoord))
            {
                string key = RegionKey.From(identityCoord);
                return new RegionLookupResult
                {
                    HasRegion = true,
                    RegionId = regionId,
                    RegionKey = key,
                    RegionName = RegionGuidNameService.CreateDeterministicName(this.worldId, key),
                    ResolutionReason = RegionResolutionReason.Resolved
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
