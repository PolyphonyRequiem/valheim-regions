using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// Region GAZETTEER exporter — the headless region dataset for Daniel + modders.
    ///
    /// Runs the SAME production pipeline the mod uses (WorldGenerator -> StandaloneWorldDataProvider
    /// -> ZoneClassifier -> ComponentLabeler -> ProtoRegionGenerator.GenerateLand) and aggregates a
    /// per-region record: durable identity, geometry, terrain character, neighbour graph. Everything
    /// here is computed from the VERIFIED port (GetBiome/GetBiomeHeight) — all values are source:computed.
    ///
    /// What is deliberately NOT here (separate follow-up, needs a PlaceVegetation port): ore / vegetation
    /// node counts, and location/POI counts (those come from the world .db, joined by a separate tool).
    /// A region record is the geographic skeleton; landmarks + resources are tagged sidecars that join on
    /// regionKey. Keeping them out keeps this dataset 100% real and 0% modelled.
    ///
    /// Emits TWO artifacts per seed:
    ///   {seed}_gazetteer.json  — structured, nested (biome composition map, neighbour arrays) + provenance
    ///   {seed}_gazetteer.tsv   — one row per region, flat, for pandas/sqlite/eyeball querying
    /// </summary>
    static class Gazetteer
    {
        const int ZoneSize = 64;
        const int SchemaVersion = 1;

        static readonly (int dx, int dy)[] N4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        static readonly Dictionary<BiomeType, string> BiomeName = new Dictionary<BiomeType, string>
        {
            { BiomeType.None, "None" }, { BiomeType.Meadows, "Meadows" }, { BiomeType.Swamp, "Swamp" },
            { BiomeType.Mountain, "Mountain" }, { BiomeType.BlackForest, "BlackForest" },
            { BiomeType.Plains, "Plains" }, { BiomeType.AshLands, "AshLands" },
            { BiomeType.DeepNorth, "DeepNorth" }, { BiomeType.Ocean, "Ocean" }, { BiomeType.Mistlands, "Mistlands" },
        };

        sealed class Agg
        {
            public ProtoRegion Region;
            public int LandZones;
            public double SumX, SumZ;          // for centroid (world meters)
            public int MinZx = int.MaxValue, MinZy = int.MaxValue, MaxZx = int.MinValue, MaxZy = int.MinValue;
            public float MinH = float.MaxValue, MaxH = float.MinValue;
            public double SumH;
            public float PeakX, PeakZ;
            public bool Coastal;
            public readonly Dictionary<BiomeType, int> Biome = new Dictionary<BiomeType, int>();
            public readonly HashSet<int> NeighborIds = new HashSet<int>();
            public Agg(ProtoRegion r) { this.Region = r; }
        }

        public static int Export(string seed, string outputDir, bool inlandWater)
        {
            string dir = outputDir ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Console.WriteLine("=== Region Gazetteer Export ===");
            Console.WriteLine($"Seed: {seed}   InlandWater: {inlandWater}");

            // ── 1. Run the production pipeline (identical to the mod / CLI regions path) ──
            var worldGen = new WorldGenerator(seed);
            var grid = new ZoneGrid();
            var provider = new StandaloneWorldDataProvider(seed, worldGen);
            ZoneClassifier.Classify(grid, provider);
            var components = ComponentLabeler.LabelLand(grid, out int[,] _labelGrid);

            const int targetZonesPerRegion = 200;
            int protoSeedRng = seed.GetStableHashCode();
            var result = ProtoRegionGenerator.GenerateLand(
                grid, components, targetZonesPerRegion, protoSeedRng,
                out int[,] regionIdGrid, out var seeds,
                inlandWaterOptions: inlandWater ? new InlandWaterAttributionOptions { Enabled = true } : null);

            Console.WriteLine($"Pipeline: {result.RegionCount} regions, {result.LandZoneCount} land zones, " +
                              $"{result.MinorIsletCount} islets, target {targetZonesPerRegion} zones/region");

            // ── 2. Aggregate per region over the assignment grid ──
            int size = grid.Size, min = grid.MinIndex;
            var agg = new Dictionary<int, Agg>();
            foreach (var r in result.Regions) agg[r.Id] = new Agg(r);

            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int id = regionIdGrid[gy, gx];
                if (id < 0 || !agg.TryGetValue(id, out var a)) continue;

                int zx = gx + min, zy = gy + min;
                float wx = zx * (float)ZoneSize, wz = zy * (float)ZoneSize;
                var biome = worldGen.GetBiome(wx, wz);
                float h = worldGen.GetBiomeHeight(biome, wx, wz);

                a.LandZones++;
                a.SumX += wx; a.SumZ += wz; a.SumH += h;
                if (zx < a.MinZx) a.MinZx = zx; if (zy < a.MinZy) a.MinZy = zy;
                if (zx > a.MaxZx) a.MaxZx = zx; if (zy > a.MaxZy) a.MaxZy = zy;
                if (h < a.MinH) a.MinH = h;
                if (h > a.MaxH) { a.MaxH = h; a.PeakX = wx; a.PeakZ = wz; }
                a.Biome.TryGetValue(biome, out int bc); a.Biome[biome] = bc + 1;

                foreach (var (dx, dy) in N4)
                {
                    int ngx = gx + dx, ngy = gy + dy;
                    if (ngx < 0 || ngx >= size || ngy < 0 || ngy >= size) continue;
                    int nid = regionIdGrid[ngy, ngx];
                    if (nid >= 0 && nid != id && agg.ContainsKey(nid)) a.NeighborIds.Add(nid);
                    var ndepth = grid[ngx + min, ngy + min];
                    if (ndepth != DepthClass.Land) a.Coastal = true;
                }
            }

            // id -> regionKey, for resolving neighbour arrays to durable keys
            var idToKey = result.Regions.ToDictionary(r => r.Id, r => r.RegionKey);

            // Stable output order: by RegionKey (durable), not Id (transient)
            var ordered = result.Regions
                .Where(r => agg[r.Id].LandZones > 0)
                .OrderBy(r => r.RegionKey, StringComparer.Ordinal)
                .ToList();

            // ── 3. Emit JSON (structured + provenance) ──
            string commit = TryGitCommit();
            string jsonPath = Path.Combine(dir, $"{seed}_gazetteer.json");
            WriteJson(jsonPath, seed, inlandWater, commit, result, ordered, agg, idToKey);
            Console.WriteLine($"JSON: {jsonPath} ({new FileInfo(jsonPath).Length} bytes)");

            // ── 4. Emit TSV (one row per region, flat) ──
            string tsvPath = Path.Combine(dir, $"{seed}_gazetteer.tsv");
            WriteTsv(seed, tsvPath, ordered, agg, idToKey);
            Console.WriteLine($"TSV:  {tsvPath} ({ordered.Count} rows)");

            return 0;
        }

        static BiomeType Dominant(Agg a)
        {
            var best = BiomeType.None; int bestC = -1;
            foreach (var kv in a.Biome)
                if (kv.Key != BiomeType.Ocean && kv.Value > bestC) { bestC = kv.Value; best = kv.Key; }
            return best;
        }

        static void WriteJson(string path, string seed, bool inlandWater, string commit,
            ProtoRegionResult result, List<ProtoRegion> ordered, Dictionary<int, Agg> agg,
            Dictionary<int, string> idToKey)
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\n");

            // provenance — non-optional for a substrate dataset
            sb.Append("  \"provenance\": {\n");
            sb.Append($"    \"schemaVersion\": {SchemaVersion},\n");
            sb.Append($"    \"seed\": \"{Esc(seed)}\",\n");
            sb.Append($"    \"worldId\": \"{Esc(seed)}\",\n");
            sb.Append($"    \"inlandWaterAttribution\": {(inlandWater ? "true" : "false")},\n");
            sb.Append($"    \"portCommit\": \"{Esc(commit)}\",\n");
            sb.Append($"    \"zoneSizeMeters\": {ZoneSize},\n");
            sb.Append($"    \"targetZonesPerRegion\": 200,\n");
            sb.Append("    \"valueSource\": \"computed\",\n");
            sb.Append($"    \"generatedUtc\": \"{DateTime.UtcNow.ToString("o", ci)}\",\n");
            sb.Append("    \"note\": \"All values computed from the verified worldgen port. Locations/POIs and ore/vegetation are NOT here — they join on regionKey from separate tools.\"\n");
            sb.Append("  },\n");

            // world summary
            sb.Append("  \"world\": {\n");
            sb.Append($"    \"regionCount\": {result.RegionCount},\n");
            sb.Append($"    \"landZoneCount\": {result.LandZoneCount},\n");
            sb.Append($"    \"minorIsletCount\": {result.MinorIsletCount},\n");
            sb.Append($"    \"minorIsletTotalZones\": {result.MinorIsletTotalArea},\n");
            sb.Append($"    \"unassignedLandZones\": {result.UnassignedLandCount}\n");
            sb.Append("  },\n");

            // regions
            sb.Append("  \"regions\": [\n");
            for (int i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                var a = agg[r.Id];
                int land = a.LandZones;
                double cx = a.SumX / land, cz = a.SumZ / land;
                double meanH = a.SumH / land;
                double areaKm2 = (double)r.TotalAreaZones * ZoneSize * ZoneSize / 1_000_000.0;
                var dom = Dominant(a);
                string name = RegionGuidNameService.CreateDeterministicName(seed, r.RegionKey);

                var neighKeys = a.NeighborIds.Select(nid => idToKey[nid])
                    .OrderBy(k => k, StringComparer.Ordinal).ToList();

                sb.Append("    {\n");
                sb.Append($"      \"regionKey\": \"{Esc(r.RegionKey)}\",\n");
                sb.Append($"      \"name\": \"{Esc(name)}\",\n");
                sb.Append($"      \"transientId\": {r.Id},\n");
                sb.Append($"      \"identityCoord\": {{ \"x\": {r.IdentityCoord.x}, \"z\": {r.IdentityCoord.y} }},\n");
                sb.Append($"      \"seedZone\": {{ \"x\": {r.Seed.x}, \"z\": {r.Seed.y} }},\n");
                sb.Append($"      \"centroidMeters\": {{ \"x\": {cx.ToString("F1", ci)}, \"z\": {cz.ToString("F1", ci)} }},\n");
                sb.Append($"      \"boundsZones\": {{ \"minX\": {a.MinZx}, \"minZ\": {a.MinZy}, \"maxX\": {a.MaxZx}, \"maxZ\": {a.MaxZy} }},\n");
                sb.Append($"      \"areaZones\": {r.TotalAreaZones},\n");
                sb.Append($"      \"landZones\": {r.LandAreaZones},\n");
                sb.Append($"      \"inlandWaterZones\": {r.InlandWaterAreaZones},\n");
                sb.Append($"      \"areaKm2\": {areaKm2.ToString("F2", ci)},\n");
                sb.Append($"      \"isCoastal\": {(a.Coastal ? "true" : "false")},\n");
                sb.Append($"      \"dominantBiome\": \"{BiomeName[dom]}\",\n");

                sb.Append("      \"biomeComposition\": {");
                var comp = a.Biome.Where(kv => kv.Key != BiomeType.Ocean)
                    .OrderByDescending(kv => kv.Value).ToList();
                for (int j = 0; j < comp.Count; j++)
                {
                    double frac = (double)comp[j].Value / land;
                    sb.Append($" \"{BiomeName[comp[j].Key]}\": {frac.ToString("F3", ci)}");
                    sb.Append(j < comp.Count - 1 ? "," : " ");
                }
                sb.Append("},\n");

                sb.Append("      \"elevationMeters\": { ");
                sb.Append($"\"min\": {a.MinH.ToString("F1", ci)}, \"mean\": {meanH.ToString("F1", ci)}, ");
                sb.Append($"\"max\": {a.MaxH.ToString("F1", ci)}, \"relief\": {(a.MaxH - a.MinH).ToString("F1", ci)} }},\n");
                sb.Append($"      \"highestPeakMeters\": {{ \"x\": {a.PeakX.ToString("F0", ci)}, \"z\": {a.PeakZ.ToString("F0", ci)}, \"height\": {a.MaxH.ToString("F1", ci)} }},\n");

                sb.Append("      \"neighborKeys\": [");
                sb.Append(string.Join(", ", neighKeys.Select(k => $"\"{Esc(k)}\"")));
                sb.Append("]\n");

                sb.Append(i < ordered.Count - 1 ? "    },\n" : "    }\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");

            File.WriteAllText(path, sb.ToString());
        }

        static void WriteTsv(string seed, string path, List<ProtoRegion> ordered, Dictionary<int, Agg> agg,
            Dictionary<int, string> idToKey)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("regionKey\tname\tcentroidX\tcentroidZ\tareaZones\tlandZones\tinlandWaterZones\tareaKm2\t");
            sb.Append("isCoastal\tdominantBiome\tbiomePctTop\treliefM\tmeanElevM\tpeakM\tneighborCount\tneighborKeys\n");

            foreach (var r in ordered)
            {
                var a = agg[r.Id];
                int land = a.LandZones;
                double cx = a.SumX / land, cz = a.SumZ / land, meanH = a.SumH / land;
                double areaKm2 = (double)r.TotalAreaZones * ZoneSize * ZoneSize / 1_000_000.0;
                var dom = Dominant(a);
                double domFrac = a.Biome.TryGetValue(dom, out int dc) ? (double)dc / land : 0;
                string name = RegionGuidNameService.CreateDeterministicName(seed, r.RegionKey);
                var neighKeys = a.NeighborIds.Select(nid => idToKey[nid])
                    .OrderBy(k => k, StringComparer.Ordinal).ToList();

                sb.Append($"{r.RegionKey}\t{name}\t{cx.ToString("F0", ci)}\t{cz.ToString("F0", ci)}\t");
                sb.Append($"{r.TotalAreaZones}\t{r.LandAreaZones}\t{r.InlandWaterAreaZones}\t{areaKm2.ToString("F2", ci)}\t");
                sb.Append($"{(a.Coastal ? 1 : 0)}\t{BiomeName[dom]}\t{domFrac.ToString("F3", ci)}\t");
                sb.Append($"{(a.MaxH - a.MinH).ToString("F1", ci)}\t{meanH.ToString("F1", ci)}\t{a.MaxH.ToString("F1", ci)}\t");
                sb.Append($"{neighKeys.Count}\t{string.Join(",", neighKeys)}\n");
            }
            File.WriteAllText(path, sb.ToString());
        }

        static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        static string TryGitCommit()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("git", "rev-parse --short HEAD")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return "unknown";
                string outp = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(2000);
                return string.IsNullOrEmpty(outp) ? "unknown" : outp;
            }
            catch { return "unknown"; }
        }
    }
}
