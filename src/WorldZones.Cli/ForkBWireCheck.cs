using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-07-01) — verifies the FORK B live wire headlessly: runs the EXACT plugin fill-bake
    /// path (RefinedRegionBoundary → RegionRingFillBaker.Bake) BOTH ways — independent per-region refine
    /// (UseSharedSeamFill=false, today) vs the shared-seam reassembly (=true, the wire) — and confirms the
    /// shared path bakes a valid, near-complete fill raster (no holes vs the independent baseline). This is
    /// the "the flip will actually work in-game" proof; the plugin toggle only compiles otherwise.
    /// </summary>
    public static class ForkBWireCheck
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== Fork B wire check — seed '{seed}' (plugin fill-bake path, both ways) ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true, ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
            });

            int[,] grid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = grid.GetLength(0), gw = grid.GetLength(1);
            const double zone = 64.0;
            int fineSub = 4;   // 16 m texel, same as the plugin

            var keyToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RegionInfo r in world.Regions) keyToLabel[r.RegionKey] = r.TransientId;
            var idToKey = new Dictionary<int, string>();
            foreach (RegionInfo r in world.Regions) if (!idToKey.ContainsKey(r.TransientId)) idToKey[r.TransientId] = r.RegionKey;

            var coast = new HeightScalarField(sampler, HeightScalarField.SeaLevel);
            var flip = new BiomeCategoryField(sampler);
            RegionRingRefiner.RegionIdAt ridAt = (wx, wz) =>
            {
                int zx = (int)Math.Round(wx / zone) - min, zy = (int)Math.Round(wz / zone) - min;
                return (zx < 0 || zy < 0 || zx >= gw || zy >= gh) ? -1 : grid[zy, zx];
            };
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);

            // ── PATH A: independent per-region refine (today's shipped fill) ──
            RefinedRegionBoundary indep = RefinedRegionBoundary.Build(graph, keyToLabel, ridAt, coast, flip);
            int[,] maskIndep = new RegionRingFillBaker(indep, keyToLabel).Bake(gh, gw, min, fineSub);

            // ── PATH B: shared-seam reassembly, adapted to RefinedRegionBoundary (the WIRE) ──
            SharedSeamSet seams = SharedSeamSet.Build(graph, coast, flip);
            RefinedRegionBoundary shared = SharedSeamBoundary.ToRefinedRegionBoundary(seams, graph, fallback: indep);
            int[,] maskShared = new RegionRingFillBaker(shared, keyToLabel).Bake(gh, gw, min, fineSub);

            // ── Compare ──
            long filledIndep = 0, filledShared = 0, differ = 0, both = 0;
            int fh = maskIndep.GetLength(0), fw = maskIndep.GetLength(1);
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    int a = maskIndep[y, x], b = maskShared[y, x];
                    if (a >= 0) filledIndep++;
                    if (b >= 0) filledShared++;
                    if (a >= 0 && b >= 0) both++;
                    if (a != b) differ++;
                }
            long total = (long)fh * fw;
            Console.WriteLine($"raster {fw}x{fh} ({total} texels)");
            Console.WriteLine($"  independent fill: {filledIndep} texels ({100.0 * filledIndep / total:F1}%)");
            Console.WriteLine($"  shared-seam fill: {filledShared} texels ({100.0 * filledShared / total:F1}%)");
            Console.WriteLine($"  texels filled by BOTH: {both}");
            Console.WriteLine($"  texels where label differs (the fill moved): {differ} ({100.0 * differ / total:F2}% of all, "
                            + $"{100.0 * differ / Math.Max(1, filledIndep):F2}% of filled)");

            double coverRatio = filledIndep > 0 ? (double)filledShared / filledIndep : 0;
            SharedSeamBoundary.Build(seams, graph, out var failed);
            Console.WriteLine($"  regions that fell back (failed reassembly → independent ring): {failed.Count}");
            Console.WriteLine();
            bool ok = coverRatio > 0.98 && coverRatio < 1.02;   // fill coverage within 2% of baseline (no holes)
            Console.WriteLine(ok
                ? $"VERDICT: WIRE OK ✓ — shared-seam fill covers {100.0 * coverRatio:F1}% of the baseline (no holes); "
                  + "the flip will produce a complete map fill, with the border on the shared curve."
                : $"VERDICT: INVESTIGATE — shared fill coverage {100.0 * coverRatio:F1}% of baseline (expected ~100%).");
            return ok ? 0 : 1;
        }
    }
}
