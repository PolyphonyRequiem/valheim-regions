using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
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

        public static int Export(string seed, string outputDir, bool inlandWater,
            string? vegetationCatalogue = null, bool emitBoundaries = false,
            string? locationCatalogue = null)
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

            // Optional location source: when a catalogue is supplied, run the verified GenerateLocations
            // port from the seed and JOIN every location to its region (RegionKey + PlacementStatus). This
            // is what unlocks the {seed}_locations.json sidecar. Offline = Registered/Candidate only.
            ILocationSource? locationSource = null;
            if (!string.IsNullOrEmpty(locationCatalogue))
            {
                var configs = LocationCatalogue.Load(locationCatalogue);
                locationSource = PortLocationSource.FromSeed(seed, configs);
                Console.WriteLine($"Locations: catalogue '{Path.GetFileName(locationCatalogue)}' ({configs.Count} configs) — joining to regions");
            }

            var world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = inlandWater,
                Namer = new LegacyRegionNamer(),
                // Feature-aware borders ON: the dataset must reflect the SAME tessellation players walk
                // (the overlay plugin also enables this). Borders fall on biome edges / shores / rivers
                // (watershed Dijkstra) instead of geometric midlines. See docs/design/region-borders.md.
                UseFeatureAwareBorders = true,
                LocationSource = locationSource,
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

            // ── 4b. (optional) Emit the renderable BOUNDARY GEOMETRY sidecar ──────────────────────
            //   The grid above ships the per-zone regionId raster; a render consumer (the overlay, a
            //   Trailborne adapter) still has to re-derive the stroke-once seams, the fill rings, and
            //   the smoothed contour arcs by calling RegionWorld.BuildBoundaryGraph() + the refiner
            //   itself. This sidecar SHIPS that geometry so the dataset carries it, not just the grid.
            //   OPTIONAL (--boundaries) so existing consumers that only read the grid are unaffected.
            if (emitBoundaries)
            {
                string boundariesPath = Path.Combine(dir, $"{seed}_boundaries.json");
                WriteBoundaries(boundariesPath, seed, commit, world, sampler);
                Console.WriteLine($"BNDS: {boundariesPath} ({new FileInfo(boundariesPath).Length} bytes)");
            }

            // ── 5. (optional) Emit modeled vegetation/ore sidecar from an extracted catalogue ──
            if (!string.IsNullOrEmpty(vegetationCatalogue))
            {
                string vegPath = Path.Combine(dir, $"{seed}_vegetation.json");
                WriteVegetationSidecar(vegPath, seed, vegetationCatalogue!, worldGen,
                    world.Grid, world.RegionIdGrid, idToKey);
                Console.WriteLine($"VEG:  {vegPath} ({new FileInfo(vegPath).Length} bytes)");
            }

            // ── 6. (optional) Emit the LOCATIONS sidecar — every POI/dungeon/boss/trader joined to its
            //   region by RegionKey. Computed from the verified GenerateLocations port (source:computed);
            //   offline so uniques are Candidate (the seed doesn't pick the winner). Keyed by regionKey to
            //   JOIN the gazetteer. OPTIONAL (--catalogue) so existing consumers are unaffected.
            if (locationSource != null)
            {
                string locPath = Path.Combine(dir, $"{seed}_locations.json");
                WriteLocationsSidecar(locPath, seed, commit, Path.GetFileName(locationCatalogue!), world);
                Console.WriteLine($"LOC:  {locPath} ({new FileInfo(locPath).Length} bytes)");
            }

            return 0;
        }

        /// <summary>
        /// Emit the per-region LOCATIONS dataset: every location the port placed, joined to its region by
        /// RegionKey + carrying its PlacementStatus. Two views: a flat <c>locations</c> array (every site
        /// with its region) and a <c>byRegion</c> map (regionKey → its location list + per-prefab counts),
        /// so a consumer can either iterate sites or join straight onto the gazetteer. Locations outside
        /// any region (ocean / minor islet) carry a null regionKey and are kept (not silently dropped).
        /// </summary>
        static void WriteLocationsSidecar(string path, string seed, string commit, string catalogueName,
            RegionWorld world)
        {
            var ci = CultureInfo.InvariantCulture;
            IReadOnlyList<GazetteerLocation> all = world.AllLocations;

            // group by region for the byRegion view (null regionKey bucketed under "" = unattributed)
            var byRegion = new Dictionary<string, List<GazetteerLocation>>(StringComparer.Ordinal);
            foreach (GazetteerLocation l in all)
            {
                string key = l.RegionKey ?? "";
                if (!byRegion.TryGetValue(key, out var list)) { list = new List<GazetteerLocation>(); byRegion[key] = list; }
                list.Add(l);
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"provenance\": {\n");
            sb.Append($"    \"schemaVersion\": {SchemaVersion},\n");
            sb.Append($"    \"seed\": \"{Esc(seed)}\",\n");
            sb.Append("    \"source\": \"computed\",\n");
            sb.Append($"    \"catalogue\": \"{Esc(catalogueName)}\",\n");
            sb.Append("    \"catalogueSource\": \"assetripper-export\",\n");
            sb.Append($"    \"commit\": \"{Esc(commit)}\",\n");
            sb.Append($"    \"generatedUtc\": \"{DateTime.UtcNow.ToString("o", ci)}\",\n");
            sb.Append($"    \"locationCount\": {all.Count},\n");
            sb.Append("    \"note\": \"Locations/POIs computed from the verified GenerateLocations port and joined to regions by regionKey. OFFLINE: unique sites (traders, etc.) are status=Candidate — the seed does not pick the winner; a live game resolves one. regionKey=null = outside any region (ocean/islet).\"\n");
            sb.Append("  },\n");

            // flat array — every placed site
            sb.Append("  \"locations\": [\n");
            for (int i = 0; i < all.Count; i++)
            {
                GazetteerLocation l = all[i];
                sb.Append("    {");
                sb.Append($"\"prefab\": \"{Esc(l.PrefabName)}\", ");
                sb.Append($"\"x\": {l.X.ToString("0.#", ci)}, \"z\": {l.Z.ToString("0.#", ci)}, ");
                sb.Append($"\"regionKey\": {(l.RegionKey == null ? "null" : $"\"{Esc(l.RegionKey)}\"")}, ");
                sb.Append($"\"status\": \"{l.Status}\"");
                if (l.CandidateGroupKey != null) sb.Append($", \"candidateGroup\": \"{Esc(l.CandidateGroupKey)}\"");
                sb.Append(i < all.Count - 1 ? "},\n" : "}\n");
            }
            sb.Append("  ],\n");

            // byRegion map — regionKey → { count, prefabCounts, sites }
            sb.Append("  \"byRegion\": {\n");
            var keys = new List<string>(byRegion.Keys);
            keys.Sort(StringComparer.Ordinal);
            for (int k = 0; k < keys.Count; k++)
            {
                string rk = keys[k];
                List<GazetteerLocation> list = byRegion[rk];
                var prefabCounts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (GazetteerLocation l in list)
                    prefabCounts[l.PrefabName] = prefabCounts.TryGetValue(l.PrefabName, out int c) ? c + 1 : 1;

                sb.Append($"    \"{Esc(rk)}\": {{\"count\": {list.Count}, \"prefabCounts\": {{");
                var pk = new List<string>(prefabCounts.Keys); pk.Sort(StringComparer.Ordinal);
                for (int p = 0; p < pk.Count; p++)
                    sb.Append($"\"{Esc(pk[p])}\": {prefabCounts[pk[p]]}{(p < pk.Count - 1 ? ", " : "")}");
                sb.Append("}}");
                sb.Append(k < keys.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  }\n");
            sb.Append("}\n");

            File.WriteAllText(path, sb.ToString());
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
        // Boundary-geometry sidecar — the RENDERABLE geometry (stroke-once seams, fill rings, and the
        // smoothed contour arcs) the grid raster does NOT carry. A render consumer would otherwise
        // re-derive all of this with RegionWorld.BuildBoundaryGraph() + RegionBoundaryRefiner; this
        // ships it so the dataset is self-contained. All coordinates are world metres, +X east / +Z
        // north, on the 64·n+32 zone-corner lattice (refined arcs are sub-zone). source = computed.
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Emit <c>{seed}_boundaries.json</c>: the Tier-1 renderable boundary geometry for the world —
        /// deduplicated <see cref="BorderSegment"/> seams (each stroke-once, keyed by the region pair),
        /// closed <see cref="RegionRing"/> fill loops (CCW outer / CW hole), and the smoothed contour
        /// arcs (coast via <c>RefineCoastlinesSmoothed</c>, biome-seam via <c>RefineBiomeSeams</c>).
        /// Points are <c>[x, z]</c> world metres. One object per line for readability without ballooning
        /// the file. The refined arcs match what the overlay would draw in-world (same refiner, same
        /// 25 m coast iso). See docs/design/region-render-seam.md + docs/design/region-borders.md.
        /// </summary>
        static void WriteBoundaries(string path, string seed, string commit,
            RegionWorld world, IWorldSampler sampler)
        {
            var ci = CultureInfo.InvariantCulture;

            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var heightField = new HeightScalarField(sampler);                 // default 25 m coast iso
            var biomeField = new BiomeCategoryField(sampler);
            IReadOnlyList<RefinedBorder> coastArcs = RegionBoundaryRefiner.RefineCoastlinesSmoothed(graph, heightField);
            IReadOnlyList<RefinedBorder> biomeSeamArcs = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField);

            int coastlineSegs = 0, interiorSegs = 0;
            foreach (var s in graph.Segments) { if (s.IsCoastline) coastlineSegs++; else interiorSegs++; }
            int outerRings = 0, holeRings = 0;
            foreach (var r in graph.Rings) { if (r.IsHole) holeRings++; else outerRings++; }

            string P(WzVec2 p) => $"[{p.X.ToString("F1", ci)}, {p.Z.ToString("F1", ci)}]";
            string PR(WzVec2 p) => $"[{p.X.ToString("F2", ci)}, {p.Z.ToString("F2", ci)}]"; // refined: sub-metre

            string Poly(IReadOnlyList<WzVec2> pts, Func<WzVec2, string> fmt)
            {
                var b = new StringBuilder(pts.Count * 14);
                for (int i = 0; i < pts.Count; i++) { if (i > 0) b.Append(", "); b.Append(fmt(pts[i])); }
                return b.ToString();
            }

            var sb = new StringBuilder(1 << 20);
            sb.Append("{\n");

            // provenance — non-optional for a substrate dataset
            sb.Append("  \"provenance\": {\n");
            sb.Append($"    \"schemaVersion\": {SchemaVersion},\n");
            sb.Append($"    \"seed\": \"{Esc(seed)}\",\n");
            sb.Append($"    \"worldId\": \"{Esc(world.WorldId)}\",\n");
            sb.Append($"    \"portCommit\": \"{Esc(commit)}\",\n");
            sb.Append($"    \"zoneSizeMeters\": {ZoneSize},\n");
            sb.Append($"    \"coastIsoMeters\": {HeightScalarField.CoastIso.ToString("F1", ci)},\n");
            sb.Append("    \"featureAwareBorders\": true,\n");
            sb.Append("    \"valueSource\": \"computed\",\n");
            sb.Append("    \"coordinateSpace\": \"world-metres, +X east / +Z north; coarse seams/rings on the 64n+32 zone-corner lattice, refined arcs sub-zone\",\n");
            sb.Append($"    \"generatedUtc\": \"{DateTime.UtcNow.ToString("o", ci)}\",\n");
            sb.Append("    \"note\": \"Renderable boundary geometry for the region world. segments = stroke-once seams keyed by region pair (keyB null = coastline / region-vs-void). rings = closed fill loops (CCW outer, CW hole). coastArcs / biomeSeamArcs = the smoothed sub-zone contour-hug polylines the overlay draws (same refiner + 25 m coast iso). The coarse 64 m seams/rings are the deterministic substrate; the arcs are an ADDITIVE render-detail layer. Join on regionKey to the gazetteer.\"\n");
            sb.Append("  },\n");

            // summary
            sb.Append("  \"summary\": {\n");
            sb.Append($"    \"regionCount\": {world.Regions.Count},\n");
            sb.Append($"    \"segmentCount\": {graph.Segments.Count},\n");
            sb.Append($"    \"coastlineSegmentCount\": {coastlineSegs},\n");
            sb.Append($"    \"interiorSegmentCount\": {interiorSegs},\n");
            sb.Append($"    \"ringCount\": {graph.Rings.Count},\n");
            sb.Append($"    \"outerRingCount\": {outerRings},\n");
            sb.Append($"    \"holeRingCount\": {holeRings},\n");
            sb.Append($"    \"coastArcCount\": {coastArcs.Count},\n");
            sb.Append($"    \"biomeSeamArcCount\": {biomeSeamArcs.Count}\n");
            sb.Append("  },\n");

            // segments — stroke-once seams (the borders-only render primitive)
            sb.Append("  \"segments\": [\n");
            for (int i = 0; i < graph.Segments.Count; i++)
            {
                var s = graph.Segments[i];
                string keyB = s.KeyB == null ? "null" : $"\"{Esc(s.KeyB)}\"";
                sb.Append($"    {{\"a\": {P(s.A)}, \"b\": {P(s.B)}, \"keyA\": \"{Esc(s.KeyA)}\", \"keyB\": {keyB}, \"coast\": {(s.IsCoastline ? "true" : "false")}}}");
                sb.Append(i < graph.Segments.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n");

            // rings — closed fill loops (fill / tint / parchment render)
            sb.Append("  \"rings\": [\n");
            for (int i = 0; i < graph.Rings.Count; i++)
            {
                var r = graph.Rings[i];
                sb.Append($"    {{\"regionKey\": \"{Esc(r.RegionKey)}\", \"isHole\": {(r.IsHole ? "true" : "false")}, ");
                sb.Append($"\"signedAreaM2\": {r.SignedArea.ToString("F0", ci)}, \"vertices\": [{Poly(r.Vertices, P)}]}}");
                sb.Append(i < graph.Rings.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n");

            // coastArcs — smoothed sub-zone coastline contour (KeyA = region, KeyB null)
            sb.Append("  \"coastArcs\": [\n");
            for (int i = 0; i < coastArcs.Count; i++)
            {
                var a = coastArcs[i];
                sb.Append($"    {{\"regionKey\": \"{Esc(a.KeyA)}\", \"hugged\": {(a.Hugged ? "true" : "false")}, \"polyline\": [{Poly(a.Polyline, PR)}]}}");
                sb.Append(i < coastArcs.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n");

            // biomeSeamArcs — smoothed sub-zone region-vs-region biome-transition contour
            sb.Append("  \"biomeSeamArcs\": [\n");
            for (int i = 0; i < biomeSeamArcs.Count; i++)
            {
                var a = biomeSeamArcs[i];
                sb.Append($"    {{\"keyA\": \"{Esc(a.KeyA)}\", \"keyB\": \"{Esc(a.KeyB)}\", \"hugged\": {(a.Hugged ? "true" : "false")}, \"polyline\": [{Poly(a.Polyline, PR)}]}}");
                sb.Append(i < biomeSeamArcs.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n");

            sb.Append("}\n");
            File.WriteAllText(path, sb.ToString());

            Console.WriteLine($"      boundaries: {graph.Segments.Count} seams ({coastlineSegs} coast), " +
                              $"{graph.Rings.Count} rings, {coastArcs.Count} coast arcs, {biomeSeamArcs.Count} biome-seam arcs");
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
