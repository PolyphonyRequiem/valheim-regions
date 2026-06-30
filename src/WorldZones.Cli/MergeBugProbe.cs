using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-30) — disambiguate the "region min-size MERGE BUG" parked in
    /// docs/design/region-min-size-merge-handoff.md (THREAD 1). The handoff's leading hypothesis (#4):
    /// the sub-floor regions that "have a neighbour yet survive" are NOT a merge bug — their
    /// <see cref="RegionInfo.NeighborKeys"/> (aggregated POST shallow-fringe, NOT land-gated, in
    /// GazetteerBuilder) over-counts vs the merge's 4-neighbour LAND-only <c>borderCounts</c>
    /// (computed PRE-fringe in MergeTinyRegions). A region can show NeighborKeys≥1 while the merge
    /// genuinely saw 0 land-neighbours → unmergeable, not buggy.
    ///
    /// This probe is READ-ONLY (no core-lib change). It:
    ///   (A) PARAM FLOW: builds Astley at MinRegionZones=6 and =25 (else identical to the live overlay
    ///       path: feature-aware borders, no inland water) and prints region count + MergedRegionCount +
    ///       sub-25 survivor count + min area for each — reproduces the handoff's "182 at both" fact AND
    ///       adds the mergedCount the handoff never captured (proves whether 25 reaches the merge).
    ///   (B) SURVIVOR CROSS-TAB at floor=25: for every region with land area &lt; 25, reconstructs the
    ///       merge's view directly from RegionIdGrid:
    ///         landAdj   = 4-neighbour count gating BOTH cells on Grid==Land  (== merge's borderCounts)
    ///         fringeAdj = 4-neighbour count gating NEITHER cell on land      (~ NeighborKeys' view)
    ///       and cross-tabs against RegionInfo.NeighborKeys.Count. Classifies each survivor:
    ///         ISLAND  (landAdj==0, NeighborKeys==0)  — correctly unmergeable, deep-island case
    ///         PHANTOM (landAdj==0, NeighborKeys>=1)  — HYP #4: the "bug" is a definition mismatch
    ///         BUG     (landAdj>=1)                   — a real land-neighbour the merge failed to fold
    /// </summary>
    public static class MergeBugProbe
    {
        // Mirror the two 4-neighbour sets (ProtoRegionGenerator.Neighbors / GazetteerBuilder.N4 — identical).
        static readonly (int dx, int dy)[] N4 = { (1, 0), (-1, 0), (0, -1), (0, 1) };

        public static int Run(string seed)
        {
            Console.WriteLine($"=== region min-size MERGE-BUG probe — seed '{seed}' ===");
            Console.WriteLine("(live-overlay opts: UseFeatureAwareBorders=true, IncludeInlandWater=false)\n");

            // ── (A) PARAM-FLOW: 6 vs 25, everything else identical ──────────────────────────────
            Console.WriteLine("── (A) param flow: does MinRegionZones reach the merge? ──");
            const int FLOOR = 25;
            RegionWorld w6 = Build(seed, 6);
            RegionWorld w25 = Build(seed, FLOOR);
            // High-floor sanity build: pushes normal land-bordering interior regions under the floor, so
            // the merge MUST fire if the param flows + the merge works. merged>0 here kills hyp #1
            // (param-not-flowing) dead — proving the 6/25 no-op is island geometry, not broken plumbing.
            const int HIGH = 130; // > world median (~113 zones)
            RegionWorld wHi = Build(seed, HIGH);
            ReportBuild(seed, 6, w6, FLOOR);
            ReportBuild(seed, FLOOR, w25, FLOOR);
            ReportBuild(seed, HIGH, wHi, FLOOR);
            Console.WriteLine($"  (param-flow check: merged@floor={HIGH} should be >0 if the option reaches the merge)");

            // ── (B) SURVIVOR CROSS-TAB at floor=25 ──────────────────────────────────────────────
            Console.WriteLine($"\n── (B) survivors (land area < {FLOOR}) at floor={FLOOR}: merge-view vs NeighborKeys ──");

            int[,] rid = w25.RegionIdGrid;
            ZoneGrid grid = w25.Grid;
            int min = grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);

            // Land-only area per region id (== what the merge counted; fringe cells excluded). Also a
            // membership set so a survivor's land cells can be scanned without a full re-walk per region.
            var landArea = new Dictionary<int, int>();
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    int id = rid[gy, gx];
                    if (id < 0) continue;
                    if (grid[gx + min, gy + min] != DepthClass.Land) continue; // land-only (merge's view)
                    landArea.TryGetValue(id, out int c);
                    landArea[id] = c + 1;
                }

            // map transient id -> RegionInfo for NeighborKeys + names
            var infoById = new Dictionary<int, RegionInfo>();
            foreach (var r in w25.Regions) infoById[r.TransientId] = r;

            int nIsland = 0, nPhantom = 0, nBug = 0;
            var rows = new List<(string key, string name, string biome, int land, int nk, int landAdj, int fringeAdj, int compZones, int compRegions, string cls)>();

            // Land-component labelling (4-connected over Grid==Land) — independent of region ids. Lets us
            // ask, per survivor: how big is the land component it sits in, and how many regions share it?
            // If a survivor is the ONLY region in its component AND that component is < FLOOR, then the
            // lever for it is the SEED-ELIGIBILITY floor (DefaultMinComponentZonesForProto=12), not the
            // merge floor — raising that would turn it into an unincorporated MinorIslet instead of a runt.
            int[,] comp = LabelLandComponents(grid, min, gh, gw, out var compSize);

            // per land-component: which region ids occupy it (land cells only)
            var regionsInComp = new Dictionary<int, HashSet<int>>();
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    int c = comp[gy, gx];
                    if (c < 0) continue;
                    int id = rid[gy, gx];
                    if (id < 0) continue;
                    if (!regionsInComp.TryGetValue(c, out var set)) regionsInComp[c] = set = new HashSet<int>();
                    set.Add(id);
                }

            foreach (var kv in landArea)
            {
                int id = kv.Key, land = kv.Value;
                if (land >= FLOOR) continue; // survivors only

                // Reconstruct the merge's land-only adjacency + the not-land-gated adjacency from the grid.
                var landNbrs = new HashSet<int>();
                var fringeNbrs = new HashSet<int>();
                for (int gy = 0; gy < gh; gy++)
                    for (int gx = 0; gx < gw; gx++)
                    {
                        if (rid[gy, gx] != id) continue;
                        bool selfLand = grid[gx + min, gy + min] == DepthClass.Land;
                        foreach (var (dx, dy) in N4)
                        {
                            int nx = gx + dx, ny = gy + dy;
                            if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                            int nid = rid[ny, nx];
                            if (nid < 0 || nid == id) continue;
                            // not-land-gated (closer to NeighborKeys' post-fringe scan)
                            fringeNbrs.Add(nid);
                            // land-gated BOTH sides (== merge's borderCounts at pre-fringe time)
                            if (selfLand && grid[nx + min, ny + min] == DepthClass.Land)
                                landNbrs.Add(nid);
                        }
                    }

                var info = infoById.TryGetValue(id, out var ri) ? ri : null;
                int nk = info?.NeighborKeys?.Count ?? -1;
                string key = info?.RegionKey ?? $"#{id}";
                string name = info?.Name ?? "(unnamed)";
                string biome = info?.DominantBiome.ToString() ?? "?";

                string cls;
                if (landNbrs.Count >= 1) { cls = "BUG"; nBug++; }
                else if (nk >= 1) { cls = "PHANTOM"; nPhantom++; }
                else { cls = "ISLAND"; nIsland++; }

                // component context: how big is the land component this survivor lives in, and is it the
                // sole region there? Find the survivor's component via any of its land cells.
                int compId = -1;
                for (int gy = 0; gy < gh && compId < 0; gy++)
                    for (int gx = 0; gx < gw; gx++)
                        if (rid[gy, gx] == id && grid[gx + min, gy + min] == DepthClass.Land)
                        { compId = comp[gy, gx]; break; }
                int compZones = compId >= 0 && compSize.TryGetValue(compId, out int cz) ? cz : -1;
                int compRegions = compId >= 0 && regionsInComp.TryGetValue(compId, out var rs) ? rs.Count : -1;

                rows.Add((key, name, biome, land, nk, landNbrs.Count, fringeNbrs.Count, compZones, compRegions, cls));
            }

            // sort: BUG first (most interesting), then PHANTOM, then ISLAND; within class by area asc
            int Rank(string c) => c == "BUG" ? 0 : c == "PHANTOM" ? 1 : 2;
            rows.Sort((a, b) =>
            {
                int c = Rank(a.cls).CompareTo(Rank(b.cls));
                if (c != 0) return c;
                c = a.land.CompareTo(b.land);
                return c != 0 ? c : string.CompareOrdinal(a.key, b.key);
            });

            Console.WriteLine($"{"regionKey",-12} {"land",4} {"NK",3} {"landAdj",7} {"frngAdj",7} {"comp",5} {"cReg",4}  {"class",-7} {"biome",-11} name");
            foreach (var r in rows)
                Console.WriteLine($"{r.key,-12} {r.land,4} {r.nk,3} {r.landAdj,7} {r.fringeAdj,7} {r.compZones,5} {r.compRegions,4}  {r.cls,-7} {r.biome,-11} {r.name}");

            int soleInSubFloorComp = rows.Count(r => r.compRegions == 1 && r.compZones >= 0 && r.compZones < FLOOR);
            Console.WriteLine($"\nsurvivors total: {rows.Count}   ISLAND(unmergeable, NK=0): {nIsland}   " +
                              $"PHANTOM(landAdj=0 but NK>=1): {nPhantom}   BUG(landAdj>=1): {nBug}");
            Console.WriteLine($"  of those: sole region in their land component AND component < {FLOOR} zones: " +
                              $"{soleInSubFloorComp}  (these are seed-eligibility runts, not merge runts — the lever is " +
                              $"DefaultMinComponentZonesForProto, not MinRegionZones)");
            Console.WriteLine("  legend: comp=land-component zone count, cReg=#regions sharing that component " +
                              "(cReg==1 ⇒ runt IS its own whole component ⇒ nothing to merge into by construction)");

            Console.WriteLine("\n── VERDICT ──");
            if (nBug == 0 && nPhantom > 0)
                Console.WriteLine($"HYP #4 CONFIRMED. The {nPhantom} \"have a neighbour yet survive\" regions ALL have\n" +
                                  "landAdj==0 — the merge genuinely could not fold them. Their NeighborKeys count is a\n" +
                                  "POST-FRINGE phantom (adjacency across a shallow fringe the land-only merge never saw).\n" +
                                  "There is NO merge bug: MergeTinyRegions is correct. The real question is the DESIGN one\n" +
                                  "(should fringe-only-adjacent runts be merge-eligible / exist as regions at all).");
            else if (nBug > 0)
                Console.WriteLine($"REAL BUG. {nBug} survivors have a genuine LAND neighbour (landAdj>=1) yet were not\n" +
                                  "merged — MergeTinyRegions is failing to fold a mergeable runt. Investigate MinorIslet\n" +
                                  "re-surfacing / seed-identity (handoff hyp #2/#3) for these specific keys.");
            else
                Console.WriteLine("All survivors are true isolated islands (landAdj=0, NK=0). Nothing mergeable; the\n" +
                                  "floor bump is a no-op purely because of deep-island geometry.");
            return 0;
        }

        // 4-connected land-component labelling over Grid==Land. Returns a per-cell component id grid
        // (-1 for non-land) and fills compSize with id→zone-count. Independent of region assignment —
        // this is the same topology ComponentLabeler.LabelLand sees, so a survivor's component size tells
        // us whether it's a whole tiny landmass (seed-eligibility runt) or a sub-split of a big one.
        static int[,] LabelLandComponents(ZoneGrid grid, int min, int gh, int gw, out Dictionary<int, int> compSize)
        {
            var comp = new int[gh, gw];
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                    comp[gy, gx] = -1;
            compSize = new Dictionary<int, int>();
            int next = 0;
            var stack = new Stack<(int gx, int gy)>();
            for (int sy = 0; sy < gh; sy++)
                for (int sx = 0; sx < gw; sx++)
                {
                    if (comp[sy, sx] >= 0) continue;
                    if (grid[sx + min, sy + min] != DepthClass.Land) continue;
                    int id = next++;
                    int size = 0;
                    stack.Push((sx, sy));
                    comp[sy, sx] = id;
                    while (stack.Count > 0)
                    {
                        var (cx, cy) = stack.Pop();
                        size++;
                        foreach (var (dx, dy) in N4)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= gw || ny >= gh) continue;
                            if (comp[ny, nx] >= 0) continue;
                            if (grid[nx + min, ny + min] != DepthClass.Land) continue;
                            comp[ny, nx] = id;
                            stack.Push((nx, ny));
                        }
                    }
                    compSize[id] = size;
                }
            return comp;
        }

        static RegionWorld Build(string seed, int minRegionZones)
        {
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            return WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
                MinRegionZones = minRegionZones,
            });
        }

        static void ReportBuild(string seed, int floor, RegionWorld w, int subThreshold)
        {
            int sub = w.Regions.Count(r => r.LandZones < subThreshold);
            int minLand = w.Regions.Count > 0 ? w.Regions.Min(r => r.LandZones) : 0;
            Console.WriteLine($"  floor={floor,2}: regions={w.Regions.Count,3}  merged={w.ProtoResult.MergedRegionCount,3}  " +
                              $"sub-{subThreshold}={sub,3}  minLandZones={minLand,3}");
        }
    }
}
