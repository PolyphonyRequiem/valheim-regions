using System;
using System.Collections.Generic;

namespace WorldZones.Mod.RegionOverlay.Persistence
{
    public sealed class DiscoveryState
    {
        public DiscoveryState(string worldId, string playerId)
        {
            this.WorldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
            this.PlayerId = string.IsNullOrWhiteSpace(playerId)
                ? throw new ArgumentException("playerId must not be null or empty", nameof(playerId))
                : playerId;

            this.DiscoveredRegionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.DiscoveredRegionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            this.LastUpdatedUtc = DateTime.UtcNow;
        }

        public string WorldId { get; }

        public string PlayerId { get; }

        public HashSet<string> DiscoveredRegionKeys { get; }

        public HashSet<string> DiscoveredRegionNames { get; }

        public DateTime LastUpdatedUtc { get; private set; }

        public bool IsDiscovered(string regionName, int? regionId)
        {
            if (string.IsNullOrWhiteSpace(regionName))
            {
                return false;
            }

            string normalizedName = NormalizeRegionName(regionName);
            string identitySafeKey = BuildRegionKey(regionName, regionId);
            return this.DiscoveredRegionKeys.Contains(identitySafeKey) || this.DiscoveredRegionNames.Contains(normalizedName);
        }

        public bool TryMarkDiscovered(string regionName, int? regionId)
        {
            if (string.IsNullOrWhiteSpace(regionName))
            {
                return false;
            }

            string normalizedName = NormalizeRegionName(regionName);
            string identitySafeKey = BuildRegionKey(regionName, regionId);

            if (this.DiscoveredRegionKeys.Contains(identitySafeKey) || this.DiscoveredRegionNames.Contains(normalizedName))
            {
                return false;
            }

            this.DiscoveredRegionKeys.Add(identitySafeKey);
            this.DiscoveredRegionNames.Add(normalizedName);
            this.LastUpdatedUtc = DateTime.UtcNow;
            return true;
        }

        public static string BuildRegionKey(string regionName, int? regionId)
        {
            if (string.IsNullOrWhiteSpace(regionName))
            {
                return string.Empty;
            }

            string normalizedName = NormalizeRegionName(regionName);
            if (!regionId.HasValue || regionId.Value < 0)
            {
                return normalizedName;
            }

            return normalizedName + "#" + regionId.Value.ToString();
        }

        public static string NormalizeRegionName(string regionName)
        {
            return string.IsNullOrWhiteSpace(regionName)
                ? string.Empty
                : regionName.Trim();
        }
    }
}
