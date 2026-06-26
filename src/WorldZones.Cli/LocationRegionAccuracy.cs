using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WorldZones.Runtime;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// REGION-ACCURACY validation: replaces the ≤500m distance proxy with the authoritative region join.
    /// Builds the full RegionWorld (with the offline PortLocationSource), then bins BOTH the computed
    /// locations AND the real .db locations through the SAME <see cref="RegionWorld.RegionAt"/> — so the
    /// comparison is "do they land in the same RegionKey?", not "are they within N metres?".
    ///
    /// <para>Reports two distinct accuracies, because locations can SWAP (computed Crypt2 lands where the
    /// real Crypt3 is — both valid crypt sites, different assignment):</para>
    /// <list type="bullet">
    ///   <item><b>Per-location, same-prefab:</b> for each computed location, is there a real location of
    ///   the SAME prefab in the SAME region? (Punishes swaps — strict.)</item>
    ///   <item><b>Per-region inventory:</b> does each region hold the same multiset of prefab types,
    ///   computed vs real? (Swap-invariant — this is what the substrate actually exposes: "what's here".)
    ///   Reported as the summed intersection over the summed union of per-region prefab counts.</item>
    /// </list>
    /// </summary>
    public static class LocationRegionAccuracy
    {
        sealed class OracleFile { public OracleLoc[] locations { get; set; } }
        sealed class OracleLoc { public string prefab { get; set; } public float x { get; set; } public float z { get; set; } public bool placed { get; set; } }

        public static int Run(string seed, string cataloguePath, string oraclePath)
        {
            Console.WriteLine($"=== Location REGION-accuracy — seed '{seed}' (authoritative grid join) ===");

            var catalogue = LocationValidation.LoadCatalogue(cataloguePath);
            var locSource = PortLocationSource.FromSeed(seed, catalogue);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            RegionWorld world = WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(seed),
                new RegionBuildOptions { IncludeInlandWater = true, LocationSource = locSource });
            sw.Stop();
            Console.WriteLine($"built world: {world.Regions.Count} regions, {world.AllLocations.Count} computed locations in {sw.Elapsed.TotalSeconds:F0}s");

            // Real .db locations, binned through the SAME authoritative RegionAt.
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            OracleFile oracle;
            using (var fs = File.OpenRead(oraclePath)) oracle = JsonSerializer.Deserialize<OracleFile>(fs, opts);
            var real = oracle.locations ?? Array.Empty<OracleLoc>();
            Console.WriteLine($"real .db locations: {real.Length}");

            // region -> prefab -> count, for computed and real (both via world.RegionAt)
            var compByRegion = BuildInventory(world.AllLocations.Select(l => (l.PrefabName, l.X, l.Z)), world);
            var realByRegion = BuildInventory(real.Select(r => (r.prefab, r.x, r.z)), world);

            // Sanity: how many of EACH set bin to a region vs fall outside (ocean/uncovered zones)?
            int compReg = world.AllLocations.Count(l => RegionKeyAt(world, l.X, l.Z) != null);
            int realReg = real.Count(r => RegionKeyAt(world, r.x, r.z) != null);
            Console.WriteLine($"binned-to-a-region: computed {compReg}/{world.AllLocations.Count} ({Pct(compReg, world.AllLocations.Count)}), " +
                              $"real {realReg}/{real.Length} ({Pct(realReg, real.Length)})  — the rest are ocean/uncovered zones, excluded fairly from both");

            // ---- Metric A: per-location same-prefab region agreement (strict, punishes swaps) ----
            // For each computed location: is its prefab present in the real inventory of its region?
            int total = 0, sameRegionSamePrefab = 0, unregioned = 0;
            foreach (var l in world.AllLocations)
            {
                total++;
                var rk = RegionKeyAt(world, l.X, l.Z);
                if (rk == null) { unregioned++; continue; }
                if (realByRegion.TryGetValue(rk, out var inv) && inv.TryGetValue(l.PrefabName, out var c) && c > 0)
                    sameRegionSamePrefab++;
            }
            int regioned = total - unregioned;
            Console.WriteLine($"\n── Metric A: per-location, SAME PREFAB in SAME region (strict) ──");
            Console.WriteLine($"  {sameRegionSamePrefab}/{regioned} = {Pct(sameRegionSamePrefab, regioned)}  ({unregioned} computed locs outside any region)");

            // ---- Metric B: per-region inventory overlap (swap-invariant) ----
            // For each region, sum min(computed_count, real_count) per prefab over sum max(...).
            var allRegions = new HashSet<string>(compByRegion.Keys);
            allRegions.UnionWith(realByRegion.Keys);
            long inter = 0, union = 0;
            int regionsWithBoth = 0;
            double sumPerRegionJaccardCount = 0; int perRegionN = 0;
            foreach (var rk in allRegions)
            {
                compByRegion.TryGetValue(rk, out var ci); ci ??= new Dictionary<string, int>();
                realByRegion.TryGetValue(rk, out var ri); ri ??= new Dictionary<string, int>();
                var prefabs = new HashSet<string>(ci.Keys); prefabs.UnionWith(ri.Keys);
                long rInter = 0, rUnion = 0;
                foreach (var p in prefabs)
                {
                    int a = ci.TryGetValue(p, out var av) ? av : 0;
                    int b = ri.TryGetValue(p, out var bv) ? bv : 0;
                    rInter += Math.Min(a, b);
                    rUnion += Math.Max(a, b);
                }
                inter += rInter; union += rUnion;
                if (ci.Count > 0 && ri.Count > 0) { regionsWithBoth++; }
                if (rUnion > 0) { sumPerRegionJaccardCount += (double)rInter / rUnion; perRegionN++; }
            }
            Console.WriteLine($"\n── Metric B: per-region INVENTORY overlap (swap-invariant — the substrate metric) ──");
            Console.WriteLine($"  count-weighted overlap: {inter}/{union} = {Pct(inter, union)}  (Σ min / Σ max of per-region prefab counts)");
            Console.WriteLine($"  mean per-region overlap: {(perRegionN > 0 ? 100.0 * sumPerRegionJaccardCount / perRegionN : 0):F1}%  over {perRegionN} regions");

            // ---- Metric C: TYPE-level (does region R contain prefab P at all, ignoring count) ----
            long tInter = 0, tUnion = 0;
            foreach (var rk in allRegions)
            {
                compByRegion.TryGetValue(rk, out var ci); ci ??= new Dictionary<string, int>();
                realByRegion.TryGetValue(rk, out var ri); ri ??= new Dictionary<string, int>();
                var cset = new HashSet<string>(ci.Keys); var rset = new HashSet<string>(ri.Keys);
                var both = new HashSet<string>(cset); both.IntersectWith(rset);
                var any = new HashSet<string>(cset); any.UnionWith(rset);
                tInter += both.Count; tUnion += any.Count;
            }
            Console.WriteLine($"\n── Metric C: per-region TYPE presence (does region contain prefab P, ignore count) ──");
            Console.WriteLine($"  {tInter}/{tUnion} = {Pct(tInter, tUnion)}  (region,prefab) pairs agree on presence");

            // ---- Diagnostic: WHERE does inventory overlap fail? Worst regions + the count-skew driver ----
            Console.WriteLine($"\n── Diagnostic: per-region overlap distribution + worst offenders ──");
            var rows = new List<(string rk, long inter, long union, double frac)>();
            foreach (var rk in allRegions)
            {
                compByRegion.TryGetValue(rk, out var ci); ci ??= new Dictionary<string, int>();
                realByRegion.TryGetValue(rk, out var ri); ri ??= new Dictionary<string, int>();
                if (ci.Count == 0 && ri.Count == 0) continue;
                var prefabs = new HashSet<string>(ci.Keys); prefabs.UnionWith(ri.Keys);
                long ii = 0, uu = 0;
                foreach (var p in prefabs)
                {
                    int a = ci.TryGetValue(p, out var av) ? av : 0;
                    int b = ri.TryGetValue(p, out var bv) ? bv : 0;
                    ii += Math.Min(a, b); uu += Math.Max(a, b);
                }
                rows.Add((rk, ii, uu, uu > 0 ? (double)ii / uu : 0));
            }
            // distribution buckets
            int[] buckets = new int[5]; // 0-20,20-40,40-60,60-80,80-100
            foreach (var r in rows) buckets[Math.Min(4, (int)(r.frac * 5))]++;
            Console.WriteLine($"  overlap buckets: [0-20%]={buckets[0]} [20-40%]={buckets[1]} [40-60%]={buckets[2]} [60-80%]={buckets[3]} [80-100%]={buckets[4]}");
            // is the count-weighted total dominated by a few big regions?
            Console.WriteLine("  5 largest regions by union (the count-weight drivers):");
            foreach (var r in rows.OrderByDescending(r => r.union).Take(5))
                Console.WriteLine($"    {r.rk}: {r.inter}/{r.union} = {Pct(r.inter, r.union)}");

            return 0;
        }

        static Dictionary<string, Dictionary<string, int>> BuildInventory(
            IEnumerable<(string prefab, float x, float z)> locs, RegionWorld world)
        {
            var byRegion = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (var (prefab, x, z) in locs)
            {
                var rk = RegionKeyAt(world, x, z);
                if (rk == null) continue;
                if (!byRegion.TryGetValue(rk, out var inv))
                    byRegion[rk] = inv = new Dictionary<string, int>(StringComparer.Ordinal);
                inv[prefab] = (inv.TryGetValue(prefab, out var c) ? c : 0) + 1;
            }
            return byRegion;
        }

        static string RegionKeyAt(RegionWorld world, float x, float z)
        {
            var r = world.RegionAt(x, z);
            return r?.RegionKey;
        }

        static string Pct(long num, long den) => den > 0 ? $"{100.0 * num / den:F1}%" : "n/a";
    }
}
