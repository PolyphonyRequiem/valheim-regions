using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    static class Program
    {
        /// <summary>Simple r/g/b tuple replacing Unity's Color32.</summary>
        struct Color32
        {
            public byte r, g, b;
            public Color32(byte r, byte g, byte b) { this.r = r; this.g = g; this.b = b; }
        }

        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 1;
            }

            string command = args[0].ToLowerInvariant();
            string seed = "HHcLC5acQt";
            string? output = null;
            bool includeInlandWater = false;
            bool compareInland = false;
            string? vegetationCatalogue = null;
            string? locationCatalogue = null;
            string? oracle = null;
            string? onlyStrategy = null;
            string? probePrefab = null;
            string? dumpPath = null;
            bool emitBoundaries = false;

            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "-seed" || args[i] == "--seed") && i + 1 < args.Length)
                    seed = args[++i];
                else if ((args[i] == "-output" || args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
                    output = args[++i];
                else if (args[i] == "--inland-water")
                    includeInlandWater = true;
                else if (args[i] == "--compare-inland")
                    compareInland = true;
                else if (args[i] == "--vegetation" && i + 1 < args.Length)
                    vegetationCatalogue = args[++i];
                else if (args[i] == "--catalogue" && i + 1 < args.Length)
                    locationCatalogue = args[++i];
                else if (args[i] == "--oracle" && i + 1 < args.Length)
                    oracle = args[++i];
                else if (args[i] == "--strategy" && i + 1 < args.Length)
                    onlyStrategy = args[++i];
                else if (args[i] == "--prefab" && i + 1 < args.Length)
                    probePrefab = args[++i];
                else if (args[i] == "--dump" && i + 1 < args.Length)
                    dumpPath = args[++i];
                else if (args[i] == "--boundaries")
                    emitBoundaries = true;
            }

            switch (command)
            {
                case "biome":
                    return ExportBiomeMap(seed, output);
                case "regions":
                    return ExportRegions(seed, output, includeInlandWater, compareInland);
                case "gazetteer":
                    return Gazetteer.Export(seed, output ?? Directory.GetCurrentDirectory(), includeInlandWater, vegetationCatalogue, emitBoundaries, locationCatalogue);
                case "seedlab":
                    return SeedLab.Run(seed, output ?? Directory.GetCurrentDirectory());
                case "composite":
                    return CompositeDump.Run(seed, output ?? Path.Combine(Directory.GetCurrentDirectory(), $"{seed}_composite.json"));
                case "glowprobe":
                    return GlowProbe.Run(seed);
                case "apronviz":
                    return ApronViz.Run(seed, output ?? "/tmp");
                case "ringspike":
                    return RingSpike.Run(seed, output ?? "/tmp");
                case "islandloss":
                    return IslandLoss.Run(seed);
                case "bridgeprobe":
                    return BridgeProbe.Run(seed);
                case "fillmaskviz":
                    return FillMaskViz.Run(seed, output ?? "/tmp");
                case "basemap":
                    return CompositeDump.Basemap(seed, output ?? Path.Combine(Directory.GetCurrentDirectory(), $"{seed}_basemap.bin"), 8);
                case "locations":
                    if (locationCatalogue == null)
                    {
                        Console.Error.WriteLine("locations: --catalogue <locations.json> is required");
                        return 1;
                    }
                    return LocationValidation.Run(seed, locationCatalogue, oracle, onlyStrategy, dumpPath);
                case "locregion":
                    if (locationCatalogue == null || oracle == null)
                    {
                        Console.Error.WriteLine("locregion: --catalogue <locations.json> and --oracle <raw.json> required");
                        return 1;
                    }
                    return LocationRegionAccuracy.Run(seed, locationCatalogue, oracle);
                case "probe":
                    if (locationCatalogue == null || oracle == null || onlyStrategy == null)
                    {
                        Console.Error.WriteLine("probe: --catalogue, --oracle, --strategy, and --prefab required");
                        return 1;
                    }
                    return LocationProbe.Run(seed, probePrefab ?? "InfestedTree01", locationCatalogue, oracle, onlyStrategy, 15);
                case "all":
                    int r1 = ExportBiomeMap(seed, output);
                    int r2 = ExportRegions(seed, output, includeInlandWater, compareInland);
                    return (r1 != 0) ? r1 : r2;
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    PrintUsage();
                    return 1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("WorldZones CLI — Valheim worldgen image exporter");
            Console.WriteLine();
            Console.WriteLine("Usage: WorldZones.Cli <command> [options]");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  biome      Export biome map PNG");
            Console.WriteLine("  regions    Export region images (land, shelf, archipelago, seeds, proto-regions)");
            Console.WriteLine("  all        Export everything");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --seed <seed>    World seed string (default: HHcLC5acQt)");
            Console.WriteLine("  --output <dir>   Output directory (default: current directory)");
            Console.WriteLine("  --inland-water   Enable inland-water attribution for proto region export");
            Console.WriteLine("  --compare-inland Export both baseline and inland-water candidate proto-region PNGs");
            Console.WriteLine("  --vegetation <catalogue.json>  (gazetteer) Emit a modeled ore/vegetation sidecar from an extracted catalogue");
            Console.WriteLine("  --boundaries     (gazetteer) Also emit {seed}_boundaries.json — renderable seam/ring/contour geometry");
        }

        // ────────────────────────────────────────────────────────────
        //  Biome map export
        // ────────────────────────────────────────────────────────────

        static readonly Color32 ColorOcean       = new Color32(0, 0, 153);
        static readonly Color32 ColorShallows    = new Color32(102, 102, 255);
        static readonly Color32 ColorMeadows     = new Color32(145, 167, 91);
        static readonly Color32 ColorMountain    = new Color32(255, 255, 255);
        static readonly Color32 ColorBlackForest = new Color32(52, 94, 59);
        static readonly Color32 ColorPlains      = new Color32(199, 199, 49);
        static readonly Color32 ColorSwamp       = new Color32(163, 113, 87);
        static readonly Color32 ColorMistlands   = new Color32(82, 82, 82);
        static readonly Color32 ColorAshLands    = new Color32(255, 0, 0);
        static readonly Color32 ColorDeepNorth   = new Color32(200, 200, 255);
        static readonly Color32 ColorEdge        = new Color32(0, 0, 0);

        const int Range = 10050;
        const int Step = 5;
        const float WorldRadius = 10500f;
        const float WorldRadiusSq = WorldRadius * WorldRadius;
        const float WaterLevel = 30f;

        static int ExportBiomeMap(string seed, string? outputDir)
        {
            string dir = outputDir ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string outputPath = Path.Combine(dir, $"{seed}_biome_map.png");

            int size = (Range * 2 / Step) + 1; // 4021

            Console.WriteLine("=== Biome Map Export ===");
            Console.WriteLine($"Seed: {seed}");
            Console.WriteLine($"Range: +/-{Range}, Step: {Step}");
            Console.WriteLine($"Image size: {size}x{size}");
            Console.WriteLine($"Output: {outputPath}");

            var swInit = Stopwatch.StartNew();
            var wg = new WorldGenerator(seed);
            swInit.Stop();
            Console.WriteLine($"WorldGenerator init: {swInit.ElapsedMilliseconds} ms");

            var swRender = Stopwatch.StartNew();
            byte[] rgbData = new byte[size * size * 3];

            for (int py = 0; py < size; py++)
            {
                // World Z: top of image = +range (north), bottom = -range (south)
                float wz = Range - py * Step;

                for (int px = 0; px < size; px++)
                {
                    float wx = -Range + px * Step;

                    Color32 c;
                    float distSq = wx * wx + wz * wz;

                    if (distSq > WorldRadiusSq)
                    {
                        c = ColorEdge;
                    }
                    else
                    {
                        var biome = wg.GetBiome(wx, wz);
                        if (biome == BiomeType.Ocean)
                            c = ColorOcean;
                        else if (wg.GetBiomeHeight(biome, wx, wz) < WaterLevel)
                            c = ColorShallows;
                        else
                            c = GetBiomeColor(biome);
                    }

                    int offset = (py * size + px) * 3;
                    rgbData[offset]     = c.r;
                    rgbData[offset + 1] = c.g;
                    rgbData[offset + 2] = c.b;
                }

                if (py % 1000 == 0)
                    Console.WriteLine($"  Rendering: {py * 100 / size}%...");
            }

            swRender.Stop();
            Console.WriteLine($"Render: {swRender.ElapsedMilliseconds} ms");

            var swSave = Stopwatch.StartNew();
            PngWriter.Write(outputPath, size, size, rgbData);
            swSave.Stop();
            var fileInfo = new FileInfo(outputPath);
            Console.WriteLine($"PNG save: {swSave.ElapsedMilliseconds} ms ({fileInfo.Length / 1024 / 1024} MB)");

            Console.WriteLine("=== Summary ===");
            Console.WriteLine($"WorldGen init: {swInit.ElapsedMilliseconds} ms");
            Console.WriteLine($"Render:        {swRender.ElapsedMilliseconds} ms");
            Console.WriteLine($"PNG save:      {swSave.ElapsedMilliseconds} ms");
            long total = swInit.ElapsedMilliseconds + swRender.ElapsedMilliseconds + swSave.ElapsedMilliseconds;
            Console.WriteLine($"Total:         {total} ms");
            Console.WriteLine($"Output:        {outputPath}");
            return 0;
        }

        static Color32 GetBiomeColor(BiomeType biome)
        {
            switch (biome)
            {
                case BiomeType.Meadows:     return ColorMeadows;
                case BiomeType.Swamp:       return ColorSwamp;
                case BiomeType.Mountain:    return ColorMountain;
                case BiomeType.BlackForest: return ColorBlackForest;
                case BiomeType.Plains:      return ColorPlains;
                case BiomeType.Ocean:       return ColorOcean;
                case BiomeType.Mistlands:   return ColorMistlands;
                case BiomeType.AshLands:    return ColorAshLands;
                case BiomeType.DeepNorth:   return ColorDeepNorth;
                default:                    return new Color32(255, 0, 255);
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Region images export
        // ────────────────────────────────────────────────────────────

        static readonly Color32 ColorDeep          = new Color32(20, 20, 40);
        static readonly Color32 ColorShallow       = new Color32(60, 60, 100);
        static readonly Color32 ColorLandInShelf   = new Color32(100, 100, 100);
        static readonly Color32 ColorNeutralGray   = new Color32(120, 120, 120);

        static int ExportRegions(string seed, string? outputDir, bool includeInlandWater = false, bool compareInland = false)
        {
            string dir = outputDir ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Console.WriteLine("=== Region Images Export ===");
            Console.WriteLine($"Seed: {seed}");

            // ── 1. Create WorldGenerator ──────────────────────────
            var swInit = Stopwatch.StartNew();
            var worldGen = new WorldGenerator(seed);
            swInit.Stop();
            Console.WriteLine($"WorldGenerator init: {swInit.ElapsedMilliseconds} ms");

            // ── 2. Classify zones ─────────────────────────────────
            var swClassify = Stopwatch.StartNew();
            var grid = new ZoneGrid();
            var provider = new StandaloneWorldDataProvider(seed, worldGen);
            ZoneClassifier.Classify(grid, provider);
            swClassify.Stop();
            Console.WriteLine($"Zone classification: {swClassify.ElapsedMilliseconds} ms");

            // ── 3. Count depth classes ────────────────────────────
            int landCount = 0, shallowCount = 0, deepCount = 0;
            foreach (var coord in grid.AllCoords())
            {
                switch (grid[coord])
                {
                    case DepthClass.Land:    landCount++;    break;
                    case DepthClass.Shallow: shallowCount++; break;
                    case DepthClass.Deep:    deepCount++;    break;
                }
            }
            int totalZones = grid.Size * grid.Size;
            Console.WriteLine($"Grid: {grid.Size}x{grid.Size} = {totalZones} zones");
            Console.WriteLine($"  Land:    {landCount} ({100.0 * landCount / totalZones:F1}%)");
            Console.WriteLine($"  Shallow: {shallowCount} ({100.0 * shallowCount / totalZones:F1}%)");
            Console.WriteLine($"  Deep:    {deepCount} ({100.0 * deepCount / totalZones:F1}%)");
            PrintZoneSnapshot(grid, provider, 2, 5);
            PrintZoneSnapshot(grid, provider, 2, 6);
            PrintZoneSnapshot(grid, provider, 3, 6);
            PrintZoneSnapshot(grid, provider, -2, 4);

            // ── 4. Label land components ──────────────────────────
            var swLabel = Stopwatch.StartNew();
            var components = ComponentLabeler.LabelLand(grid, out int[,] labelGrid);
            swLabel.Stop();
            Console.WriteLine($"Component labeling: {swLabel.ElapsedMilliseconds} ms");
            Console.WriteLine($"Land components: {components.Count}");
            for (int i = 0; i < Math.Min(components.Count, 10); i++)
                Console.WriteLine($"  #{i}: {components[i].Zones.Count} zones (id={components[i].Id})");
            if (components.Count > 10)
                Console.WriteLine($"  ... and {components.Count - 10} more");

            int size = grid.Size;
            int markerX = 0 - grid.MinIndex;
            int markerY = size - 1 - (0 - grid.MinIndex);

            // ── 5. Render & save land_components.png ──────────────
            Console.WriteLine("Rendering land_components...");
            var swRender = Stopwatch.StartNew();
            byte[] landRgb = new byte[size * size * 3];
            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int zx = gx + grid.MinIndex;
                    int zy = gy + grid.MinIndex;
                    var depth = grid[zx, zy];

                    Color32 c;
                    if (depth == DepthClass.Land)
                        c = ComponentColor(labelGrid[gy, gx]);
                    else if (depth == DepthClass.Shallow)
                        c = ColorShallow;
                    else
                        c = ColorDeep;

                    int py = size - 1 - gy;
                    int offset = (py * size + gx) * 3;
                    landRgb[offset]     = c.r;
                    landRgb[offset + 1] = c.g;
                    landRgb[offset + 2] = c.b;
                }
            }
            swRender.Stop();

            DrawZoneMarker(landRgb, size, size, grid, 0, 0, new Color32(255, 255, 255));
            DrawZonePixel(landRgb, size, size, grid, 2, 5, new Color32(160, 160, 160));
            DrawZonePixel(landRgb, size, size, grid, 3, 6, new Color32(160, 160, 160));

            string landPath = Path.Combine(dir, $"{seed}_land_components.png");
            var swSave = Stopwatch.StartNew();
            PngWriter.Write(landPath, size, size, landRgb);
            swSave.Stop();
            Console.WriteLine($"  land_components: {swRender.ElapsedMilliseconds} ms render, {swSave.ElapsedMilliseconds} ms save ({new FileInfo(landPath).Length / 1024} KB)");

            // ── 6. Label shelf components ─────────────────────────
            var shelfOptions = new ShelfLabelingOptions();
            var swShelfLabel = Stopwatch.StartNew();
            var shelfComponents = ComponentLabeler.LabelShelf(grid, labelGrid, out int[,] shelfLabelGrid, shelfOptions);
            swShelfLabel.Stop();
            Console.WriteLine($"Shelf labeling (K={shelfOptions.MaxShallowDistanceFromLandZones}): {swShelfLabel.ElapsedMilliseconds} ms");
            Console.WriteLine($"Shelf components: {shelfComponents.Count}");
            for (int i = 0; i < Math.Min(shelfComponents.Count, 10); i++)
            {
                var sc = shelfComponents[i];
                Console.WriteLine($"  #{i}: {sc.Zones.Count} zones, {sc.ContainedLandComponentIds.Count} land components (id={sc.Id})");
            }
            if (shelfComponents.Count > 10)
                Console.WriteLine($"  ... and {shelfComponents.Count - 10} more");

            // ── 7. Render & save shelf_components.png ─────────────
            Console.WriteLine("Rendering shelf_components...");
            var swShelfRender = Stopwatch.StartNew();
            byte[] shelfRgb = new byte[size * size * 3];
            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int zx = gx + grid.MinIndex;
                    int zy = gy + grid.MinIndex;
                    var depth = grid[zx, zy];

                    Color32 c;
                    if (depth == DepthClass.Land || depth == DepthClass.Shallow)
                        c = ComponentColor(shelfLabelGrid[gy, gx]);
                    else
                        c = ColorDeep;

                    int py = size - 1 - gy;
                    int offset = (py * size + gx) * 3;
                    shelfRgb[offset]     = c.r;
                    shelfRgb[offset + 1] = c.g;
                    shelfRgb[offset + 2] = c.b;
                }
            }
            swShelfRender.Stop();

            DrawZoneMarker(shelfRgb, size, size, grid, 0, 0, new Color32(255, 255, 255));
            DrawZonePixel(shelfRgb, size, size, grid, 2, 5, new Color32(160, 160, 160));
            DrawZonePixel(shelfRgb, size, size, grid, 3, 6, new Color32(160, 160, 160));

            string shelfPath = Path.Combine(dir, $"{seed}_shelf_components.png");
            var swShelfSave = Stopwatch.StartNew();
            PngWriter.Write(shelfPath, size, size, shelfRgb);
            swShelfSave.Stop();
            Console.WriteLine($"  shelf_components: {swShelfRender.ElapsedMilliseconds} ms render, {swShelfSave.ElapsedMilliseconds} ms save ({new FileInfo(shelfPath).Length / 1024} KB)");

            // ── 8. Detect archipelago candidates ──────────────────
            var swArchDetect = Stopwatch.StartNew();
            var archCandidates = ArchipelagoDetector.Detect(shelfComponents, components);
            swArchDetect.Stop();
            Console.WriteLine($"Archipelago detection: {swArchDetect.ElapsedMilliseconds} ms");
            Console.WriteLine($"Archipelago candidates: {archCandidates.Count}");
            for (int i = 0; i < Math.Min(archCandidates.Count, 10); i++)
            {
                var ac = archCandidates[i];
                Console.WriteLine($"  #{i}: shelf={ac.ShelfComponentId}, {ac.LandComponentIds.Count} islands, {ac.TotalLandZoneCount} land zones, dominant={ac.DominantLandShare:P1}");
            }
            if (archCandidates.Count > 10)
                Console.WriteLine($"  ... and {archCandidates.Count - 10} more");

            // ── 9. Render & save archipelago_candidates.png ───────
            Console.WriteLine("Rendering archipelago_candidates...");
            var swArchRender = Stopwatch.StartNew();
            byte[] archRgb = new byte[size * size * 3];

            var landToArch = new Dictionary<int, ArchipelagoCandidate>();
            foreach (var ac in archCandidates)
                foreach (var landId in ac.LandComponentIds)
                    landToArch[landId] = ac;

            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int zx = gx + grid.MinIndex;
                    int zy = gy + grid.MinIndex;
                    var depth = grid[zx, zy];

                    Color32 c;
                    if (depth == DepthClass.Land)
                    {
                        int landLabel = labelGrid[gy, gx];
                        if (landToArch.TryGetValue(landLabel, out var ac))
                            c = ComponentColor(ac.ShelfComponentId);
                        else
                            c = ColorDeep;
                    }
                    else
                    {
                        c = ColorDeep;
                    }

                    int py = size - 1 - gy;
                    int offset = (py * size + gx) * 3;
                    archRgb[offset]     = c.r;
                    archRgb[offset + 1] = c.g;
                    archRgb[offset + 2] = c.b;
                }
            }
            swArchRender.Stop();

            DrawZoneMarker(archRgb, size, size, grid, 0, 0, new Color32(255, 255, 255));
            DrawZonePixel(archRgb, size, size, grid, 2, 5, new Color32(160, 160, 160));
            DrawZonePixel(archRgb, size, size, grid, 3, 6, new Color32(160, 160, 160));

            string archPath = Path.Combine(dir, $"{seed}_archipelago_candidates.png");
            var swArchSave = Stopwatch.StartNew();
            PngWriter.Write(archPath, size, size, archRgb);
            swArchSave.Stop();
            Console.WriteLine($"  archipelago_candidates: {swArchRender.ElapsedMilliseconds} ms render, {swArchSave.ElapsedMilliseconds} ms save ({new FileInfo(archPath).Length / 1024} KB)");

            // ── 10. Generate proto-regions (baseline/candidate) ───
            int targetZonesPerRegion = 200;
            int protoSeedRng = seed.GetStableHashCode();

            bool exportBaseline = !includeInlandWater || compareInland;
            bool exportCandidate = includeInlandWater || compareInland;

            Stopwatch swProtoBaseline = Stopwatch.StartNew();
            swProtoBaseline.Stop();
            Stopwatch swProtoCandidate = Stopwatch.StartNew();
            swProtoCandidate.Stop();

            ProtoRegionResult? baselineResult = null;
            ProtoRegionResult? candidateResult = null;
            int[,]? baselineRegionIdGrid = null;
            int[,]? candidateRegionIdGrid = null;
            List<WorldZones.Regions.Vector2i>? baselineSeeds = null;
            List<WorldZones.Regions.Vector2i>? candidateSeeds = null;

            if (exportBaseline)
            {
                swProtoBaseline = Stopwatch.StartNew();
                baselineResult = ProtoRegionGenerator.GenerateLand(
                    grid,
                    components,
                    targetZonesPerRegion,
                    protoSeedRng,
                    out baselineRegionIdGrid,
                    out baselineSeeds);
                swProtoBaseline.Stop();
            }

            if (exportCandidate)
            {
                swProtoCandidate = Stopwatch.StartNew();
                candidateResult = ProtoRegionGenerator.GenerateLand(
                    grid,
                    components,
                    targetZonesPerRegion,
                    protoSeedRng,
                    out candidateRegionIdGrid,
                    out candidateSeeds,
                    inlandWaterOptions: new InlandWaterAttributionOptions { Enabled = true });
                swProtoCandidate.Stop();
            }

            var primaryResult = candidateResult ?? baselineResult;
            var primarySeeds = candidateSeeds ?? baselineSeeds;

            if (primaryResult == null || primarySeeds == null)
            {
                Console.Error.WriteLine("Proto-region generation produced no result.");
                return 1;
            }

            Console.WriteLine("Proto-region generation:");
            if (baselineResult != null)
            {
                Console.WriteLine($"  baseline: {swProtoBaseline.ElapsedMilliseconds} ms, regions={baselineResult.RegionCount}, attributedInland={baselineResult.AttributedWaterZoneCount}");
            }

            if (candidateResult != null)
            {
                Console.WriteLine($"  candidate: {swProtoCandidate.ElapsedMilliseconds} ms, regions={candidateResult.RegionCount}, attributedInland={candidateResult.AttributedWaterZoneCount}");
            }

            Console.WriteLine($"  Seeds: {primarySeeds.Count}, Target: {targetZonesPerRegion} zones/region");
            Console.WriteLine($"  Seeded components: {primaryResult.SeededComponentCount}");
            Console.WriteLine($"  Regions (after merge): {primaryResult.RegionCount}");
            Console.WriteLine($"  Merged away: {primaryResult.MergedRegionCount}");
            Console.WriteLine($"  Minor islets: {primaryResult.MinorIsletCount} ({primaryResult.MinorIsletTotalArea} zones)");
            Console.WriteLine($"  Land zones: {primaryResult.LandZoneCount}");
            Console.WriteLine($"  Unassigned (minor islet zones): {primaryResult.UnassignedLandCount}");
            Console.WriteLine($"  Area min/avg/max: {primaryResult.MinAreaZones}/{primaryResult.AvgAreaZones:F1}/{primaryResult.MaxAreaZones}");
            for (int i = 0; i < Math.Min(primaryResult.Regions.Count, 10); i++)
            {
                var pr = primaryResult.Regions[i];
                Console.WriteLine($"  #{i}: id={pr.Id}, seed=({pr.Seed.x},{pr.Seed.y}), area={pr.AreaZones}");
            }
            if (primaryResult.Regions.Count > 10)
                Console.WriteLine($"  ... and {primaryResult.Regions.Count - 10} more");

            // ── 11. Render & save proto_seeds.png ─────────────────
            Console.WriteLine("Rendering proto_seeds...");
            var swSeedRender = Stopwatch.StartNew();
            byte[] seedRgb = new byte[size * size * 3];
            var seedSet = new HashSet<(int, int)>();
            foreach (var ps in primarySeeds)
                seedSet.Add((ps.x, ps.y));

            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int zx = gx + grid.MinIndex;
                    int zy = gy + grid.MinIndex;
                    var depth = grid[zx, zy];

                    Color32 c;
                    if (seedSet.Contains((zx, zy)))
                        c = new Color32(255, 0, 0);  // red dot for seeds
                    else if (depth == DepthClass.Land)
                        c = new Color32(80, 100, 80); // muted land
                    else if (depth == DepthClass.Shallow)
                        c = ColorShallow;
                    else
                        c = ColorDeep;

                    int py = size - 1 - gy;
                    int offset = (py * size + gx) * 3;
                    seedRgb[offset]     = c.r;
                    seedRgb[offset + 1] = c.g;
                    seedRgb[offset + 2] = c.b;
                }
            }
            swSeedRender.Stop();

            DrawZoneMarker(seedRgb, size, size, grid, 0, 0, new Color32(255, 255, 255));
            DrawZonePixel(seedRgb, size, size, grid, 2, 5, new Color32(160, 160, 160));
            DrawZonePixel(seedRgb, size, size, grid, 3, 6, new Color32(160, 160, 160));

            string seedsPath = Path.Combine(dir, $"{seed}_proto_seeds.png");
            var swSeedSave = Stopwatch.StartNew();
            PngWriter.Write(seedsPath, size, size, seedRgb);
            swSeedSave.Stop();
            Console.WriteLine($"  proto_seeds: {swSeedRender.ElapsedMilliseconds} ms render, {swSeedSave.ElapsedMilliseconds} ms save ({new FileInfo(seedsPath).Length / 1024} KB)");

            // ── 12. Render & save proto_regions*.png ──────────────
            Console.WriteLine("Rendering proto_regions...");
            var protoRegionPaths = new List<string>();
            long protoRegionRenderMs = 0;
            long protoRegionSaveMs = 0;

            if (baselineRegionIdGrid != null)
            {
                var swRegionRender = Stopwatch.StartNew();
                byte[] baselineRegionRgb = CreateProtoRegionImage(grid, baselineRegionIdGrid, size);
                swRegionRender.Stop();

                string baselineRegionsPath = Path.Combine(
                    dir,
                    compareInland ? $"{seed}_proto_regions_baseline.png" : $"{seed}_proto_regions.png");
                var swRegionSave = Stopwatch.StartNew();
                PngWriter.Write(baselineRegionsPath, size, size, baselineRegionRgb);
                swRegionSave.Stop();

                protoRegionRenderMs += swRegionRender.ElapsedMilliseconds;
                protoRegionSaveMs += swRegionSave.ElapsedMilliseconds;
                protoRegionPaths.Add(baselineRegionsPath);
                Console.WriteLine($"  {Path.GetFileName(baselineRegionsPath)}: {swRegionRender.ElapsedMilliseconds} ms render, {swRegionSave.ElapsedMilliseconds} ms save ({new FileInfo(baselineRegionsPath).Length / 1024} KB)");
            }

            if (candidateRegionIdGrid != null)
            {
                var swRegionRender = Stopwatch.StartNew();
                byte[] candidateRegionRgb = CreateProtoRegionImage(grid, candidateRegionIdGrid, size);
                swRegionRender.Stop();

                string candidateRegionsPath = Path.Combine(
                    dir,
                    compareInland ? $"{seed}_proto_regions_candidate.png" : $"{seed}_proto_regions_inland.png");
                var swRegionSave = Stopwatch.StartNew();
                PngWriter.Write(candidateRegionsPath, size, size, candidateRegionRgb);
                swRegionSave.Stop();

                protoRegionRenderMs += swRegionRender.ElapsedMilliseconds;
                protoRegionSaveMs += swRegionSave.ElapsedMilliseconds;
                protoRegionPaths.Add(candidateRegionsPath);
                Console.WriteLine($"  {Path.GetFileName(candidateRegionsPath)}: {swRegionRender.ElapsedMilliseconds} ms render, {swRegionSave.ElapsedMilliseconds} ms save ({new FileInfo(candidateRegionsPath).Length / 1024} KB)");
            }

            // ── Summary ───────────────────────────────────────────
            Console.WriteLine("=== Summary ===");
            Console.WriteLine($"WorldGen init:     {swInit.ElapsedMilliseconds} ms");
            Console.WriteLine($"Classification:    {swClassify.ElapsedMilliseconds} ms");
            Console.WriteLine($"Land labeling:     {swLabel.ElapsedMilliseconds} ms");
            Console.WriteLine($"Shelf labeling:    {swShelfLabel.ElapsedMilliseconds} ms");
            Console.WriteLine($"Archip. detection: {swArchDetect.ElapsedMilliseconds} ms");
                        Console.WriteLine($"Proto-region gen:  {swProtoBaseline.ElapsedMilliseconds + swProtoCandidate.ElapsedMilliseconds} ms");
            long renderMs = swRender.ElapsedMilliseconds + swShelfRender.ElapsedMilliseconds
                          + swArchRender.ElapsedMilliseconds + swSeedRender.ElapsedMilliseconds
                                                    + protoRegionRenderMs;
            long saveMs = swSave.ElapsedMilliseconds + swShelfSave.ElapsedMilliseconds
                        + swArchSave.ElapsedMilliseconds + swSeedSave.ElapsedMilliseconds
                                                + protoRegionSaveMs;
            Console.WriteLine($"Render:            {renderMs} ms");
            Console.WriteLine($"PNG save:          {saveMs} ms");
            long totalMs = swInit.ElapsedMilliseconds + swClassify.ElapsedMilliseconds
                         + swLabel.ElapsedMilliseconds + swShelfLabel.ElapsedMilliseconds
                                                 + swArchDetect.ElapsedMilliseconds + swProtoBaseline.ElapsedMilliseconds + swProtoCandidate.ElapsedMilliseconds
                         + renderMs + saveMs;
            Console.WriteLine($"Total:             {totalMs} ms");
            Console.WriteLine($"Output files:");
            Console.WriteLine($"  {landPath}");
            Console.WriteLine($"  {shelfPath}");
            Console.WriteLine($"  {archPath}");
            Console.WriteLine($"  {seedsPath}");
            foreach (var protoRegionPath in protoRegionPaths)
            {
                Console.WriteLine($"  {protoRegionPath}");
            }
            Console.WriteLine($"Marker: zone (0,0) at pixel ({markerX},{markerY}) (top-left origin)");
            Console.WriteLine("Gray markers: zones (2,5) and (3,6)");
            return 0;
        }

        // ────────────────────────────────────────────────────────────
        //  Shared helpers
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Deterministic color from component ID using golden-ratio hue spread.
        /// </summary>
        static Color32 ComponentColor(int id)
        {
            double hue = (id * 0.618033988749895) % 1.0;
            return HslToRgb(hue, 0.7, 0.55);
        }

        static Color32 HslToRgb(double h, double s, double l)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h * 6.0) % 2.0 - 1.0));
            double m = l - c / 2.0;

            double r, g, b;
            int sector = (int)(h * 6.0) % 6;
            switch (sector)
            {
                case 0:  r = c; g = x; b = 0; break;
                case 1:  r = x; g = c; b = 0; break;
                case 2:  r = 0; g = c; b = x; break;
                case 3:  r = 0; g = x; b = c; break;
                case 4:  r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }

            return new Color32(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255));
        }

        static byte[] CreateProtoRegionImage(ZoneGrid grid, int[,] regionIdGrid, int size)
        {
            byte[] regionRgb = new byte[size * size * 3];

            for (int i = 0; i < regionRgb.Length; i += 3)
            {
                regionRgb[i] = 20;
                regionRgb[i + 1] = 20;
                regionRgb[i + 2] = 30;
            }

            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int rid = regionIdGrid[gy, gx];
                    if (rid < 0)
                    {
                        continue;
                    }

                    var c = ComponentColor(rid);
                    int offset = ((size - 1 - gy) * size + gx) * 3;
                    regionRgb[offset] = c.r;
                    regionRgb[offset + 1] = c.g;
                    regionRgb[offset + 2] = c.b;
                }
            }

            DrawZoneMarker(regionRgb, size, size, grid, 0, 0, new Color32(255, 255, 255));
            DrawZonePixel(regionRgb, size, size, grid, 2, 5, new Color32(160, 160, 160));
            DrawZonePixel(regionRgb, size, size, grid, 3, 6, new Color32(160, 160, 160));

            return regionRgb;
        }

        static void DrawZoneMarker(byte[] rgbData, int width, int height, ZoneGrid grid, int zoneX, int zoneY, Color32 color)
        {
            int gx = zoneX - grid.MinIndex;
            int gy = zoneY - grid.MinIndex;

            if (gx < 0 || gx >= width || gy < 0 || gy >= height)
            {
                return;
            }

            int py = height - 1 - gy;
            DrawCross(rgbData, width, height, gx, py, 3, color);
        }

        static void DrawZonePixel(byte[] rgbData, int width, int height, ZoneGrid grid, int zoneX, int zoneY, Color32 color)
        {
            int gx = zoneX - grid.MinIndex;
            int gy = zoneY - grid.MinIndex;

            if (gx < 0 || gx >= width || gy < 0 || gy >= height)
            {
                return;
            }

            int py = height - 1 - gy;
            SetPixel(rgbData, width, height, gx, py, color);
        }

        static void DrawCross(byte[] rgbData, int width, int height, int x, int y, int armLength, Color32 color)
        {
            for (int d = -armLength; d <= armLength; d++)
            {
                SetPixel(rgbData, width, height, x + d, y, color);
                SetPixel(rgbData, width, height, x, y + d, color);
            }
        }

        static void SetPixel(byte[] rgbData, int width, int height, int x, int y, Color32 color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            int offset = (y * width + x) * 3;
            rgbData[offset] = color.r;
            rgbData[offset + 1] = color.g;
            rgbData[offset + 2] = color.b;
        }

        static void PrintZoneSnapshot(ZoneGrid grid, IWorldDataProvider provider, int zoneX, int zoneY)
        {
            var coord = new WorldZones.Regions.Vector2i(zoneX, zoneY);
            if (!grid.InBounds(coord))
            {
                Console.WriteLine($"Zone snapshot z({zoneX},{zoneY}): out-of-bounds");
                return;
            }

            var center = ZoneGrid.ZoneCenter(coord);
            float waterLevel = provider.WaterLevel;
            float offset = ZoneGrid.ZoneSize * 0.25f;
            float hCenter = provider.GetTerrainHeight(center.worldX, center.worldZ);
            float hNW = provider.GetTerrainHeight(center.worldX - offset, center.worldZ + offset);
            float hNE = provider.GetTerrainHeight(center.worldX + offset, center.worldZ + offset);
            float hSW = provider.GetTerrainHeight(center.worldX - offset, center.worldZ - offset);
            float hSE = provider.GetTerrainHeight(center.worldX + offset, center.worldZ - offset);
            var depth = grid[coord];

            Console.WriteLine($"Zone snapshot z({zoneX},{zoneY}): depth={depth} waterLevel={waterLevel:F2} hCenter={hCenter:F3} hNW={hNW:F3} hNE={hNE:F3} hSW={hSW:F3} hSE={hSE:F3}");
        }
    }
}
