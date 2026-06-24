using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// Validation harness for the ported <see cref="LocationModel"/>. Runs the offline port against a
    /// real world's seed and DIFFS the computed placements against the ground-truth locations decoded
    /// from that world's <c>.db</c> (tools/locations/decode_locations.py). This is how we resolve the
    /// one empirical unknown — <c>insideUnitCircle</c>'s native draw pattern — by sweeping
    /// <see cref="InsideUnitCircleStrategy"/> and reporting which reproduces the real placements best.
    ///
    /// Usage:
    ///   WorldZones.Cli locations --seed ForTheWort --catalogue locations.json --oracle niflheim_locations_raw.json
    /// </summary>
    public static class LocationValidation
    {
        // ---------- catalogue loader (mirrors VegetationCatalogue) ----------

        static readonly Dictionary<string, BiomeType> NameToBiome = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Meadows"] = BiomeType.Meadows, ["Swamp"] = BiomeType.Swamp, ["Mountain"] = BiomeType.Mountain,
            ["BlackForest"] = BiomeType.BlackForest, ["Plains"] = BiomeType.Plains, ["AshLands"] = BiomeType.AshLands,
            ["DeepNorth"] = BiomeType.DeepNorth, ["Ocean"] = BiomeType.Ocean, ["Mistlands"] = BiomeType.Mistlands,
        };

        sealed class CatFile { public LocDto[]? locations { get; set; } }
        sealed class LocDto
        {
            public string? PrefabName { get; set; }
            public bool Enable { get; set; } = true;
            public int BiomeMask { get; set; }
            public string[]? Biomes { get; set; }
            public int BiomeAreaMask { get; set; }
            public int Quantity { get; set; }
            public bool Prioritized { get; set; }
            public bool CenterFirst { get; set; }
            public bool Unique { get; set; }
            public string? Group { get; set; }
            public string? GroupMax { get; set; }
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

        public static IReadOnlyList<LocationModel.LocationConfig> LoadCatalogue(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException($"Location catalogue not found: {path}");
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            CatFile? file;
            using (var fs = File.OpenRead(path)) file = JsonSerializer.Deserialize<CatFile>(fs, opts);
            if (file?.locations == null || file.locations.Length == 0)
                throw new InvalidDataException($"Catalogue has no locations: {path}");

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

        // ---------- oracle loader (.db decode JSON) ----------

        sealed class OracleFile { public OracleLoc[]? locations { get; set; } }
        sealed class OracleLoc { public string? prefab { get; set; } public float x { get; set; } public float z { get; set; } public bool placed { get; set; } }

        static List<(string prefab, float x, float z)> LoadOracle(string path)
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            OracleFile? f;
            using (var fs = File.OpenRead(path)) f = JsonSerializer.Deserialize<OracleFile>(fs, opts);
            return (f?.locations ?? Array.Empty<OracleLoc>())
                .Select(l => (l.prefab ?? "", l.x, l.z)).ToList();
        }

        // ---------- the harness ----------

        public static int Run(string seed, string cataloguePath, string? oraclePath, string? onlyStrategy = null)
        {
            Console.WriteLine($"=== Location port validation — seed '{seed}' ===");
            var catalogue = LoadCatalogue(cataloguePath);
            int worldSeed = seed.GetStableHashCode();
            var gen = new WorldGenerator(seed);

            var enabled = catalogue.Where(c => c.Enable && c.Quantity != 0).ToList();
            Console.WriteLine($"catalogue: {catalogue.Count} configs, {enabled.Count} enabled, " +
                              $"target sum = {enabled.Sum(c => c.Quantity)}");

            // oracle (optional — without it we just report computed counts)
            Dictionary<string, List<(float x, float z)>>? oracleByPrefab = null;
            if (oraclePath != null && File.Exists(oraclePath))
            {
                var oracle = LoadOracle(oraclePath);
                oracleByPrefab = oracle.GroupBy(o => o.prefab)
                    .ToDictionary(g => g.Key, g => g.Select(o => (o.x, o.z)).ToList());
                Console.WriteLine($"oracle: {oracle.Count} real placed locations, {oracleByPrefab.Count} prefabs");
            }
            else
            {
                Console.WriteLine("oracle: (none — reporting computed counts only)");
            }

            foreach (InsideUnitCircleStrategy strat in Enum.GetValues(typeof(InsideUnitCircleStrategy)))
            {
                if (onlyStrategy != null && !strat.ToString().Equals(onlyStrategy, StringComparison.OrdinalIgnoreCase))
                    continue;
                Console.WriteLine($"\n──────── strategy: {strat} ────────");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var placed = LocationModel.Generate(worldSeed, gen, catalogue, strat);
                sw.Stop();

                var byPrefab = placed.GroupBy(p => p.PrefabName)
                    .ToDictionary(g => g.Key, g => g.Select(p => (p.X, p.Z)).ToList());
                Console.WriteLine($"computed {placed.Count} placements in {sw.Elapsed.TotalSeconds:F1}s");

                if (oracleByPrefab == null) continue;

                // Position-level bit-exactness: for each computed placement, is there a real placement
                // of the same prefab within 0.5 m? (exact = same RNG stream produced the same point)
                int exact = 0, total = 0, countMatch = 0, prefabsCompared = 0;
                int within50 = 0, within500 = 0;   // tiered: region-scale agreement for the substrate use
                double sumNearest = 0;
                int prioExact = 0, prioTotal = 0;   // prioritized types: processed first, no upstream occupancy
                var prioPrefabs = new HashSet<string>(
                    catalogue.Where(c => c.Prioritized).Select(c => c.PrefabName));
                var perPrefab = new List<(string p, int comp, int real, int hit)>();
                foreach (var kv in byPrefab)
                {
                    if (!oracleByPrefab.TryGetValue(kv.Key, out var reals)) continue;
                    prefabsCompared++;
                    if (kv.Value.Count == reals.Count) countMatch++;
                    int hit = 0;
                    foreach (var (cx, cz) in kv.Value)
                    {
                        total++;
                        double best = double.MaxValue;
                        foreach (var (rx, rz) in reals)
                        {
                            double d = (cx - rx) * (cx - rx) + (cz - rz) * (cz - rz);
                            if (d < best) best = d;
                        }
                        best = Math.Sqrt(best);
                        sumNearest += best;
                        if (best < 50.0) within50++;
                        if (best < 500.0) within500++;
                        bool isHit = best < 0.5;
                        if (isHit) { exact++; hit++; }
                        if (prioPrefabs.Contains(kv.Key)) { prioTotal++; if (isHit) prioExact++; }
                    }
                    perPrefab.Add((kv.Key, kv.Value.Count, reals.Count, hit));
                }

                Console.WriteLine($"  position-exact (≤0.5m):  {exact}/{total}  " +
                                  $"({(total > 0 ? 100.0 * exact / total : 0):F1}%)  [bit-exact RNG+terrain]");
                Console.WriteLine($"  near     (≤50m):         {within50}/{total}  " +
                                  $"({(total > 0 ? 100.0 * within50 / total : 0):F1}%)  [same locale]");
                Console.WriteLine($"  region-scale (≤500m):    {within500}/{total}  " +
                                  $"({(total > 0 ? 100.0 * within500 / total : 0):F1}%)  [SAME REGION — the substrate metric]");
                Console.WriteLine($"  ↳ PRIORITIZED ≤0.5m (clean — no upstream occupancy): " +
                                  $"{prioExact}/{prioTotal} ({(prioTotal > 0 ? 100.0 * prioExact / prioTotal : 0):F1}%)");
                Console.WriteLine($"  count-exact prefabs: {countMatch}/{prefabsCompared}");
                Console.WriteLine($"  mean nearest-real distance: {(total > 0 ? sumNearest / total : 0):F2} m");
                Console.WriteLine("  per-prefab (computed/real/exact-hits), BEST 8 + worst 8:");
                foreach (var r in perPrefab.OrderByDescending(r => (double)r.hit / Math.Max(1, r.comp)).Take(8))
                    Console.WriteLine($"    ✓ {r.p,-28} comp={r.comp,4} real={r.real,4} exact={r.hit,4}");
                foreach (var r in perPrefab.OrderByDescending(r => r.comp - r.hit).Take(8))
                    Console.WriteLine($"    ✗ {r.p,-28} comp={r.comp,4} real={r.real,4} exact={r.hit,4}");
            }
            return 0;
        }
    }
}
