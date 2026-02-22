using System;
using System.Collections.Generic;
using WorldZones.WorldGen;

namespace WorldZones.Regions
{
    /// <summary>
    /// Summary statistics from proto-region generation.
    /// </summary>
    public class ProtoRegionResult
    {
        /// <summary>All generated proto-regions, sorted by descending area.</summary>
        public List<ProtoRegion> Regions { get; set; } = new List<ProtoRegion>();

        /// <summary>Total number of land zones in the grid.</summary>
        public int LandZoneCount { get; set; }

        /// <summary>Number of proto-regions created.</summary>
        public int RegionCount { get; set; }

        /// <summary>Smallest region area (zone count).</summary>
        public int MinAreaZones { get; set; }

        /// <summary>Average region area (zone count).</summary>
        public float AvgAreaZones { get; set; }

        /// <summary>Largest region area (zone count).</summary>
        public int MaxAreaZones { get; set; }

        /// <summary>
        /// Number of land zones not assigned to any region.
        /// Must be 0 after successful generation.
        /// </summary>
        public int UnassignedLandCount { get; set; }
    }

    /// <summary>
    /// Partitions <see cref="DepthClass.Land"/> zones into proto-regions via
    /// multi-source BFS from deterministically placed seed zones.
    /// <para>
    /// v0 is land-only: shallow and deep zones are excluded from traversal
    /// and assignment.
    /// </para>
    /// </summary>
    public static class ProtoRegionGenerator
    {
        private static readonly (int dx, int dy)[] Neighbors = { (1, 0), (-1, 0), (0, -1), (0, 1) };

        /// <summary>
        /// Generates proto-regions over land zones.
        /// </summary>
        /// <param name="grid">Classified zone grid.</param>
        /// <param name="targetZonesPerRegion">
        /// Desired average region size in zones.
        /// Seed count = max(1, landCount / targetZonesPerRegion).
        /// </param>
        /// <param name="seedRng">
        /// Seed for <see cref="System.Random"/> used during seed placement.
        /// Ensures determinism.
        /// </param>
        /// <param name="regionIdGrid">
        /// Output [size, size] array indexed by (zy - grid.MinIndex, zx - grid.MinIndex).
        /// Contains the region ID for each zone, or -1 for non-land zones.
        /// </param>
        /// <param name="seeds">Output list of seed zone coordinates, in placement order.</param>
        /// <returns>Summary statistics including all generated regions.</returns>
        public static ProtoRegionResult GenerateLand(
            ZoneGrid grid,
            int targetZonesPerRegion,
            int seedRng,
            out int[,] regionIdGrid,
            out List<Vector2i> seeds)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (targetZonesPerRegion <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetZonesPerRegion), "Must be > 0");

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            // ── 1. Collect all land coords in deterministic order ──────
            var landCoords = new List<Vector2i>();
            for (int zy = min; zy <= max; zy++)
                for (int zx = min; zx <= max; zx++)
                    if (grid[zx, zy] == DepthClass.Land)
                        landCoords.Add(new Vector2i(zx, zy));

            int landCount = landCoords.Count;

            // ── 2. Place seeds ────────────────────────────────────────
            seeds = PlaceSeeds(landCoords, landCount, targetZonesPerRegion, seedRng);

            // ── 3. Multi-source BFS assignment ────────────────────────
            regionIdGrid = new int[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    regionIdGrid[y, x] = -1;

            var queue = new Queue<Vector2i>();
            for (int i = 0; i < seeds.Count; i++)
            {
                var s = seeds[i];
                regionIdGrid[s.y - min, s.x - min] = i;
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

            // ── 3b. Mop up: any land zone not reached by BFS gets its own region ──
            // This handles small isolated land components that received no seed.
            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    if (grid[zx, zy] != DepthClass.Land)
                        continue;

                    int gy = zy - min;
                    int gx = zx - min;

                    if (regionIdGrid[gy, gx] >= 0)
                        continue; // already assigned

                    // New region seeded here
                    int newId = seeds.Count;
                    var newSeed = new Vector2i(zx, zy);
                    seeds.Add(newSeed);
                    regionIdGrid[gy, gx] = newId;
                    queue.Enqueue(newSeed);

                    // BFS fill from this new seed
                    while (queue.Count > 0)
                    {
                        var cur2 = queue.Dequeue();
                        int cgy2 = cur2.y - min;
                        int cgx2 = cur2.x - min;

                        foreach (var (dx, dy) in Neighbors)
                        {
                            int nx = cur2.x + dx;
                            int ny = cur2.y + dy;

                            if (nx < min || nx > max || ny < min || ny > max)
                                continue;

                            int ngy = ny - min;
                            int ngx = nx - min;

                            if (regionIdGrid[ngy, ngx] >= 0)
                                continue;

                            if (grid[nx, ny] != DepthClass.Land)
                                continue;

                            regionIdGrid[ngy, ngx] = newId;
                            queue.Enqueue(new Vector2i(nx, ny));
                        }
                    }
                }
            }

            // ── 4. Build result ───────────────────────────────────────
            var regionAreas = new int[seeds.Count];
            int unassigned = 0;

            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    if (grid[zx, zy] != DepthClass.Land)
                        continue;

                    int rid = regionIdGrid[zy - min, zx - min];
                    if (rid < 0)
                        unassigned++;
                    else
                        regionAreas[rid]++;
                }
            }

            var regions = new List<ProtoRegion>(seeds.Count);
            int minArea = int.MaxValue;
            int maxArea = 0;
            long totalArea = 0;

            for (int i = 0; i < seeds.Count; i++)
            {
                var r = new ProtoRegion(i, seeds[i]);
                r.AreaZones = regionAreas[i];
                regions.Add(r);

                if (regionAreas[i] < minArea) minArea = regionAreas[i];
                if (regionAreas[i] > maxArea) maxArea = regionAreas[i];
                totalArea += regionAreas[i];
            }

            regions.Sort((a, b) => b.AreaZones.CompareTo(a.AreaZones));

            return new ProtoRegionResult
            {
                Regions = regions,
                LandZoneCount = landCount,
                RegionCount = seeds.Count,
                MinAreaZones = seeds.Count > 0 ? minArea : 0,
                AvgAreaZones = seeds.Count > 0 ? (float)totalArea / seeds.Count : 0f,
                MaxAreaZones = maxArea,
                UnassignedLandCount = unassigned
            };
        }

        /// <summary>
        /// Places seed zones using farthest-point heuristic:
        /// first seed is random; each subsequent seed maximizes
        /// minimum Manhattan distance to all existing seeds.
        /// </summary>
        private static List<Vector2i> PlaceSeeds(
            List<Vector2i> landCoords,
            int landCount,
            int targetZonesPerRegion,
            int seedRng)
        {
            int seedCount = Math.Max(1, landCount / targetZonesPerRegion);
            var rng = new Random(seedRng);
            var seeds = new List<Vector2i>(seedCount);

            if (landCount == 0)
                return seeds;

            // First seed: random land zone
            seeds.Add(landCoords[rng.Next(landCount)]);

            // Subsequent seeds: farthest-point sampling with 256 candidates
            const int CandidateCount = 256;

            for (int s = 1; s < seedCount; s++)
            {
                Vector2i best = landCoords[0];
                int bestScore = -1;

                for (int c = 0; c < CandidateCount; c++)
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
