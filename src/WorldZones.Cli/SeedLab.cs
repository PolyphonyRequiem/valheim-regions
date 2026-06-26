using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// SEEDING-LEVER lab — measures whether biome-aware seeding actually moves region COMPOSITION,
    /// the oddity that routing (the v3 cost field) provably cannot fix (docs/design/region-borders.md).
    /// For a sweep of aggressiveness levels it builds the real region world (via the runtime façade,
    /// the same path the gazetteer uses), measures per-level:
    ///   - region count
    ///   - mean dominant-biome fraction (the headline composition metric — higher = purer regions)
    ///   - blended-region count (dominant &lt; 55% — the multi-biome blobs we're trying to kill)
    ///   - on-feature% of the resulting borders (so we confirm seeding doesn't WRECK routing quality)
    /// and emits a {seed}_seedlab_a{level}_grid.bin per level for the colorblind-safe renderer. This
    /// is the measurement instrument that turns "did the lever work" into numbers + before/after maps,
    /// the technique Daniel requires before greenlighting a substrate change. Headless; no client/walk.
    /// Routing (feature-aware borders) is held ON across the sweep so on-feature% is comparable and we
    /// isolate the SEEDING variable.
    /// </summary>
    static class SeedLab
    {
        const int ZoneSize = 64;

        static readonly Dictionary<BiomeType, string> BiomeName = new Dictionary<BiomeType, string>
        {
            { BiomeType.None, "None" }, { BiomeType.Meadows, "Meadows" }, { BiomeType.Swamp, "Swamp" },
            { BiomeType.Mountain, "Mountain" }, { BiomeType.BlackForest, "BlackForest" },
            { BiomeType.Plains, "Plains" }, { BiomeType.AshLands, "AshLands" },
            { BiomeType.DeepNorth, "DeepNorth" }, { BiomeType.Ocean, "Ocean" }, { BiomeType.Mistlands, "Mistlands" },
        };

        public static int Run(string seed, string outputDir)
        {
            string dir = outputDir ?? Directory.GetCurrentDirectory();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // Aggressiveness sweep. 0 = baseline (legacy area-only seed budget, routing on). The rest
            // turn the seeding lever up. Routing held ON throughout so on-feature% is apples-to-apples.
            double[] levels = { 0.0, 0.5, 1.0, 2.0, 4.0 };

            Console.WriteLine("=== Seeding-Lever Lab ===");
            Console.WriteLine($"Seed: {seed}");
            Console.WriteLine("Routing (feature-aware borders): ON for every level (isolating the SEEDING variable)");
            Console.WriteLine();
            Console.WriteLine($"{"aggr",6} {"regions",8} {"meanDom%",9} {"blended<55%",12} {"onFeat%",8}  worst-blended examples");
            Console.WriteLine(new string('-', 92));

            foreach (double aggr in levels)
            {
                var worldGen = new WorldGenerator(seed);
                var sampler = new PortWorldSampler(worldGen, seed);
                var world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
                {
                    IncludeInlandWater = true,
                    Namer = new LegacyRegionNamer(),
                    UseFeatureAwareBorders = true,                 // routing held ON
                    UseBiomeAwareSeeding = aggr > 0.0,
                    SeedingFieldOptions = aggr > 0.0
                        ? new RegionSeedingFieldOptions { Aggressiveness = aggr }
                        : null,
                });

                IReadOnlyList<RegionInfo> regions = world.Regions;
                int n = regions.Count;

                // Composition metrics over land-bearing regions.
                double sumDom = 0; int blended = 0;
                var domFracs = new List<(RegionInfo r, double frac)>();
                foreach (var r in regions)
                {
                    int land = r.SampledLandZones;
                    double dom = (land > 0 && r.BiomeZoneCounts.TryGetValue(r.DominantBiome, out int dc))
                        ? (double)dc / land : 0.0;
                    sumDom += dom;
                    if (dom < 0.55) blended++;
                    domFracs.Add((r, dom));
                }
                double meanDom = n > 0 ? sumDom / n : 0;

                double onFeat = OnFeaturePercent(sampler, world);

                // 3 worst-blended regions as concrete examples (name + composition).
                var worst = domFracs.OrderBy(x => x.frac).Take(3)
                    .Select(x => $"{x.r.Name}({Compo(x.r)})");
                Console.WriteLine($"{aggr,6:F1} {n,8} {meanDom * 100,8:F1}% {blended,9}/{n,-3} {onFeat * 100,7:F1}%  {string.Join("  ", worst)}");

                // Emit the per-zone grid for the colorblind-safe renderer.
                string tag = aggr.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", "p");
                string gridPath = Path.Combine(dir, $"{seed}_seedlab_a{tag}_grid.bin");
                var idToKey = world.ProtoResult.Regions.ToDictionary(r => r.Id, r => r.RegionKey);
                WriteGrid(gridPath, worldGen, world.Grid, world.RegionIdGrid, idToKey);
            }

            Console.WriteLine();
            Console.WriteLine("meanDom% UP and blended<55% DOWN = the lever moved composition (what 3 prior attempts could NOT).");
            Console.WriteLine($"Grids: {seed}_seedlab_a*_grid.bin (render with tools/mapview/render_map.py-style scripts).");

            // ── Second sweep: hold budget at a moderate level, sweep PLACEMENT BIAS toward interiors ──
            Console.WriteLine();
            Console.WriteLine("=== Placement-bias sweep (budget aggr=2.0 held; bias toward biome interiors) ===");
            Console.WriteLine($"{"bias",6} {"regions",8} {"meanDom%",9} {"blended<55%",12} {"onFeat%",8}  worst-blended examples");
            Console.WriteLine(new string('-', 92));
            double[] biases = { 0.0, 0.5, 0.9 };
            foreach (double bias in biases)
            {
                var worldGen = new WorldGenerator(seed);
                var sampler = new PortWorldSampler(worldGen, seed);
                var world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
                {
                    IncludeInlandWater = true,
                    Namer = new LegacyRegionNamer(),
                    UseFeatureAwareBorders = true,
                    UseBiomeAwareSeeding = true,
                    SeedingFieldOptions = new RegionSeedingFieldOptions { Aggressiveness = 2.0, PlacementBias = bias },
                });

                IReadOnlyList<RegionInfo> regions = world.Regions;
                int n = regions.Count;
                double sumDom = 0; int blended = 0;
                var domFracs = new List<(RegionInfo r, double frac)>();
                foreach (var r in regions)
                {
                    int land = r.SampledLandZones;
                    double dom = (land > 0 && r.BiomeZoneCounts.TryGetValue(r.DominantBiome, out int dc))
                        ? (double)dc / land : 0.0;
                    sumDom += dom; if (dom < 0.55) blended++;
                    domFracs.Add((r, dom));
                }
                double meanDom = n > 0 ? sumDom / n : 0;
                double onFeat = OnFeaturePercent(sampler, world);
                var worst = domFracs.OrderBy(x => x.frac).Take(3).Select(x => $"{x.r.Name}({Compo(x.r)})");
                Console.WriteLine($"{bias,6:F1} {n,8} {meanDom * 100,8:F1}% {blended,9}/{n,-3} {onFeat * 100,7:F1}%  {string.Join("  ", worst)}");

                // Emit a bias-tagged grid for the colorblind-safe before/after renderer.
                string btag = bias.ToString("0.0", CultureInfo.InvariantCulture).Replace(".", "p");
                string bgrid = Path.Combine(dir, $"{seed}_seedlab_a2p0_b{btag}_grid.bin");
                var idToKeyB = world.ProtoResult.Regions.ToDictionary(r => r.Id, r => r.RegionKey);
                WriteGrid(bgrid, worldGen, world.Grid, world.RegionIdGrid, idToKeyB);
            }

            return 0;
        }

        // Top-2 biome composition string, e.g. "Mtn48/Mist30".
        static string Compo(RegionInfo r)
        {
            int land = r.SampledLandZones;
            if (land <= 0) return "-";
            return string.Join("/", r.BiomeZoneCounts.OrderByDescending(kv => kv.Value).Take(2)
                .Select(kv => $"{Abbr(kv.Key)}{(int)Math.Round(100.0 * kv.Value / land)}"));
        }

        static string Abbr(BiomeType b) => b switch
        {
            BiomeType.Meadows => "Mead", BiomeType.BlackForest => "BFor", BiomeType.Swamp => "Swmp",
            BiomeType.Mountain => "Mtn", BiomeType.Plains => "Plns", BiomeType.Mistlands => "Mist",
            BiomeType.AshLands => "Ash", BiomeType.DeepNorth => "DpN", BiomeType.Ocean => "Ocn", _ => "?",
        };

        /// <summary>
        /// Fraction of region-vs-region border zone-edges that sit ON a real feature (a biome
        /// transition or a shore). The on-feature% metric from docs/design/region-borders.md — measured
        /// over the actual regionIdGrid so it reflects the SHIPPED tessellation, not a Python mirror.
        /// </summary>
        static double OnFeaturePercent(IWorldSampler sampler, RegionWorld world)
        {
            int[,] rid = world.RegionIdGrid;
            ZoneGrid grid = world.Grid;
            int size = grid.Size, min = grid.MinIndex;

            // Pre-sample biome + land per cell once.
            var biome = new BiomeType[size, size];
            var isLand = new bool[size, size];
            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int zx = gx + min, zy = gy + min;
                bool land = grid[zx, zy] == DepthClass.Land;
                isLand[gy, gx] = land;
                if (land) biome[gy, gx] = sampler.GetBiome(zx * (float)ZoneSize, zy * (float)ZoneSize);
            }

            long borderEdges = 0, onFeature = 0;
            // Walk every right + up edge once; count those that separate two different regions.
            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int a = rid[gy, gx];
                if (a < 0) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (0, 1) })
                {
                    int nx = gx + dx, ny = gy + dy;
                    if (nx < 0 || nx >= size || ny < 0 || ny >= size) continue;
                    int b = rid[ny, nx];
                    if (b < 0 || b == a) continue; // region-vs-region edges only
                    borderEdges++;
                    // On-feature if the two land cells straddle a biome transition, or either side is
                    // a shore (touches water) — the crisp seams the cost field targets.
                    bool feature = false;
                    if (isLand[gy, gx] && isLand[ny, nx] && biome[gy, gx] != biome[ny, nx]) feature = true;
                    else if (IsShore(isLand, gx, gy, size) || IsShore(isLand, nx, ny, size)) feature = true;
                    if (feature) onFeature++;
                }
            }
            return borderEdges > 0 ? (double)onFeature / borderEdges : 0.0;
        }

        static bool IsShore(bool[,] isLand, int gx, int gy, int size)
        {
            if (!isLand[gy, gx]) return false;
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = gx + dx, ny = gy + dy;
                if (nx < 0 || nx >= size || ny < 0 || ny >= size) return true; // world edge = shore
                if (!isLand[ny, nx]) return true;
            }
            return false;
        }

        // Same binary layout as Gazetteer.WriteGrid (WZGR) so the existing renderers consume it.
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
    }
}
