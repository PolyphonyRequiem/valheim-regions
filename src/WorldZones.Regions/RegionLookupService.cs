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
        private readonly IReadOnlyDictionary<string, string> namesByKey;

        public RegionLookupService(ZoneGrid grid, int[,] regionIdGrid, string worldId, IEnumerable<int> knownRegionIds)
            : this(grid, regionIdGrid, worldId, knownRegionIds, null, null)
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
            : this(grid, regionIdGrid, worldId, knownRegionIds, identityById, null)
        {
        }

        /// <summary>
        /// Constructs a lookup service that, in addition to durable identity, resolves the rich
        /// display name via <paramref name="namesByKey"/> (RegionKey → name, produced by the active
        /// <c>IRegionNamer.NameAll</c>). When that map is present, <see cref="ResolveCurrent"/>
        /// PREFERS it over the legacy deterministic hash; when it is null (point-query-only consumers,
        /// tests, identity-less callers) the legacy hash is used unchanged. This is the seam that
        /// lets the multi-schema namer's output reach the live minimap labels.
        /// </summary>
        public RegionLookupService(ZoneGrid grid, int[,] regionIdGrid, string worldId,
            IEnumerable<int> knownRegionIds, Dictionary<int, Vector2i> identityById,
            IReadOnlyDictionary<string, string> namesByKey)
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
            this.namesByKey = namesByKey;
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
                    // Prefer the rich, named-region map (multi-schema namer output) when threaded
                    // through; fall back to the legacy deterministic hash when absent so tests and
                    // point-query-only consumers keep their existing behaviour.
                    RegionName = this.ResolveName(key),
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

        /// <summary>
        /// Resolves the display name for a region identity key: the rich name from the threaded
        /// <c>namesByKey</c> map when present and populated for this key, otherwise the legacy
        /// deterministic hash over (worldId, key). Keeping the hash fallback means an identity-less
        /// or naming-skipped build still yields a stable name.
        /// </summary>
        private string ResolveName(string key)
        {
            if (this.namesByKey != null
                && this.namesByKey.TryGetValue(key, out var richName)
                && !string.IsNullOrWhiteSpace(richName))
            {
                return richName;
            }

            return RegionGuidNameService.CreateDeterministicName(this.worldId, key);
        }
    }
}
