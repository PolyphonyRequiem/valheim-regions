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

        public static int Export(string seed, string outputDir, bool inlandWater, string? vegetationCatalogue = null)
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

            // ── 5. Emit per-zone grid binary (for the visual map renderer) ──
            string gridPath = Path.Combine(dir, $"{seed}_gazetteer_grid.bin");
            WriteGrid(gridPath, seed, worldGen, grid, regionIdGrid, idToKey);
            Console.WriteLine($"GRID: {gridPath} ({new FileInfo(gridPath).Length} bytes)");

            // ── 6. (optional) Emit modeled vegetation/ore sidecar from an extracted catalogue ──
            if (!string.IsNullOrEmpty(vegetationCatalogue))
            {
                string vegPath = Path.Combine(dir, $"{seed}_vegetation.json");
                WriteVegetationSidecar(vegPath, seed, vegetationCatalogue!, worldGen,
                    grid, regionIdGrid, idToKey);
                Console.WriteLine($"VEG:  {vegPath} ({new FileInfo(vegPath).Length} bytes)");
            }

            return 0;
        }

        /// <summary>
        /// Emit the MODELED vegetation/ore sidecar: for every land zone, run VegetationModel.ModelZone
        /// (deterministic, RNG-exact, cheap-filters-only) with the extracted catalogue + the verified
        /// port's real height/biome samplers, and aggregate per regionKey. Keyed by regionKey to JOIN
        /// the gazetteer + location sidecars. EVERY value is source=modeled and an UPPER-BIAS estimate
        /// (headless cannot apply the mesh/physics rejection filters — see the design doc's caveat).
        /// </summary>
        static void WriteVegetationSidecar(string path, string seed, string cataloguePath,
            WorldGenerator worldGen, ZoneGrid grid, int[,] regionIdGrid, Dictionary<int, string> idToKey)
        {
            var catalogue = VegetationCatalogue.Load(cataloguePath);
            int worldSeed = seed.GetStableHashCode();
            Func<float, float, float> height = (wx, wz) => worldGen.GetBiomeHeight(worldGen.GetBiome(wx, wz), wx, wz);
            Func<float, float, BiomeType> biomeAt = (wx, wz) => worldGen.GetBiome(wx, wz);

            // per-region: prefab -> count, plus resource/flora split
            var perRegion = new Dictionary<string, RegionVeg>();
            int size = grid.Size, min = grid.MinIndex;

            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int id = regionIdGrid[gy, gx];
                if (id < 0 || !idToKey.TryGetValue(id, out var key)) continue;

                int zx = gx + min, zy = gy + min;
                var counts = VegetationModel.ModelZone(worldSeed, zx, zy, catalogue, height, biomeAt);
                if (counts.Count == 0) continue;

                if (!perRegion.TryGetValue(key, out var rv)) { rv = new RegionVeg(); perRegion[key] = rv; }
                foreach (var c in counts)
                {
                    rv.ByPrefab.TryGetValue(c.PrefabName, out int prev);
                    rv.ByPrefab[c.PrefabName] = prev + c.EstimatedCount;
                    if (c.IsResource) rv.ResourceTotal += c.EstimatedCount;
                    else rv.FloraTotal += c.EstimatedCount;
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"provenance\": {\n");
            sb.Append($"    \"schemaVersion\": {SchemaVersion},\n");
            sb.Append($"    \"seed\": \"{Esc(seed)}\",\n");
            sb.Append("    \"source\": \"modeled\",\n");
            sb.Append($"    \"catalogue\": \"{Esc(Path.GetFileName(cataloguePath))}\",\n");
            sb.Append("    \"catalogueSource\": \"assetripper-export\",\n");
            sb.Append("    \"overcountBias\": \"mesh/physics rejection filters (vegetation mask, ocean depth, terrain delta, tilt, blocked, clear-area, forest factor) are NOT applied headlessly — counts are an UPPER-BIAS estimate, not exact node counts\",\n");
            sb.Append("    \"note\": \"Modeled ore/flora counts. Join on regionKey to the gazetteer + locations sidecars. Every value is source=modeled. Fully deterministic: same seed+catalogue => identical counts.\"\n");
            sb.Append("  },\n");

            // stable region order
            var keys = new List<string>(perRegion.Keys);
            keys.Sort(StringComparer.Ordinal);
            sb.Append("  \"regions\": {\n");
            for (int k = 0; k < keys.Count; k++)
            {
                var key = keys[k];
                var rv = perRegion[key];
                sb.Append($"    \"{Esc(key)}\": {{ \"resourceTotal\": {rv.ResourceTotal}, \"floraTotal\": {rv.FloraTotal}, \"byPrefab\": {{");
                var pk = new List<string>(rv.ByPrefab.Keys);
                pk.Sort(StringComparer.Ordinal);
                for (int p = 0; p < pk.Count; p++)
                    sb.Append($"{(p > 0 ? ", " : " ")}\"{Esc(pk[p])}\": {rv.ByPrefab[pk[p]]}");
                sb.Append(" } }");
                sb.Append(k < keys.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  }\n}\n");
            File.WriteAllText(path, sb.ToString());

            int totalResource = 0, regionsWithResource = 0;
            foreach (var rv in perRegion.Values)
                if (rv.ResourceTotal > 0) { totalResource += rv.ResourceTotal; regionsWithResource++; }
            Console.WriteLine($"      vegetation: {perRegion.Count} regions, {regionsWithResource} with ore, " +
                              $"{totalResource} modeled ore nodes total (source=modeled, upper-bias)");
        }

        sealed class RegionVeg
        {
            public int ResourceTotal;
            public int FloraTotal;
            public readonly Dictionary<string, int> ByPrefab = new Dictionary<string, int>();
        }

        /// <summary>
        /// Emit the per-zone grid the offline map renderer consumes. Binary, little-endian:
        ///   char[4] "WZGR"; int32 version=1
        ///   int32 minIndex, size, zoneSize
        ///   int32 regionCount; then per region: int32 idLen, utf8 RegionKey  (id == array index used in grid)
        ///   then size*size records row-major (gy-major, gx-minor):
        ///     int32 regionId (-1 = unassigned/non-land); uint16 biome; uint16 pad; float32 height
        /// Region ids in the grid are the transient BFS ids; the header maps id->RegionKey so the
        /// renderer can label by durable name.
        /// </summary>
        static void WriteGrid(string path, string seed, WorldGenerator worldGen, ZoneGrid grid,
            int[,] regionIdGrid, Dictionary<int, string> idToKey)
        {
            int size = grid.Size, min = grid.MinIndex;
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(new char[] { 'W', 'Z', 'G', 'R' });
            bw.Write(1);
            bw.Write(min); bw.Write(size); bw.Write(ZoneSize);

            // region id -> key table
            bw.Write(idToKey.Count);
            foreach (var kv in idToKey)
            {
                bw.Write(kv.Key);
                var keyBytes = System.Text.Encoding.UTF8.GetBytes(kv.Value);
                bw.Write(keyBytes.Length);
                bw.Write(keyBytes);
            }

            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int id = regionIdGrid[gy, gx];
                int zx = gx + min, zy = gy + min;
                float wx = zx * (float)ZoneSize, wz = zy * (float)ZoneSize;
                var biome = worldGen.GetBiome(wx, wz);
                float h = worldGen.GetBiomeHeight(biome, wx, wz);
                bw.Write(id);
                bw.Write((ushort)(int)biome);
                bw.Write((ushort)0);
                bw.Write(h);
            }
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
