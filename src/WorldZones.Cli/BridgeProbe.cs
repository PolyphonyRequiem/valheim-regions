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
    /// THROWAWAY (2026-06-29) confirming Daniel's BRIDGING hypothesis: the one-zone shallow coastal
    /// buffer (ProtoRegionGenerator step 5, ExpandRegionsIntoAdjacentShallowZones) is what glues
    /// archipelago islands into a SINGLE outer ring per region — which is why islandloss reported 0%
    /// fragment land. Test: trace rings from the CURRENT grid (buffer included), then trace rings from
    /// the SAME grid masked to land-only at the true sea-level line (30 m, dropping the shallow fringe),
    /// and compare outer-ring counts per region. Regions that go 1→N rings when the buffer is removed
    /// were being BRIDGED by it. Reuses the real RegionBoundaryExtractor so the count is authoritative,
    /// not an approximation. Target model: keep buffer → 1 authoritative outer ring; coastal detail
    /// (the sub-zone island shapes) is the RASTER's job for the 2D map.
    /// </summary>
    public static class BridgeProbe
    {
        const double SeaLevel = 30.0;   // true waterline (provider.WaterLevel)
        const double CoastIso = 25.0;   // where the refiner snaps coast (HeightScalarField.CoastIso)
        const double Zone = 64.0;

        public static int Run(string seed)
        {
            Console.WriteLine($"=== Bridge probe — seed '{seed}' (is the shallow buffer gluing archipelagos?) ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });

            int[,] rid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);

            var idToKey = new Dictionary<int, string>();
            var infoByKey = new Dictionary<string, RegionInfo>();
            foreach (RegionInfo r in world.Regions)
            {
                if (!idToKey.ContainsKey(r.TransientId)) idToKey[r.TransientId] = r.RegionKey;
                infoByKey[r.RegionKey] = r;
            }

            // CURRENT graph (buffer included) — what the authoritative ring is today.
            RegionBoundaryGraph cur = RegionBoundaryExtractor.Extract(rid, min, idToKey);

            // Build masks at the true sea-level line AND at the coast iso, dropping every zone whose
            // CENTRE terrain is below the line (i.e. the shallow-buffer zones). Re-extract rings.
            int bufferZones30 = 0, bufferZones25 = 0, assigned = 0;
            var maskSea = new int[gh, gw];
            var maskIso = new int[gh, gw];
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    int id = rid[gy, gx];
                    maskSea[gy, gx] = -1; maskIso[gy, gx] = -1;
                    if (id < 0) continue;
                    assigned++;
                    double wx = (gx + min) * Zone, wz = (gy + min) * Zone;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    if (h >= SeaLevel) maskSea[gy, gx] = id; else bufferZones30++;
                    if (h >= CoastIso) maskIso[gy, gx] = id; else bufferZones25++;
                }
            RegionBoundaryGraph sea = RegionBoundaryExtractor.Extract(maskSea, min, idToKey);
            RegionBoundaryGraph iso = RegionBoundaryExtractor.Extract(maskIso, min, idToKey);

            // Outer-ring counts per region key.
            Dictionary<string, (int outers, double area)> Tally(RegionBoundaryGraph g)
            {
                var d = new Dictionary<string, (int, double)>();
                foreach (RegionRing rg in g.Rings)
                {
                    if (rg.SignedArea <= 0) continue;   // outer rings only
                    var cur2 = d.TryGetValue(rg.RegionKey, out var v) ? v : (0, 0.0);
                    d[rg.RegionKey] = (cur2.Item1 + 1, cur2.Item2 + Math.Abs(rg.SignedArea) / 1e6);
                }
                return d;
            }
            var tCur = Tally(cur); var tSea = Tally(sea); var tIso = Tally(iso);

            Console.WriteLine();
            Console.WriteLine($"ZONES: {assigned} assigned | shallow-buffer (below sea level 30 m): {bufferZones30} "
                            + $"({100.0 * bufferZones30 / assigned:F1}%) | below coast-iso 25 m: {bufferZones25} ({100.0 * bufferZones25 / assigned:F1}%)");

            // Headline: ring-count distribution current vs sea-level-masked.
            int curMulti = tCur.Count(kv => kv.Value.outers > 1);
            int seaMulti = tSea.Count(kv => kv.Value.outers > 1);
            int isoMulti = tIso.Count(kv => kv.Value.outers > 1);
            int curRings = tCur.Sum(kv => kv.Value.outers);
            int seaRings = tSea.Sum(kv => kv.Value.outers);
            int isoRings = tIso.Sum(kv => kv.Value.outers);
            Console.WriteLine();
            Console.WriteLine("OUTER RINGS PER REGION — buffer included vs stripped to the waterline:");
            Console.WriteLine($"   CURRENT (buffer in):    {curRings} outer rings | {curMulti} regions have >1 ring");
            Console.WriteLine($"   masked @ coast-iso 25m:  {isoRings} outer rings | {isoMulti} regions have >1 ring");
            Console.WriteLine($"   masked @ sea-level 30m:  {seaRings} outer rings | {seaMulti} regions have >1 ring");

            // The bridging count: regions that gain rings when the buffer is removed (1→N).
            int bridged = 0, newFragments = 0; double reappearLand = 0, totalCurLand = tCur.Sum(kv => kv.Value.area);
            var bridgedRows = new List<(string key, string name, BiomeType biome, int curN, int seaN, double seaMain, double seaFrag)>();
            foreach (var kv in tCur)
            {
                int curN = kv.Value.outers;
                int seaN = tSea.TryGetValue(kv.Key, out var sv) ? sv.outers : 0;
                if (seaN > curN)
                {
                    bridged++;
                    newFragments += (seaN - curN);
                    // reappearing land = sum of all-but-largest sea-masked outer rings for this region
                    var seaOuters = sea.RingsFor(kv.Key).Where(r => r.SignedArea > 0)
                                       .Select(r => Math.Abs(r.SignedArea) / 1e6).OrderByDescending(a => a).ToList();
                    double seaMain = seaOuters.Count > 0 ? seaOuters[0] : 0;
                    double seaFrag = seaOuters.Skip(1).Sum();
                    reappearLand += seaFrag;
                    RegionInfo ri = infoByKey.TryGetValue(kv.Key, out var rr) ? rr : null;
                    bridgedRows.Add((kv.Key, ri?.Name ?? "?", ri?.DominantBiome ?? BiomeType.Meadows, curN, seaN, seaMain, seaFrag));
                }
            }
            // regions that LOSE their whole body at the waterline (all-buffer regions → vanish)
            int vanished = tCur.Count(kv => !tSea.ContainsKey(kv.Key));

            Console.WriteLine();
            Console.WriteLine("── BRIDGING VERDICT ────────────────────────────────────────");
            Console.WriteLine($"  regions BRIDGED by the buffer (1 ring now → many at waterline): {bridged}");
            Console.WriteLine($"  islands that re-appear when buffer removed: {newFragments}");
            Console.WriteLine($"  regions that VANISH entirely at the waterline (all-buffer): {vanished}");
            Console.WriteLine($"  land that becomes 'fragment' at the waterline: {reappearLand:F2} km² "
                            + $"({(totalCurLand > 0 ? 100.0 * reappearLand / totalCurLand : 0):F1}% of current land)");
            Console.WriteLine($"  → {(bridged > 0 ? "CONFIRMED: the shallow buffer is the de-fragmentation mechanism." : "NOT bridging — fragments persist regardless of buffer.")}");
            Console.WriteLine("────────────────────────────────────────────────────────────");

            // by-biome rollup of the bridging (Mistlands hypothesis check)
            Console.WriteLine();
            Console.WriteLine("BRIDGED REGIONS BY BIOME (which biomes the buffer rescues most):");
            foreach (var grp in bridgedRows.GroupBy(r => r.biome).OrderByDescending(g => g.Sum(x => x.seaFrag)))
                Console.WriteLine($"    {grp.Key,-12} {grp.Count()} regions, {grp.Sum(x => x.seaN - x.curN)} islands re-appear, {grp.Sum(x => x.seaFrag):F2} km² fragment land");

            Console.WriteLine();
            Console.WriteLine("TOP 12 MOST-BRIDGED REGIONS (islands the buffer is gluing into one ring):");
            Console.WriteLine($"    {"region",-13}{"biome",-12}{"now",5}{"@sea",6}{"main km²",10}{"frag km²",10}  name");
            foreach (var row in bridgedRows.OrderByDescending(r => r.seaN - r.curN).Take(12))
                Console.WriteLine($"    {row.key,-13}{row.biome,-12}{row.curN,5}{row.seaN,6}{row.seaMain,10:F3}{row.seaFrag,10:F3}  {row.name}");

            return 0;
        }
    }
}
