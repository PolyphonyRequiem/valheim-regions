using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-30) — intent-setting sweep for the LOCKED design A: raise
    /// <see cref="RegionBuildOptions.MinComponentZonesForProto"/> so tiny whole-island land components
    /// demote to UNINCORPORATED (MinorIslet) instead of becoming runt regions. Same method we used to
    /// tune the swamp floor: measure the real distribution first, let Daniel pick the line by intent,
    /// then show what that intent costs with an ACTUAL build at each candidate floor.
    ///
    /// Two parts, both on the real seed Daniel walks:
    ///   (A) DISTRIBUTION — histogram the authoritative land-component sizes (ComponentLabeler.LabelLand,
    ///       the exact partition GenerateLand seeds from). Buckets around the decision band so the
    ///       "where's the natural gap" question is answerable. Shows how many components (and how many
    ///       LAND ZONES) sit below each candidate floor — i.e. exactly what demotes.
    ///   (B) BUILD SWEEP — for each candidate floor, run the FULL live-overlay build with that floor and
    ///       report: region count, regions still under 25 zones, min region land, MinorIslet count +
    ///       total islet land, and world-wide unincorporated land %. This is the real before/after, not
    ///       a projection — it includes second-order effects (a demoted component changes nothing else,
    ///       but the build proves it rather than assuming it).
    /// </summary>
    public static class CompFloorSweep
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== component-floor (design A) intent sweep — seed '{seed}' ===");
            Console.WriteLine("Raising MinComponentZonesForProto demotes tiny land components to unincorporated.\n");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);

            // ── (A) authoritative land-component size distribution ──────────────────────────────
            // Build the SAME classified grid the runtime builds (swamp floor on, default 28.5), so the
            // components are exactly those GenerateLand partitions. Then label land the way step-2 does.
            var grid = new ZoneGrid(ZoneGrid.WorldRadius);
            ZoneClassifier.ClassifyWithSwampFloor(
                grid,
                (wx, wz) => sampler.GetHeight(wx, wz),
                (wx, wz) => sampler.GetBiome(wx, wz) == BiomeType.Swamp,
                28.5f);
            List<LandComponent> comps = ComponentLabeler.LabelLand(grid, out _);
            var sizes = comps.Select(c => c.Zones.Count).OrderBy(n => n).ToList();
            int totalLandZones = sizes.Sum();

            Console.WriteLine($"── (A) land components: {comps.Count} total, {totalLandZones} land zones ──");
            // size histogram across the decision band (1-zone speckle up through 'real place')
            int[] edges = { 1, 2, 4, 6, 9, 12, 16, 20, 25, 30, 40, 60, 100, int.MaxValue };
            string[] lbl = { "1", "2-3", "4-5", "6-8", "9-11", "12-15", "16-19", "20-24", "25-29", "30-39", "40-59", "60-99", "100+" };
            var binCount = new int[lbl.Length];
            var binZones = new int[lbl.Length];
            foreach (int s in sizes)
                for (int b = 0; b < lbl.Length; b++)
                    if (s >= edges[b] && s < edges[b + 1]) { binCount[b]++; binZones[b] += s; break; }
            Console.WriteLine($"{"sizeZones",-10} {"#comps",7} {"landZones",10}");
            for (int b = 0; b < lbl.Length; b++)
                Console.WriteLine($"{lbl[b],-10} {binCount[b],7} {binZones[b],10}");

            // cumulative "what demotes at floor F" for the candidate floors
            Console.WriteLine($"\n── what each candidate floor DEMOTES (components < floor → unincorporated) ──");
            int[] floors = { 12, 15, 18, 20, 25 };
            Console.WriteLine($"{"floor",5} {"compsDemoted",13} {"landZonesLost",14} {"%ofLand",8}");
            foreach (int f in floors)
            {
                int dc = sizes.Count(s => s < f);
                int dz = sizes.Where(s => s < f).Sum();
                Console.WriteLine($"{f,5} {dc,13} {dz,14} {(totalLandZones > 0 ? 100.0 * dz / totalLandZones : 0):F2}%");
            }

            // ── (B) ACTUAL build sweep ──────────────────────────────────────────────────────────
            Console.WriteLine($"\n── (B) actual builds at each floor (live-overlay opts) ──");
            Console.WriteLine($"{"floor",5} {"regions",8} {"sub25",6} {"minLand",8} {"islets",7} {"isletZones",11} {"unincLand%",11}");
            foreach (int f in floors)
            {
                RegionWorld w = Build(seed, f);
                int regions = w.Regions.Count;
                int sub25 = w.Regions.Count(r => r.LandZones < 25);
                int minLand = regions > 0 ? w.Regions.Min(r => r.LandZones) : 0;
                int islets = w.ProtoResult.MinorIsletCount;
                int isletZones = w.ProtoResult.MinorIsletTotalArea;
                double uninc = UnincLandPct(w);
                Console.WriteLine($"{f,5} {regions,8} {sub25,6} {minLand,8} {islets,7} {isletZones,11} {uninc,10:F2}%");
            }

            Console.WriteLine("\nGuidance: pick the floor by INTENT (where the natural gap is + what a 'real place' means),");
            Console.WriteLine("not the max-cleanup number. 'sub25' falling to ~0 means no runt regions survive; 'minLand'");
            Console.WriteLine("is the smallest region that remains. The demoted land becomes unincorporated (islets), which");
            Console.WriteLine("is the locked intent — verify the unincLand% rise is acceptable on the walk before shipping.");
            return 0;
        }

        // World-wide unincorporated LAND %: land texels (zone-centre, swamp-floor classify) with no region.
        static double UnincLandPct(RegionWorld w)
        {
            int[,] rid = w.RegionIdGrid;
            ZoneGrid grid = w.Grid;
            int min = grid.MinIndex, gh = rid.GetLength(0), gw = rid.GetLength(1);
            long land = 0, uninc = 0;
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    if (grid[gx + min, gy + min] != DepthClass.Land) continue;
                    land++;
                    if (rid[gy, gx] < 0) uninc++;
                }
            return land > 0 ? 100.0 * uninc / land : 0.0;
        }

        static RegionWorld Build(string seed, int minComponentZonesForProto)
        {
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            return WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
                MinComponentZonesForProto = minComponentZonesForProto,
            });
        }
    }
}
