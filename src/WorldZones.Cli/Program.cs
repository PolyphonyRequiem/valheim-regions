using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>Simple r/g/b tuple replacing Unity's Color32.</summary>
    struct Color32
    {
        public byte r, g, b;
        public Color32(byte r, byte g, byte b) { this.r = r; this.g = g; this.b = b; }
    }

    static class Program
    {
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

            for (int i = 1; i < args.Length; i++)
            {
                if ((args[i] == "-seed" || args[i] == "--seed") && i + 1 < args.Length)
                    seed = args[++i];
                else if ((args[i] == "-output" || args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
                    output = args[++i];
            }

            switch (command)
            {
                case "biome":
                    return ExportBiomeMap(seed, output);
                case "regions":
                    return ExportRegions(seed, output);
                case "all":
                    int r1 = ExportBiomeMap(seed, output);
                    int r2 = ExportRegions(seed, output);
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

        static int ExportRegions(string seed, string? outputDir)
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
            ZoneClassifier.Classify(grid, worldGen);
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

            string archPath = Path.Combine(dir, $"{seed}_archipelago_candidates.png");
            var swArchSave = Stopwatch.StartNew();
            PngWriter.Write(archPath, size, size, archRgb);
            swArchSave.Stop();
            Console.WriteLine($"  archipelago_candidates: {swArchRender.ElapsedMilliseconds} ms render, {swArchSave.ElapsedMilliseconds} ms save ({new FileInfo(archPath).Length / 1024} KB)");

            // ── 10. Generate proto-regions ────────────────────────
            int targetZonesPerRegion = 200;
            int protoSeedRng = seed.GetHashCode();
            var swProto = Stopwatch.StartNew();
            var protoResult = ProtoRegionGenerator.GenerateLand(
                grid, components,
                targetZonesPerRegion, protoSeedRng,
                out int[,] regionIdGrid, out List<Vector2i> protoSeeds);
            swProto.Stop();
            Console.WriteLine($"Proto-region generation: {swProto.ElapsedMilliseconds} ms");
            Console.WriteLine($"  Seeds: {protoSeeds.Count}, Target: {targetZonesPerRegion} zones/region");
            Console.WriteLine($"  Seeded components: {protoResult.SeededComponentCount}");
            Console.WriteLine($"  Regions (after merge): {protoResult.RegionCount}");
            Console.WriteLine($"  Merged away: {protoResult.MergedRegionCount}");
            Console.WriteLine($"  Minor islets: {protoResult.MinorIsletCount} ({protoResult.MinorIsletTotalArea} zones)");
            Console.WriteLine($"  Land zones: {protoResult.LandZoneCount}");
            Console.WriteLine($"  Unassigned (minor islet zones): {protoResult.UnassignedLandCount}");
            Console.WriteLine($"  Area min/avg/max: {protoResult.MinAreaZones}/{protoResult.AvgAreaZones:F1}/{protoResult.MaxAreaZones}");
            for (int i = 0; i < Math.Min(protoResult.Regions.Count, 10); i++)
            {
                var pr = protoResult.Regions[i];
                Console.WriteLine($"  #{i}: id={pr.Id}, seed=({pr.Seed.x},{pr.Seed.y}), area={pr.AreaZones}");
            }
            if (protoResult.Regions.Count > 10)
                Console.WriteLine($"  ... and {protoResult.Regions.Count - 10} more");

            // ── 11. Render & save proto_seeds.png ─────────────────
            Console.WriteLine("Rendering proto_seeds...");
            var swSeedRender = Stopwatch.StartNew();
            byte[] seedRgb = new byte[size * size * 3];
            var seedSet = new HashSet<(int, int)>();
            foreach (var ps in protoSeeds)
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

            string seedsPath = Path.Combine(dir, $"{seed}_proto_seeds.png");
            var swSeedSave = Stopwatch.StartNew();
            PngWriter.Write(seedsPath, size, size, seedRgb);
            swSeedSave.Stop();
            Console.WriteLine($"  proto_seeds: {swSeedRender.ElapsedMilliseconds} ms render, {swSeedSave.ElapsedMilliseconds} ms save ({new FileInfo(seedsPath).Length / 1024} KB)");

            // ── 12. Render & save proto_regions.png ───────────────
            Console.WriteLine("Rendering proto_regions...");
            var swRegionRender = Stopwatch.StartNew();
            byte[] regionRgb = new byte[size * size * 3];

            // Background: dark ocean
            for (int i = 0; i < regionRgb.Length; i += 3)
            {
                regionRgb[i]     = 20;
                regionRgb[i + 1] = 20;
                regionRgb[i + 2] = 30;
            }

            // Fill each region zone with its color (north-up: flip Y)
            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int rid = regionIdGrid[gy, gx];
                    if (rid < 0) continue;

                    var c = ComponentColor(rid);
                    int offset = ((size - 1 - gy) * size + gx) * 3;
                    regionRgb[offset]     = c.r;
                    regionRgb[offset + 1] = c.g;
                    regionRgb[offset + 2] = c.b;
                }
            }
            swRegionRender.Stop();

            string regionsPath = Path.Combine(dir, $"{seed}_proto_regions.png");
            var swRegionSave = Stopwatch.StartNew();
            PngWriter.Write(regionsPath, size, size, regionRgb);
            swRegionSave.Stop();
            Console.WriteLine($"  proto_regions: {swRegionRender.ElapsedMilliseconds} ms render, {swRegionSave.ElapsedMilliseconds} ms save ({new FileInfo(regionsPath).Length / 1024} KB)");

            // ── Summary ───────────────────────────────────────────
            Console.WriteLine("=== Summary ===");
            Console.WriteLine($"WorldGen init:     {swInit.ElapsedMilliseconds} ms");
            Console.WriteLine($"Classification:    {swClassify.ElapsedMilliseconds} ms");
            Console.WriteLine($"Land labeling:     {swLabel.ElapsedMilliseconds} ms");
            Console.WriteLine($"Shelf labeling:    {swShelfLabel.ElapsedMilliseconds} ms");
            Console.WriteLine($"Archip. detection: {swArchDetect.ElapsedMilliseconds} ms");
            Console.WriteLine($"Proto-region gen:  {swProto.ElapsedMilliseconds} ms");
            long renderMs = swRender.ElapsedMilliseconds + swShelfRender.ElapsedMilliseconds
                          + swArchRender.ElapsedMilliseconds + swSeedRender.ElapsedMilliseconds
                          + swRegionRender.ElapsedMilliseconds;
            long saveMs = swSave.ElapsedMilliseconds + swShelfSave.ElapsedMilliseconds
                        + swArchSave.ElapsedMilliseconds + swSeedSave.ElapsedMilliseconds
                        + swRegionSave.ElapsedMilliseconds;
            Console.WriteLine($"Render:            {renderMs} ms");
            Console.WriteLine($"PNG save:          {saveMs} ms");
            long totalMs = swInit.ElapsedMilliseconds + swClassify.ElapsedMilliseconds
                         + swLabel.ElapsedMilliseconds + swShelfLabel.ElapsedMilliseconds
                         + swArchDetect.ElapsedMilliseconds + swProto.ElapsedMilliseconds
                         + renderMs + saveMs;
            Console.WriteLine($"Total:             {totalMs} ms");
            Console.WriteLine($"Output files:");
            Console.WriteLine($"  {landPath}");
            Console.WriteLine($"  {shelfPath}");
            Console.WriteLine($"  {archPath}");
            Console.WriteLine($"  {seedsPath}");
            Console.WriteLine($"  {regionsPath}");
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
    }
}
