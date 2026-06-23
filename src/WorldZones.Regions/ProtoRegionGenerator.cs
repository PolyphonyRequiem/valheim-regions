using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Regions
{
    /// <summary>
    /// Partitions <see cref="DepthClass.Land"/> zones into proto-regions via
    /// multi-source BFS from deterministically placed seed zones.
    /// <para>
    /// Seeds are placed per land component. Only land components with
    /// <c>AreaZones &gt;= MinComponentZonesForProto</c> receive seeds and
    /// proto-regions. Smaller components are recorded as <see cref="MinorIslet"/>s
    /// and left unassigned, keeping proto-region geometry strictly contiguous.
    /// </para>
    /// <para>
    /// A post-pass merges regions smaller than <see cref="DefaultMinRegionZones"/>
    /// into their longest-border neighbor.
    /// </para>
    /// <para>
    /// Region growth is land-only. After land assignment/merge, a one-zone
    /// non-cascading shallow fringe is assigned from adjacent land regions.
    /// Deep zones remain unassigned.
    /// </para>
    /// </summary>
    public static class ProtoRegionGenerator
    {
        public static ZoneGrid CreateClassifiedGrid(IWorldDataProvider provider, float worldRadiusMeters = ZoneGrid.WorldRadius)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var grid = new ZoneGrid(worldRadiusMeters);
            ZoneClassifier.Classify(grid, provider);
            return grid;
        }

        /// <summary>
        /// Default minimum region size in zones. Regions smaller than this
        /// are merged into their longest-border neighbor.
        /// </summary>
        public const int DefaultMinRegionZones = 6;

        /// <summary>
        /// Default minimum land component size to receive proto-region seeds.
        /// Components smaller than this become <see cref="MinorIslet"/>s.
        /// </summary>
        public const int DefaultMinComponentZonesForProto = 12;

        private static readonly (int dx, int dy)[] Neighbors = { (1, 0), (-1, 0), (0, -1), (0, 1) };

        /// <summary>
        /// Generates proto-regions over land zones using per-component seeding.
        /// <para>
        /// Each land component with <c>AreaZones &gt;= minComponentZonesForProto</c>
        /// receives <c>max(1, AreaZones / targetZonesPerRegion)</c> seeds placed
        /// via farthest-point heuristic, followed by multi-source BFS assignment
        /// restricted to that component's land zones only. Components below the
        /// threshold are recorded as <see cref="MinorIslet"/>s.
        /// </para>
        /// </summary>
        /// <param name="grid">Classified zone grid.</param>
        /// <param name="landComponents">Land components from <see cref="ComponentLabeler.LabelLand"/>.</param>
        /// <param name="targetZonesPerRegion">
        /// Desired average region size in zones.
        /// Per-component seed count = max(1, component.AreaZones / targetZonesPerRegion).
        /// </param>
        /// <param name="seedRng">
        /// Seed for <see cref="System.Random"/> used during seed placement.
        /// Ensures determinism.
        /// </param>
        /// <param name="regionIdGrid">
        /// Output [size, size] array indexed by (zy - grid.MinIndex, zx - grid.MinIndex).
        /// Contains the region ID for assigned zones, or -1 for unassigned/non-land zones.
        /// </param>
        /// <param name="seeds">Output list of seed zone coordinates, in placement order.</param>
        /// <param name="minRegionZones">
        /// Minimum region size. Regions smaller than this are merged into their
        /// longest-border neighbor. Pass 0 to disable merging.
        /// </param>
        /// <param name="minComponentZonesForProto">
        /// Minimum land component size to receive proto-region seeds.
        /// Components smaller than this become <see cref="MinorIslet"/>s.
        /// </param>
        /// <returns>Summary statistics including all generated regions and minor islets.</returns>
        public static ProtoRegionResult GenerateLand(
            ZoneGrid grid,
            List<LandComponent> landComponents,
            int targetZonesPerRegion,
            int seedRng,
            out int[,] regionIdGrid,
            out List<Vector2i> seeds,
            int minRegionZones = DefaultMinRegionZones,
            int minComponentZonesForProto = DefaultMinComponentZonesForProto,
            InlandWaterAttributionOptions inlandWaterOptions = null)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (landComponents == null) throw new ArgumentNullException(nameof(landComponents));
            if (targetZonesPerRegion <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetZonesPerRegion), "Must be > 0");

            var attributionOptions = (inlandWaterOptions ?? InlandWaterAttributionOptions.Disabled).Validated();

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            // ── 1. Partition components into seeded vs minor islets ───
            var seededComponents = new List<LandComponent>();
            var minorIslets = new List<MinorIslet>();
            int minorIsletTotalArea = 0;

            // Sort by descending area for deterministic processing order
            var sortedComponents = new List<LandComponent>(landComponents);
            sortedComponents.Sort((a, b) =>
            {
                int cmp = b.Zones.Count.CompareTo(a.Zones.Count);
                return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
            });

            foreach (var lc in sortedComponents)
            {
                if (lc.Zones.Count >= minComponentZonesForProto)
                {
                    seededComponents.Add(lc);
                }
                else
                {
                    minorIslets.Add(new MinorIslet(lc.Id, lc.Zones.Count));
                    minorIsletTotalArea += lc.Zones.Count;
                }
            }

            // ── 2. Place seeds per qualifying component ──────────────
            seeds = new List<Vector2i>();
            var rng = new Random(seedRng);

            int landCount = 0;
            foreach (var lc in landComponents)
                landCount += lc.Zones.Count;

            foreach (var lc in seededComponents)
            {
                int componentSeedCount = Math.Max(1, lc.Zones.Count / targetZonesPerRegion);
                var placed = PlaceSeeds(lc.Zones, lc.Zones.Count, componentSeedCount, rng);
                seeds.AddRange(placed);
            }

            // ── 3. Multi-source BFS assignment (land-only) ───────────
            regionIdGrid = new int[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    regionIdGrid[y, x] = -1;

            var queue = new Queue<Vector2i>();
            var identityById = new Dictionary<int, Vector2i>(seeds.Count);
            for (int i = 0; i < seeds.Count; i++)
            {
                var s = seeds[i];
                regionIdGrid[s.y - min, s.x - min] = i;
                identityById[i] = s; // Option B: identity starts as the region's own seed coordinate
                queue.Enqueue(s);
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int cgy = cur.y - min;
                int cgx = cur.x - min;
                int curId = regionIdGrid[cgy, cgx];

                foreach (var (dx, dy) in Neighbors)
                {
                    int nx = cur.x + dx;
                    int ny = cur.y + dy;

                    if (nx < min || nx > max || ny < min || ny > max)
                        continue;

                    int ngy = ny - min;
                    int ngx = nx - min;

                    if (regionIdGrid[ngy, ngx] >= 0)
                        continue; // already assigned

                    if (grid[nx, ny] != DepthClass.Land)
                        continue; // land-only in v0

                    regionIdGrid[ngy, ngx] = curId;
                    queue.Enqueue(new Vector2i(nx, ny));
                }
            }

            // ── 4. Merge tiny regions ─────────────────────────────────
            int mergedCount = 0;
            if (minRegionZones > 0 && seeds.Count > 1)
            {
                mergedCount = MergeTinyRegions(grid, regionIdGrid, seeds, minRegionZones, identityById);
            }

            // ── 5. One-zone shallow fringe assignment ───────────────
            ExpandRegionsIntoAdjacentShallowZones(grid, regionIdGrid);

            // ── 6. Optional inland-water attribution ────────────────
            InlandWaterAttributionResult inlandAttribution = InlandWaterAttributionResult.Empty;
            WaterConnectivityKind[,] connectivityGrid = null;
            if (attributionOptions.Enabled)
            {
                var categorization = InlandWaterConnectivityCategorizer.Categorize(grid);
                connectivityGrid = categorization.connectivityGrid;
                inlandAttribution = InlandWaterAttributor.Attribute(
                    grid,
                    regionIdGrid,
                    connectivityGrid,
                    categorization.inlandBodies);
            }

            // ── 7. Build result ───────────────────────────────────────
            var landAreaByRegion = new Dictionary<int, int>();
            var inlandWaterAreaByRegion = new Dictionary<int, int>();
            int unassigned = 0;

            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    int rid = regionIdGrid[zy - min, zx - min];
                    DepthClass depth = grid[zx, zy];

                    if (depth == DepthClass.Land)
                    {
                        if (rid < 0)
                        {
                            unassigned++;
                        }
                        else
                        {
                            if (!landAreaByRegion.ContainsKey(rid))
                            {
                                landAreaByRegion[rid] = 0;
                            }

                            landAreaByRegion[rid]++;
                        }
                    }

                    if (connectivityGrid != null &&
                        rid >= 0 &&
                        connectivityGrid[zy - min, zx - min] == WaterConnectivityKind.InlandWater)
                    {
                        if (!inlandWaterAreaByRegion.ContainsKey(rid))
                        {
                            inlandWaterAreaByRegion[rid] = 0;
                        }

                        inlandWaterAreaByRegion[rid]++;
                    }
                }
            }

            var regions = new List<ProtoRegion>(landAreaByRegion.Count);
            int minArea = int.MaxValue;
            int maxArea = 0;
            long totalArea = 0;

            foreach (var kv in landAreaByRegion)
            {
                var r = new ProtoRegion(kv.Key, seeds[kv.Key]);
                // Option B identity: the min seed coordinate absorbed into this region after merges.
                // Falls back to the region's own seed if (unexpectedly) untracked.
                r.IdentityCoord = identityById.TryGetValue(kv.Key, out var identity)
                    ? identity
                    : seeds[kv.Key];
                r.AreaZones = kv.Value;
                r.LandAreaZones = kv.Value;
                r.InlandWaterAreaZones = inlandWaterAreaByRegion.TryGetValue(kv.Key, out int inlandArea)
                    ? inlandArea
                    : 0;
                regions.Add(r);

                if (kv.Value < minArea) minArea = kv.Value;
                if (kv.Value > maxArea) maxArea = kv.Value;
                totalArea += kv.Value;
            }

            regions.Sort((a, b) => b.AreaZones.CompareTo(a.AreaZones));

            int regionCount = landAreaByRegion.Count;
            int totalInlandWaterArea = 0;
            foreach (var inland in inlandWaterAreaByRegion.Values)
            {
                totalInlandWaterArea += inland;
            }

            return new ProtoRegionResult
            {
                Regions = regions,
                LandZoneCount = landCount,
                RegionCount = regionCount,
                MinAreaZones = regionCount > 0 ? minArea : 0,
                AvgAreaZones = regionCount > 0 ? (float)totalArea / regionCount : 0f,
                MaxAreaZones = maxArea,
                UnassignedLandCount = unassigned,
                MergedRegionCount = mergedCount,
                MinorIslets = minorIslets,
                MinorIsletCount = minorIslets.Count,
                MinorIsletTotalArea = minorIsletTotalArea,
                SeededComponentCount = seededComponents.Count,
                AttributedWaterZoneCount = inlandAttribution.AttributedWaterZoneCount,
                UnassignedInlandWaterZoneCount = inlandAttribution.UnassignedInlandWaterZoneCount,
                AttributedWaterBodyCount = inlandAttribution.AttributedWaterBodyCount,
                UnassignedWaterBodyCount = inlandAttribution.UnassignedWaterBodyCount,
                TotalInlandWaterAreaZones = totalInlandWaterArea,
                TotalRegionTerritoryAreaZones = landCount - unassigned + totalInlandWaterArea
            };
        }

        private static void ExpandRegionsIntoAdjacentShallowZones(ZoneGrid grid, int[,] regionIdGrid)
        {
            int size = grid.Size;
            var original = (int[,])regionIdGrid.Clone();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (original[y, x] >= 0)
                    {
                        continue;
                    }

                    int zoneX = x + grid.MinIndex;
                    int zoneY = y + grid.MinIndex;
                    if (grid[zoneX, zoneY] != DepthClass.Shallow)
                    {
                        continue;
                    }

                    int chosen = ChooseAdjacentAssignedRegionId(original, x, y, size);
                    if (chosen >= 0)
                    {
                        regionIdGrid[y, x] = chosen;
                    }
                }
            }
        }

        private static int ChooseAdjacentAssignedRegionId(int[,] original, int x, int y, int size)
        {
            int left = x > 0 ? original[y, x - 1] : -1;
            int right = x < size - 1 ? original[y, x + 1] : -1;
            int down = y > 0 ? original[y - 1, x] : -1;
            int up = y < size - 1 ? original[y + 1, x] : -1;

            if (left >= 0)
            {
                return left;
            }

            if (right >= 0)
            {
                return right;
            }

            if (down >= 0)
            {
                return down;
            }

            if (up >= 0)
            {
                return up;
            }

            return -1;
        }

        /// <summary>
        /// Merges regions with area &lt; minRegionZones into the neighboring region
        /// sharing the longest 4-neighbor border. Deterministic tie-break: lower region ID wins.
        /// Repeats until no more merges are possible.
        /// </summary>
        /// <returns>Number of regions merged away.</returns>
        private static int MergeTinyRegions(
            ZoneGrid grid, int[,] regionIdGrid, List<Vector2i> seeds, int minRegionZones,
            Dictionary<int, Vector2i> identityById)
        {
            int min = grid.MinIndex;
            int max = grid.MaxIndex;
            int size = grid.Size;
            int totalMerged = 0;

            // Iteratively merge until stable
            bool changed = true;
            while (changed)
            {
                changed = false;

                // Recompute areas
                var areas = new Dictionary<int, int>();
                for (int gy = 0; gy < size; gy++)
                {
                    for (int gx = 0; gx < size; gx++)
                    {
                        int rid = regionIdGrid[gy, gx];
                        if (rid < 0) continue;
                        if (!areas.ContainsKey(rid))
                            areas[rid] = 0;
                        areas[rid]++;
                    }
                }

                // Find tiny regions, sorted by area ascending then ID ascending for determinism
                var tinyRegions = new List<int>();
                foreach (var kv in areas)
                {
                    if (kv.Value < minRegionZones)
                        tinyRegions.Add(kv.Key);
                }

                if (tinyRegions.Count == 0)
                    break;

                tinyRegions.Sort((a, b) =>
                {
                    int cmp = areas[a].CompareTo(areas[b]);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });

                foreach (int tinyId in tinyRegions)
                {
                    // Re-check area — previous merges in this iteration may have changed it
                    int currentArea = 0;
                    for (int gy = 0; gy < size; gy++)
                        for (int gx = 0; gx < size; gx++)
                            if (regionIdGrid[gy, gx] == tinyId)
                                currentArea++;

                    if (currentArea >= minRegionZones || currentArea == 0)
                        continue;

                    // Count border length with each neighbor region
                    var borderCounts = new Dictionary<int, int>();
                    for (int gy = 0; gy < size; gy++)
                    {
                        for (int gx = 0; gx < size; gx++)
                        {
                            if (regionIdGrid[gy, gx] != tinyId)
                                continue;

                            int zx = gx + min;
                            int zy = gy + min;

                            foreach (var (dx, dy) in Neighbors)
                            {
                                int nx = zx + dx;
                                int ny = zy + dy;
                                if (nx < min || nx > max || ny < min || ny > max)
                                    continue;

                                int nrid = regionIdGrid[ny - min, nx - min];
                                if (nrid >= 0 && nrid != tinyId)
                                {
                                    if (!borderCounts.ContainsKey(nrid))
                                        borderCounts[nrid] = 0;
                                    borderCounts[nrid]++;
                                }
                            }
                        }
                    }

                    if (borderCounts.Count == 0)
                        continue; // isolated, no neighbor to merge into

                    // Find best neighbor: longest border, tie-break: lower ID
                    int bestNeighbor = -1;
                    int bestBorder = -1;
                    foreach (var kv in borderCounts)
                    {
                        if (kv.Value > bestBorder ||
                            (kv.Value == bestBorder && kv.Key < bestNeighbor))
                        {
                            bestBorder = kv.Value;
                            bestNeighbor = kv.Key;
                        }
                    }

                    // Merge: reassign all zones of tinyId to bestNeighbor
                    for (int gy = 0; gy < size; gy++)
                        for (int gx = 0; gx < size; gx++)
                            if (regionIdGrid[gy, gx] == tinyId)
                                regionIdGrid[gy, gx] = bestNeighbor;

                    // Option B identity: the survivor inherits the MIN seed coordinate of the two
                    // (its own absorbed set ∪ the tiny region's). This makes identity independent of
                    // which region was the "survivor" and of merge order.
                    if (identityById != null &&
                        identityById.TryGetValue(tinyId, out var tinyIdentity))
                    {
                        if (identityById.TryGetValue(bestNeighbor, out var neighborIdentity))
                            identityById[bestNeighbor] = RegionKey.Min(neighborIdentity, tinyIdentity);
                        else
                            identityById[bestNeighbor] = tinyIdentity;
                        identityById.Remove(tinyId);
                    }

                    totalMerged++;
                    changed = true;
                }
            }

            return totalMerged;
        }

        /// <summary>
        /// Places seed zones using farthest-point heuristic within a given
        /// list of candidate coordinates. First seed is random; each subsequent
        /// seed maximizes minimum Manhattan distance to all existing seeds.
        /// </summary>
        private static List<Vector2i> PlaceSeeds(
            List<Vector2i> landCoords,
            int landCount,
            int seedCount,
            Random rng)
        {
            var seeds = new List<Vector2i>(seedCount);

            if (landCount == 0)
                return seeds;

            // First seed: random land zone from this group
            seeds.Add(landCoords[rng.Next(landCount)]);

            // Subsequent seeds: farthest-point sampling with 256 candidates
            const int CandidateCount = 256;

            for (int s = 1; s < seedCount; s++)
            {
                Vector2i best = landCoords[0];
                int bestScore = -1;

                int candidatesThisRound = Math.Min(CandidateCount, landCount);
                for (int c = 0; c < candidatesThisRound; c++)
                {
                    var candidate = landCoords[rng.Next(landCount)];
                    int minDist = int.MaxValue;

                    for (int e = 0; e < seeds.Count; e++)
                    {
                        int d = ManhattanDistance(candidate, seeds[e]);
                        if (d < minDist)
                            minDist = d;
                    }

                    if (minDist > bestScore ||
                        (minDist == bestScore && TieBreak(candidate, best)))
                    {
                        bestScore = minDist;
                        best = candidate;
                    }
                }

                seeds.Add(best);
            }

            return seeds;
        }

        /// <summary>
        /// Deterministic tie-break: prefer lower x, then lower y.
        /// Returns true if <paramref name="a"/> should win over <paramref name="b"/>.
        /// </summary>
        private static bool TieBreak(Vector2i a, Vector2i b)
        {
            if (a.x != b.x) return a.x < b.x;
            return a.y < b.y;
        }

        private static int ManhattanDistance(Vector2i a, Vector2i b)
        {
            return Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
        }
    }
}
