using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY ANALYSIS (2026-06-29) answering Daniel's deciding question for the de-fragmentation
    /// fork: "what % of total inclusion-by-region would be lost if we excluded the small islands?"
    ///
    /// A fragmented region appears as MULTIPLE outer rings under one region key (main body + each
    /// detached island as its own CCW loop). "Exclude small islands" = keep only each region's LARGEST
    /// outer ring (its main body) and drop the rest. Loss = Σ(fragment ring area)/Σ(total land area).
    /// Reports the headline %, a size-threshold curve (so "small" can be defined by a floor), a
    /// per-region breakdown, and a by-biome rollup (Mistlands fragmentation is the live hypothesis).
    /// Pure vector ring areas (Shoelace), holes (inland water, CW) excluded from the land measure.
    /// </summary>
    public static class IslandLoss
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== Island-loss analysis — seed '{seed}' (exclude-fragments cost) ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();

            double totalLand = 0, totalMain = 0, totalFrag = 0;
            int regionsWithFrags = 0, totalFrags = 0, regionCount = 0;
            // size buckets for fragment rings (km²)
            double[] edges = { 0.01, 0.02, 0.05, 0.10, 0.25, double.MaxValue };
            var bucketArea = new double[edges.Length];
            var bucketCount = new int[edges.Length];
            // per-region rows: (key, name, biome, main km², frag km², fragFrac, fragCount)
            var rows = new List<(string key, string name, BiomeType biome, double main, double frag, double frac, int n)>();
            // by-biome fragment rollup
            var biomeLand = new Dictionary<BiomeType, double>();
            var biomeFrag = new Dictionary<BiomeType, double>();

            foreach (RegionInfo r in world.Regions)
            {
                regionCount++;
                var outers = graph.RingsFor(r.RegionKey)
                    .Where(rg => rg.SignedArea > 0)
                    .Select(rg => Math.Abs(rg.SignedArea) / 1e6)   // km²
                    .OrderByDescending(a => a)
                    .ToList();
                if (outers.Count == 0) continue;

                double main = outers[0];
                double frag = 0; int nf = 0;
                for (int i = 1; i < outers.Count; i++)
                {
                    frag += outers[i]; nf++;
                    int b = 0; while (outers[i] >= edges[b]) b++;
                    bucketArea[b] += outers[i]; bucketCount[b]++;
                }
                double land = main + frag;
                totalLand += land; totalMain += main; totalFrag += frag;
                totalFrags += nf;
                if (nf > 0) regionsWithFrags++;

                biomeLand[r.DominantBiome] = (biomeLand.TryGetValue(r.DominantBiome, out var bl) ? bl : 0) + land;
                biomeFrag[r.DominantBiome] = (biomeFrag.TryGetValue(r.DominantBiome, out var bf) ? bf : 0) + frag;

                rows.Add((r.RegionKey, r.Name, r.DominantBiome, main, frag, land > 0 ? frag / land : 0, nf));
            }

            Console.WriteLine();
            Console.WriteLine($"REGIONS: {regionCount} | total land (Σ outer rings): {totalLand:F2} km²");
            Console.WriteLine($"  main-body land:  {totalMain:F2} km² ({100.0 * totalMain / totalLand:F1}%)");
            Console.WriteLine($"  fragment land:   {totalFrag:F2} km² ({100.0 * totalFrag / totalLand:F2}%)  ← LOST if all fragments excluded");
            Console.WriteLine($"  fragment rings:  {totalFrags} across {regionsWithFrags}/{regionCount} regions");

            Console.WriteLine();
            Console.WriteLine("THRESHOLD CURVE — exclude fragment islands SMALLER THAN floor F:");
            Console.WriteLine("  (cumulative % of TOTAL land lost, and # fragment rings dropped)");
            double cumA = 0; int cumN = 0;
            string[] lbl = { "<0.01", "<0.02", "<0.05", "<0.10", "<0.25", "ALL frags" };
            for (int b = 0; b < edges.Length; b++)
            {
                cumA += bucketArea[b]; cumN += bucketCount[b];
                string fl = b < edges.Length - 1 ? $"F={edges[b]:F2} km² ({lbl[b]})" : $"no floor ({lbl[b]})";
                Console.WriteLine($"    {fl,-26} lost={cumA:F2} km² ({100.0 * cumA / totalLand:F2}% of total)  rings dropped={cumN}");
            }

            Console.WriteLine();
            Console.WriteLine("BY BIOME — fragment land as % of that biome's land (tests the Mistlands hypothesis):");
            foreach (var kv in biomeLand.OrderByDescending(k => biomeFrag.TryGetValue(k.Key, out var f) ? f : 0))
            {
                double f = biomeFrag.TryGetValue(kv.Key, out var fv) ? fv : 0;
                Console.WriteLine($"    {kv.Key,-12} land={kv.Value:F2} km²  fragment={f:F2} km² ({(kv.Value > 0 ? 100.0 * f / kv.Value : 0):F1}% of biome | {(totalFrag > 0 ? 100.0 * f / totalFrag : 0):F0}% of ALL fragment land)");
            }

            Console.WriteLine();
            Console.WriteLine("TOP 12 REGIONS BY FRAGMENT FRACTION (most island-fragmented):");
            Console.WriteLine($"    {"region",-14}{"biome",-12}{"main km²",10}{"frag km²",10}{"frag%",8}{"#isl",6}  name");
            foreach (var row in rows.OrderByDescending(r => r.frac).Take(12))
                Console.WriteLine($"    {row.key,-14}{row.biome,-12}{row.main,10:F3}{row.frag,10:F3}{row.frac * 100,7:F1}%{row.n,6}  {row.name}");

            // regions that are ENTIRELY tiny (main body itself below a floor) — these would VANISH under
            // an absolute-size cull, changing region count / the <200 guard. Domain-relevant.
            Console.WriteLine();
            int tinyMain01 = rows.Count(r => r.main < 0.01), tinyMain05 = rows.Count(r => r.main < 0.05);
            Console.WriteLine($"REGIONS WHOSE MAIN BODY IS TINY (would vanish under an ABSOLUTE floor cull):");
            Console.WriteLine($"    main < 0.01 km²: {tinyMain01} regions | main < 0.05 km²: {tinyMain05} regions");
            return 0;
        }
    }
}
