using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BepInEx;
using BepInEx.Logging;

namespace WorldZones.Mod.RegionOverlay.Persistence
{
    public sealed class DiscoveryStore
    {
        private readonly string baseDirectory;
        private readonly ManualLogSource log;
        private readonly Dictionary<string, DiscoveryState> stateCache;

        public DiscoveryStore(ManualLogSource log)
            : this(Path.Combine(Paths.ConfigPath, "WorldZones", "RegionOverlay"), log)
        {
        }

        internal DiscoveryStore(string baseDirectory, ManualLogSource log)
        {
            this.baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
                ? throw new ArgumentException("baseDirectory must not be null or empty", nameof(baseDirectory))
                : baseDirectory;
            this.log = log;
            this.stateCache = new Dictionary<string, DiscoveryState>(StringComparer.OrdinalIgnoreCase);
        }

        public bool CheckAndRecordDiscovery(string worldId, string playerId, string regionName, int? regionId)
        {
            if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(regionName))
            {
                return false;
            }

            string cacheKey = BuildCacheKey(worldId, playerId);
            if (!this.stateCache.TryGetValue(cacheKey, out DiscoveryState? state))
            {
                state = this.Load(worldId, playerId) ?? new DiscoveryState(worldId, playerId);
                this.stateCache[cacheKey] = state;
            }

            bool firstDiscovery = state.TryMarkDiscovered(regionName, regionId);
            if (firstDiscovery)
            {
                this.Save(state);
            }

            return firstDiscovery;
        }

        public DiscoveryState LoadOrCreate(string worldId, string playerId)
        {
            string cacheKey = BuildCacheKey(worldId, playerId);
            if (this.stateCache.TryGetValue(cacheKey, out DiscoveryState? cachedState) && cachedState != null)
            {
                return cachedState;
            }

            DiscoveryState loadedState = this.Load(worldId, playerId) ?? new DiscoveryState(worldId, playerId);
            this.stateCache[cacheKey] = loadedState;
            return loadedState;
        }

        private DiscoveryState? Load(string worldId, string playerId)
        {
            string path = this.GetStatePath(worldId, playerId);
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(DiscoveryStateDocument));
                    var document = serializer.ReadObject(stream) as DiscoveryStateDocument;
                    return this.ToState(worldId, playerId, document);
                }
            }
            catch (Exception ex)
            {
                this.log?.LogWarning($"Failed to read discovery state '{path}': {ex.Message}. Falling back to empty discovery state.");
                return null;
            }
        }

        private void Save(DiscoveryState state)
        {
            string path = this.GetStatePath(state.WorldId, state.PlayerId);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                var document = new DiscoveryStateDocument
                {
                    SchemaVersion = 2,
                    WorldId = state.WorldId,
                    PlayerId = state.PlayerId,
                    DiscoveredRegionKeys = new List<string>(state.DiscoveredRegionKeys),
                    DiscoveredRegionNames = new List<string>(state.DiscoveredRegionNames),
                    LastUpdatedUtc = state.LastUpdatedUtc.ToString("O", CultureInfo.InvariantCulture)
                };

                using (var stream = File.Create(path))
                {
                    var serializer = new DataContractJsonSerializer(typeof(DiscoveryStateDocument));
                    serializer.WriteObject(stream, document);
                }
            }
            catch (Exception ex)
            {
                this.log?.LogWarning($"Failed to write discovery state '{path}': {ex.Message}.");
            }
        }

        private DiscoveryState ToState(string worldId, string playerId, DiscoveryStateDocument? document)
        {
            var state = new DiscoveryState(worldId, playerId);
            if (document == null)
            {
                return state;
            }

            if (document.DiscoveredRegionKeys != null)
            {
                foreach (string key in document.DiscoveredRegionKeys)
                {
                    if (string.IsNullOrWhiteSpace(key))
                    {
                        continue;
                    }

                    state.DiscoveredRegionKeys.Add(key.Trim());
                }
            }

            if (document.DiscoveredRegionNames != null)
            {
                foreach (string name in document.DiscoveredRegionNames)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string normalized = DiscoveryState.NormalizeRegionName(name);
                    state.DiscoveredRegionNames.Add(normalized);
                    state.DiscoveredRegionKeys.Add(DiscoveryState.BuildRegionKey(normalized, null));
                }
            }

            if (document.DiscoveredRegionGuids != null && document.DiscoveredRegionGuids.Count > 0)
            {
                int migratedNames = 0;
                int ignoredGuids = 0;

                foreach (string legacyValue in document.DiscoveredRegionGuids)
                {
                    if (string.IsNullOrWhiteSpace(legacyValue))
                    {
                        continue;
                    }

                    if (Guid.TryParse(legacyValue, out _))
                    {
                        ignoredGuids++;
                        continue;
                    }

                    string normalized = DiscoveryState.NormalizeRegionName(legacyValue);
                    if (string.IsNullOrWhiteSpace(normalized))
                    {
                        continue;
                    }

                    state.DiscoveredRegionNames.Add(normalized);
                    state.DiscoveredRegionKeys.Add(DiscoveryState.BuildRegionKey(normalized, null));
                    migratedNames++;
                }

                this.log?.LogInfo($"Loaded legacy discovery format for world='{worldId}' player='{playerId}': migratedNames={migratedNames}, ignoredGuidKeys={ignoredGuids}.");
            }

            return state;
        }

        private string GetStatePath(string worldId, string playerId)
        {
            string worldSafe = SanitizePathSegment(worldId);
            string playerSafe = SanitizePathSegment(playerId);
            return Path.Combine(this.baseDirectory, worldSafe + "__" + playerSafe + ".json");
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            string candidate = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(candidate) ? "unknown" : candidate;
        }

        private static string BuildCacheKey(string worldId, string playerId)
        {
            return worldId.Trim() + "::" + playerId.Trim();
        }

        [DataContract]
        private sealed class DiscoveryStateDocument
        {
            [DataMember(Name = "schemaVersion", EmitDefaultValue = false)]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "worldId", EmitDefaultValue = false)]
            public string? WorldId { get; set; }

            [DataMember(Name = "playerId", EmitDefaultValue = false)]
            public string? PlayerId { get; set; }

            [DataMember(Name = "discoveredRegionKeys", EmitDefaultValue = false)]
            public List<string>? DiscoveredRegionKeys { get; set; }

            [DataMember(Name = "discoveredRegionNames", EmitDefaultValue = false)]
            public List<string>? DiscoveredRegionNames { get; set; }

            [DataMember(Name = "discoveredRegionGuids", EmitDefaultValue = false)]
            public List<string>? DiscoveredRegionGuids { get; set; }

            [DataMember(Name = "lastUpdatedUtc", EmitDefaultValue = false)]
            public string? LastUpdatedUtc { get; set; }
        }
    }
}
