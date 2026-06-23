using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// Loads the EXTRACTED Valheim vegetation catalogue (data/valheim_vegetation_catalogue.json,
    /// produced by tools/vegetation/parse_vegetation.py from an AssetRipper export of the client's
    /// ZoneSystem.m_vegetation) into the model's <see cref="VegetationModel.VegetationConfig"/> list.
    ///
    /// This is the "real configs are an EXTERNAL INPUT" seam named in VegetationModel.cs and
    /// docs/design/vegetation-resource-model.md: the catalogue is asset data that does NOT exist on a
    /// headless box, so it is extracted once (client-gated) and checked into data/. With it loaded,
    /// VegetationModel.ModelZone goes from "honest empty" to a real (upper-bias) deterministic estimate.
    /// </summary>
    public static class VegetationCatalogue
    {
        // The catalogue's BiomeMask uses Valheim's raw Heightmap.Biome bits, which the port's BiomeType
        // enum mirrors EXACTLY (Meadows=1, Swamp=2, Mountain=4, BlackForest=8, Plains=16, AshLands=32,
        // DeepNorth=64, Ocean=256, Mistlands=512 — verified in BiomeType.cs). We still map by NAME (not
        // raw bits) so a future enum divergence fails loudly here rather than silently zeroing filters.
        static readonly Dictionary<string, BiomeType> NameToBiome = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Meadows"] = BiomeType.Meadows,
            ["Swamp"] = BiomeType.Swamp,
            ["Mountain"] = BiomeType.Mountain,
            ["BlackForest"] = BiomeType.BlackForest,
            ["Plains"] = BiomeType.Plains,
            ["AshLands"] = BiomeType.AshLands,
            ["DeepNorth"] = BiomeType.DeepNorth,
            ["Ocean"] = BiomeType.Ocean,
            ["Mistlands"] = BiomeType.Mistlands,
        };

        sealed class CatalogueFile
        {
            public ConfigDto[]? configs { get; set; }
        }

        sealed class ConfigDto
        {
            public string? PrefabName { get; set; }
            public string? VegName { get; set; }
            public bool Enable { get; set; } = true;
            public string[]? Biomes { get; set; }
            public int BiomeMask { get; set; }
            public float Min { get; set; }
            public float Max { get; set; } = 10f;
            public int GroupSizeMin { get; set; } = 1;
            public int GroupSizeMax { get; set; } = 1;
            public float GroupRadius { get; set; }
            public float MinAltitude { get; set; } = -1000f;
            public float MaxAltitude { get; set; } = 1000f;
            public float MinDistanceFromCenter { get; set; }
            public float MaxDistanceFromCenter { get; set; }
            public bool ForcePlacement { get; set; }
            public bool IsResource { get; set; }
        }

        /// <summary>Parse the catalogue JSON into the model's config list. Maps biome NAMES → BiomeType
        /// (OR'd into the bitmask). Throws on a missing/garbled file — a caller asking for vegetation
        /// with a bad catalogue should fail loudly, not silently emit an empty sidecar.</summary>
        public static IReadOnlyList<VegetationModel.VegetationConfig> Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Vegetation catalogue not found: {path}");

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            CatalogueFile? file;
            using (var fs = File.OpenRead(path))
                file = JsonSerializer.Deserialize<CatalogueFile>(fs, opts);

            if (file?.configs == null || file.configs.Length == 0)
                throw new InvalidDataException($"Vegetation catalogue has no configs: {path}");

            var list = new List<VegetationModel.VegetationConfig>(file.configs.Length);
            foreach (var c in file.configs)
            {
                BiomeType biome = BiomeType.None;
                if (c.Biomes != null)
                    foreach (var name in c.Biomes)
                        if (NameToBiome.TryGetValue(name, out var b)) biome |= b;

                list.Add(new VegetationModel.VegetationConfig
                {
                    PrefabName = c.PrefabName ?? c.VegName ?? "",
                    Enable = c.Enable,
                    Biome = biome,
                    Min = c.Min,
                    Max = c.Max,
                    GroupSizeMin = c.GroupSizeMin,
                    GroupSizeMax = c.GroupSizeMax,
                    GroupRadius = c.GroupRadius,
                    MinAltitude = c.MinAltitude,
                    MaxAltitude = c.MaxAltitude,
                    MinDistanceFromCenter = c.MinDistanceFromCenter,
                    MaxDistanceFromCenter = c.MaxDistanceFromCenter,
                    ForcePlacement = c.ForcePlacement,
                    IsResource = c.IsResource,
                });
            }
            return list;
        }
    }
}
