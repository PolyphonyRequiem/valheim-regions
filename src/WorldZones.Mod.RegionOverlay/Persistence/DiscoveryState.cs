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
            return this.IsDiscovered(regionName, regionId, null);
        }

        /// <summary>
        /// Discovery check keyed on a durable <paramref name="regionKey"/> (coordinate-derived) when
        /// supplied — this is the identity-stable path. Falls back to the legacy name#id key when
        /// regionKey is null, preserving old behavior for callers that don't thread identity through.
        /// </summary>
        public bool IsDiscovered(string regionName, int? regionId, string regionKey)
        {
            if (string.IsNullOrWhiteSpace(regionName))
            {
                return false;
            }

            string normalizedName = NormalizeRegionName(regionName);
            string identitySafeKey = string.IsNullOrWhiteSpace(regionKey)
                ? BuildRegionKey(regionName, regionId)
                : regionKey.Trim();
            return this.DiscoveredRegionKeys.Contains(identitySafeKey) || this.DiscoveredRegionNames.Contains(normalizedName);
        }

        public bool TryMarkDiscovered(string regionName, int? regionId)
        {
            return this.TryMarkDiscovered(regionName, regionId, null);
        }

        /// <summary>
        /// Marks a region discovered, keyed on a durable <paramref name="regionKey"/> when supplied.
        /// The stable key (not the transient int ID) is what gets persisted, so saved discovery state
        /// survives seed-list churn from border rewrites / authored seeds / Valheim 1.0.
        /// </summary>
        public bool TryMarkDiscovered(string regionName, int? regionId, string regionKey)
        {
            if (string.IsNullOrWhiteSpace(regionName))
            {
                return false;
            }

            string normalizedName = NormalizeRegionName(regionName);
            string identitySafeKey = string.IsNullOrWhiteSpace(regionKey)
                ? BuildRegionKey(regionName, regionId)
                : regionKey.Trim();

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
