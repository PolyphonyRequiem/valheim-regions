using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// Region GAZETTEER exporter — the headless region dataset for Daniel + modders.
    ///
    /// As of the runtime-façade retrofit this is a thin SERIALIZER over
    /// <see cref="WorldZonesRuntime.Build"/>: it no longer hand-rolls the classify → label →
    /// GenerateLand → aggregate pipeline (that lived here in triplicate with the mod + the regions
    /// export). It builds a <see cref="RegionWorld"/> and writes the rich <see cref="RegionInfo"/>
    /// records out. Naming is pinned to <see cref="LegacyRegionNamer"/> so the dataset's names are
    /// byte-identical to the pre-retrofit output; adopting the richer multi-schema namer is a
    /// separate, visible switch (swap the namer in <see cref="RegionBuildOptions"/>).
    ///
    /// Everything here is computed from the VERIFIED port (GetBiome/GetBiomeHeight) — source:computed.
    ///
    /// What is deliberately NOT here (separate follow-up): ore/vegetation node counts and location/POI
    /// counts. A region record is the geographic skeleton; landmarks + resources are tagged sidecars
    /// that join on regionKey.
    ///
    /// Emits per seed:
    ///   {seed}_gazetteer.json  — structured, nested (biome composition map, neighbour arrays) + provenance
    ///   {seed}_gazetteer.tsv   — one row per region, flat, for pandas/sqlite/eyeball querying
    ///   {seed}_gazetteer_grid.bin — per-zone binary for the offline map renderer
    /// </summary>
    static class Gazetteer
    {
        const int ZoneSize = 64;
        const int SchemaVersion = 1;

        static readonly Dictionary<BiomeType, string> BiomeName = new Dictionary<BiomeType, string>
        {
            { BiomeType.None, "None" }, { BiomeType.Meadows, "Meadows" }, { BiomeType.Swamp, "Swamp" },
            { BiomeType.Mountain, "Mountain" }, { BiomeType.BlackForest, "BlackForest" },
            { BiomeType.Plains, "Plains" }, { BiomeType.AshLands, "AshLands" },
            { BiomeType.DeepNorth, "DeepNorth" }, { BiomeType.Ocean, "Ocean" }, { BiomeType.Mistlands, "Mistlands" },
        };

        public static int Export(string seed, string outputDir, bool inlandWater, string? vegetationCatalogue = null)
        {
            string dir = outputDir ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Console.WriteLine("=== Region Gazetteer Export ===");
            Console.WriteLine($"Seed: {seed}   InlandWater: {inlandWater}");

            // ── 1. Build the region world via the runtime façade (ONE entry point; the bootstrap that
            //       used to live here in triplicate now lives in WorldZonesRuntime.Build). The seed is
            //       the worldId — names pinned to the legacy catalog to preserve byte-identical output. ──
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            var world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = inlandWater,
                Namer = new LegacyRegionNamer(),
            });

            ProtoRegionResult result = world.ProtoResult;
            IReadOnlyList<RegionInfo> regions = world.Regions; // already RegionKey-ordered, land-only

            Console.WriteLine($"Pipeline: {result.RegionCount} regions, {result.LandZoneCount} land zones, " +
                              $"{result.MinorIsletCount} islets, target 200 zones/region");

            // ── 2. Emit JSON (structured + provenance) ──
            string commit = TryGitCommit();
            string jsonPath = Path.Combine(dir, $"{seed}_gazetteer.json");
            WriteJson(jsonPath, seed, inlandWater, commit, result, regions);
            Console.WriteLine($"JSON: {jsonPath} ({new FileInfo(jsonPath).Length} bytes)");

            // ── 3. Emit TSV (one row per region, flat) ──
            string tsvPath = Path.Combine(dir, $"{seed}_gazetteer.tsv");
            WriteTsv(tsvPath, regions);
            Console.WriteLine($"TSV:  {tsvPath} ({regions.Count} rows)");

            // ── 4. Emit per-zone grid binary (for the visual map renderer) ──
            string gridPath = Path.Combine(dir, $"{seed}_gazetteer_grid.bin");
            var idToKey = result.Regions.ToDictionary(r => r.Id, r => r.RegionKey);
            WriteGrid(gridPath, worldGen, world.Grid, world.RegionIdGrid, idToKey);
            Console.WriteLine($"GRID: {gridPath} ({new FileInfo(gridPath).Length} bytes)");

            // ── 5. (optional) Emit modeled vegetation/ore sidecar from an extracted catalogue ──
            if (!string.IsNullOrEmpty(vegetationCatalogue))
            {
                string vegPath = Path.Combine(dir, $"{seed}_vegetation.json");
                WriteVegetationSidecar(vegPath, seed, vegetationCatalogue!, worldGen,
                    world.Grid, world.RegionIdGrid, idToKey);
                Console.WriteLine($"VEG:  {vegPath} ({new FileInfo(vegPath).Length} bytes)");
            }

            return 0;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        // Serializers — consume RegionInfo (the rich runtime model). Formats are byte-for-byte
        // identical to the pre-retrofit output; verified by SHA diff against a captured baseline.
        // ─────────────────────────────────────────────────────────────────────────────

        static void WriteJson(string path, string seed, bool inlandWater, string commit,
            ProtoRegionResult result, IReadOnlyList<RegionInfo> regions)
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
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                int land = r.SampledLandZones;

                sb.Append("    {\n");
                sb.Append($"      \"regionKey\": \"{Esc(r.RegionKey)}\",\n");
                sb.Append($"      \"name\": \"{Esc(r.Name)}\",\n");
                sb.Append($"      \"transientId\": {r.TransientId},\n");
                sb.Append($"      \"identityCoord\": {{ \"x\": {r.IdentityCoord.x}, \"z\": {r.IdentityCoord.y} }},\n");
                sb.Append($"      \"seedZone\": {{ \"x\": {r.SeedZone.x}, \"z\": {r.SeedZone.y} }},\n");
                sb.Append($"      \"centroidMeters\": {{ \"x\": {r.CentroidX.ToString("F1", ci)}, \"z\": {r.CentroidZ.ToString("F1", ci)} }},\n");
                sb.Append($"      \"boundsZones\": {{ \"minX\": {r.MinZoneX}, \"minZ\": {r.MinZoneZ}, \"maxX\": {r.MaxZoneX}, \"maxZ\": {r.MaxZoneZ} }},\n");
                sb.Append($"      \"areaZones\": {r.AreaZones},\n");
                sb.Append($"      \"landZones\": {r.LandZones},\n");
                sb.Append($"      \"inlandWaterZones\": {r.InlandWaterZones},\n");
                sb.Append($"      \"areaKm2\": {r.AreaKm2.ToString("F2", ci)},\n");
                sb.Append($"      \"isCoastal\": {(r.IsCoastal ? "true" : "false")},\n");
                sb.Append($"      \"dominantBiome\": \"{BiomeName[r.DominantBiome]}\",\n");

                sb.Append("      \"biomeComposition\": {");
                var comp = r.BiomeZoneCounts.OrderByDescending(kv => kv.Value).ToList();
                for (int j = 0; j < comp.Count; j++)
                {
                    double frac = (double)comp[j].Value / land;
                    sb.Append($" \"{BiomeName[comp[j].Key]}\": {frac.ToString("F3", ci)}");
                    sb.Append(j < comp.Count - 1 ? "," : " ");
                }
                sb.Append("},\n");

                sb.Append("      \"elevationMeters\": { ");
                sb.Append($"\"min\": {r.MinElevation.ToString("F1", ci)}, \"mean\": {r.MeanElevation.ToString("F1", ci)}, ");
                sb.Append($"\"max\": {r.MaxElevation.ToString("F1", ci)}, \"relief\": {r.Relief.ToString("F1", ci)} }},\n");
                sb.Append($"      \"highestPeakMeters\": {{ \"x\": {r.HighestPeakX.ToString("F0", ci)}, \"z\": {r.HighestPeakZ.ToString("F0", ci)}, \"height\": {r.MaxElevation.ToString("F1", ci)} }},\n");

                sb.Append("      \"neighborKeys\": [");
                sb.Append(string.Join(", ", r.NeighborKeys.Select(k => $"\"{Esc(k)}\"")));
                sb.Append("]\n");

                sb.Append(i < regions.Count - 1 ? "    },\n" : "    }\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");

            File.WriteAllText(path, sb.ToString());
        }

        static void WriteTsv(string path, IReadOnlyList<RegionInfo> regions)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("regionKey\tname\tcentroidX\tcentroidZ\tareaZones\tlandZones\tinlandWaterZones\tareaKm2\t");
            sb.Append("isCoastal\tdominantBiome\tbiomePctTop\treliefM\tmeanElevM\tpeakM\tneighborCount\tneighborKeys\n");

            // TSV row order matches the JSON: by durable RegionKey (ordinal). `regions` is already
            // RegionKey-ordered from the builder, so iterate as-is.
            foreach (var r in regions)
            {
                int land = r.SampledLandZones;
                double domFrac = (r.BiomeZoneCounts.TryGetValue(r.DominantBiome, out int dc) && land > 0)
                    ? (double)dc / land : 0;

                sb.Append($"{r.RegionKey}\t{r.Name}\t{r.CentroidX.ToString("F0", ci)}\t{r.CentroidZ.ToString("F0", ci)}\t");
                sb.Append($"{r.AreaZones}\t{r.LandZones}\t{r.InlandWaterZones}\t{r.AreaKm2.ToString("F2", ci)}\t");
                sb.Append($"{(r.IsCoastal ? 1 : 0)}\t{BiomeName[r.DominantBiome]}\t{domFrac.ToString("F3", ci)}\t");
                sb.Append($"{r.Relief.ToString("F1", ci)}\t{r.MeanElevation.ToString("F1", ci)}\t{r.MaxElevation.ToString("F1", ci)}\t");
                sb.Append($"{r.NeighborKeys.Count}\t{string.Join(",", r.NeighborKeys)}\n");
            }
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Emit the per-zone grid the offline map renderer consumes. Binary, little-endian:
        ///   char[4] "WZGR"; int32 version=1
        ///   int32 minIndex, size, zoneSize
        ///   int32 regionCount; then per region: int32 id, int32 idLen, utf8 RegionKey
        ///   then size*size records row-major (gy-major, gx-minor):
        ///     int32 regionId (-1 = unassigned/non-land); uint16 biome; uint16 pad; float32 height
        /// </summary>
        static void WriteGrid(string path, WorldGenerator worldGen, ZoneGrid grid,
            int[,] regionIdGrid, Dictionary<int, string> idToKey)
        {
            int size = grid.Size, min = grid.MinIndex;
            using var bw = new BinaryWriter(File.Create(path));
            bw.Write(new char[] { 'W', 'Z', 'G', 'R' });
            bw.Write(1);
            bw.Write(min); bw.Write(size); bw.Write(ZoneSize);

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

        // ─────────────────────────────────────────────────────────────────────────────
        // Vegetation/ore sidecar — unchanged; still needs the raw worldGen + regionIdGrid, both of
        // which the RegionWorld exposes. Source = modeled (NOT computed); kept separate on purpose.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Emit the MODELED vegetation/ore sidecar: for every land zone, run VegetationModel.ModelZone
        /// (deterministic, RNG-exact, cheap-filters-only) with the extracted catalogue + the verified
        /// port's real height/biome samplers, and aggregate per regionKey. Keyed by regionKey to JOIN
        /// the gazetteer + location sidecars. EVERY value is source=modeled and an UPPER-BIAS estimate.
        /// </summary>
        static void WriteVegetationSidecar(string path, string seed, string cataloguePath,
            WorldGenerator worldGen, ZoneGrid grid, int[,] regionIdGrid, Dictionary<int, string> idToKey)
        {
            var catalogue = VegetationCatalogue.Load(cataloguePath);
            int worldSeed = seed.GetStableHashCode();
            Func<float, float, float> height = (wx, wz) => worldGen.GetBiomeHeight(worldGen.GetBiome(wx, wz), wx, wz);
            Func<float, float, BiomeType> biomeAt = (wx, wz) => worldGen.GetBiome(wx, wz);

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
