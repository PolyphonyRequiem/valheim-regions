// The JSON catalogue loader is OFFLINE TOOLING — it is compiled only for net8.0 (the CLI / tests /
// headless host), NOT for the net472 ship target. This keeps System.Text.Json off the in-process
// consumer surface: a mod referencing WorldZones.Runtime gets the location API (PortLocationSource,
// the model) without inheriting a System.Text.Json net472 package dependency. A live mod never parses
// the catalogue JSON anyway — it reads ZoneSystem.m_locationInstances. Offline callers supply configs
// via this loader; in-process callers pass an already-parsed IReadOnlyList<LocationConfig>.
#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldZones.WorldGen;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Loads the extracted ZoneLocation catalogue (data/valheim_locations_catalogue.json, produced by
    /// tools/locations/parse_locations.py from an AssetRipper export of the client's
    /// <c>ZoneSystem.m_locations</c>) into <see cref="LocationModel.LocationConfig"/> records — the
    /// configs <see cref="PortLocationSource"/> needs. Mirrors the vegetation catalogue loader.
    ///
    /// <para>The catalogue is asset data (does not exist on a headless box without an extraction), so it
    /// is extracted once and checked into <c>data/</c>. This is the "configs are an external input" seam:
    /// the port computes positions, the catalogue says which locations exist and with what filters.</para>
    /// </summary>
    public static class LocationCatalogue
    {
        private static readonly Dictionary<string, BiomeType> NameToBiome =
            new Dictionary<string, BiomeType>(StringComparer.OrdinalIgnoreCase)
            {
                ["Meadows"] = BiomeType.Meadows, ["Swamp"] = BiomeType.Swamp, ["Mountain"] = BiomeType.Mountain,
                ["BlackForest"] = BiomeType.BlackForest, ["Plains"] = BiomeType.Plains, ["AshLands"] = BiomeType.AshLands,
                ["DeepNorth"] = BiomeType.DeepNorth, ["Ocean"] = BiomeType.Ocean, ["Mistlands"] = BiomeType.Mistlands,
            };

        private sealed class CatFile { public LocDto[] locations { get; set; } }

        private sealed class LocDto
        {
            public string PrefabName { get; set; }
            public bool Enable { get; set; } = true;
            public string[] Biomes { get; set; }
            public int BiomeAreaMask { get; set; }
            public int Quantity { get; set; }
            public bool Prioritized { get; set; }
            public bool CenterFirst { get; set; }
            public bool Unique { get; set; }
            public string Group { get; set; }
            public string GroupMax { get; set; }
            public float MinDistanceFromSimilar { get; set; }
            public float MaxDistanceFromSimilar { get; set; }
            public float ExteriorRadius { get; set; }
            public float InteriorRadius { get; set; }
            public float MinTerrainDelta { get; set; }
            public float MaxTerrainDelta { get; set; } = 10f;
            public float MinAltitude { get; set; } = -1000f;
            public float MaxAltitude { get; set; } = 1000f;
            public bool InForest { get; set; }
            public float ForestTresholdMin { get; set; }
            public float ForestTresholdMax { get; set; }
            public float MinDistanceFromCenter { get; set; }
            public float MaxDistanceFromCenter { get; set; }
            public float MinDistance { get; set; }
            public float MaxDistance { get; set; }
        }

        /// <summary>Parse the catalogue JSON into the port's config list. Maps biome NAMES → BiomeType.
        /// Throws on a missing/empty file — a caller asking for locations with a bad catalogue should
        /// fail loudly, not silently produce an empty world.</summary>
        public static IReadOnlyList<LocationModel.LocationConfig> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Location catalogue not found: {path}");

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            CatFile file;
            using (var fs = File.OpenRead(path))
                file = JsonSerializer.Deserialize<CatFile>(fs, opts);

            if (file?.locations == null || file.locations.Length == 0)
                throw new InvalidDataException($"Location catalogue has no locations: {path}");

            var list = new List<LocationModel.LocationConfig>(file.locations.Length);
            foreach (var c in file.locations)
            {
                BiomeType biome = BiomeType.None;
                if (c.Biomes != null)
                    foreach (var n in c.Biomes)
                        if (NameToBiome.TryGetValue(n, out var b)) biome |= b;

                list.Add(new LocationModel.LocationConfig
                {
                    PrefabName = c.PrefabName ?? "",
                    Enable = c.Enable,
                    Quantity = c.Quantity,
                    Prioritized = c.Prioritized,
                    CenterFirst = c.CenterFirst,
                    Unique = c.Unique,
                    Group = c.Group ?? "",
                    GroupMax = c.GroupMax ?? "",
                    Biome = biome,
                    BiomeArea = c.BiomeAreaMask == 0 ? 7 : c.BiomeAreaMask,
                    MinDistanceFromSimilar = c.MinDistanceFromSimilar,
                    MaxDistanceFromSimilar = c.MaxDistanceFromSimilar,
                    ExteriorRadius = c.ExteriorRadius,
                    InteriorRadius = c.InteriorRadius,
                    MinTerrainDelta = c.MinTerrainDelta,
                    MaxTerrainDelta = c.MaxTerrainDelta,
                    MinAltitude = c.MinAltitude,
                    MaxAltitude = c.MaxAltitude,
                    InForest = c.InForest,
                    ForestTresholdMin = c.ForestTresholdMin,
                    ForestTresholdMax = c.ForestTresholdMax,
                    MinDistanceFromCenter = c.MinDistanceFromCenter,
                    MaxDistanceFromCenter = c.MaxDistanceFromCenter,
                    MinDistance = c.MinDistance,
                    MaxDistance = c.MaxDistance,
                });
            }
            return list;
        }
    }
}
#endif
