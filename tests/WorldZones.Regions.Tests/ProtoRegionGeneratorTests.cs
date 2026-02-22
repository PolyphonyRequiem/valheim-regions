using System.Collections.Generic;
using System.Linq;
using Xunit;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Regions.Tests
{
    public class ProtoRegionGeneratorTests
    {
        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>64m radius → zone indices -1..1 → 3×3 grid.</summary>
        private static ZoneGrid SmallGrid() => new ZoneGrid(64f);

        /// <summary>192m radius → zone indices -3..3 → 7×7 grid.</summary>
        private static ZoneGrid MediumGrid() => new ZoneGrid(192f);

        /// <summary>Sets all zones to the given depth class.</summary>
        private static void FillAll(ZoneGrid grid, DepthClass depth)
        {
            foreach (var c in grid.AllCoords())
                grid[c] = depth;
        }

        /// <summary>Sets a rectangular block of zones to the given depth class.</summary>
        private static void FillRect(ZoneGrid grid, int x0, int y0, int x1, int y1, DepthClass depth)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    grid[x, y] = depth;
        }

        // ── 1. Every land zone is assigned ────────────────────────────

        [Fact]
        public void All_land_zones_assigned()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            var result = ProtoRegionGenerator.GenerateLand(
                grid, targetZonesPerRegion: 10, seedRng: 42,
                out int[,] regionIdGrid, out List<Vector2i> seeds);

            Assert.Equal(0, result.UnassignedLandCount);

            // Also verify via grid scan
            foreach (var c in grid.AllCoords())
            {
                int gy = c.y - grid.MinIndex;
                int gx = c.x - grid.MinIndex;
                Assert.True(regionIdGrid[gy, gx] >= 0,
                    $"Land zone ({c.x},{c.y}) was not assigned a region");
            }
        }

        // ── 2. Non-land zones stay unassigned ─────────────────────────

        [Fact]
        public void Deep_zones_not_assigned()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            // Put a single land zone at origin
            grid[0, 0] = DepthClass.Land;

            var result = ProtoRegionGenerator.GenerateLand(
                grid, targetZonesPerRegion: 10, seedRng: 42,
                out int[,] regionIdGrid, out _);

            // The land zone is assigned
            int gy0 = 0 - grid.MinIndex;
            int gx0 = 0 - grid.MinIndex;
            Assert.True(regionIdGrid[gy0, gx0] >= 0);

            // All deep zones stay -1
            foreach (var c in grid.AllCoords())
            {
                if (grid[c] == DepthClass.Deep)
                {
                    int gy = c.y - grid.MinIndex;
                    int gx = c.x - grid.MinIndex;
                    Assert.Equal(-1, regionIdGrid[gy, gx]);
                }
            }
        }

        [Fact]
        public void Shallow_zones_not_assigned_in_v0()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Shallow);
            // Single land zone
            grid[0, 0] = DepthClass.Land;

            var result = ProtoRegionGenerator.GenerateLand(
                grid, targetZonesPerRegion: 10, seedRng: 42,
                out int[,] regionIdGrid, out _);

            foreach (var c in grid.AllCoords())
            {
                if (grid[c] == DepthClass.Shallow)
                {
                    int gy = c.y - grid.MinIndex;
                    int gx = c.x - grid.MinIndex;
                    Assert.Equal(-1, regionIdGrid[gy, gx]);
                }
            }
        }

        // ── 3. Deep water blocks connectivity ─────────────────────────

        [Fact]
        public void Two_islands_separated_by_deep_get_different_regions()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);

            // Island A: left side
            FillRect(grid, -3, -1, -2, 1, DepthClass.Land);
            // Island B: right side
            FillRect(grid, 2, -1, 3, 1, DepthClass.Land);

            var result = ProtoRegionGenerator.GenerateLand(
                grid, targetZonesPerRegion: 3, seedRng: 42,
                out int[,] regionIdGrid, out _);

            Assert.Equal(0, result.UnassignedLandCount);

            // Collect region IDs for each island
            var idsA = new HashSet<int>();
            var idsB = new HashSet<int>();

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -3; x <= -2; x++)
                    idsA.Add(regionIdGrid[y - grid.MinIndex, x - grid.MinIndex]);
                for (int x = 2; x <= 3; x++)
                    idsB.Add(regionIdGrid[y - grid.MinIndex, x - grid.MinIndex]);
            }

            // No region ID should appear on both islands
            Assert.Empty(idsA.Intersect(idsB));
        }

        // ── 4. Determinism ────────────────────────────────────────────

        [Fact]
        public void Same_inputs_produce_identical_output()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            var r1 = ProtoRegionGenerator.GenerateLand(
                grid, 10, 42, out int[,] g1, out List<Vector2i> s1);
            var r2 = ProtoRegionGenerator.GenerateLand(
                grid, 10, 42, out int[,] g2, out List<Vector2i> s2);

            Assert.Equal(r1.RegionCount, r2.RegionCount);
            Assert.Equal(s1.Count, s2.Count);

            for (int i = 0; i < s1.Count; i++)
            {
                Assert.Equal(s1[i].x, s2[i].x);
                Assert.Equal(s1[i].y, s2[i].y);
            }

            for (int y = 0; y < grid.Size; y++)
                for (int x = 0; x < grid.Size; x++)
                    Assert.Equal(g1[y, x], g2[y, x]);
        }

        // ── 5. Different seed RNG → different output ──────────────────

        [Fact]
        public void Different_seedRng_produces_different_seeds()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            ProtoRegionGenerator.GenerateLand(
                grid, 10, 42, out _, out List<Vector2i> s1);
            ProtoRegionGenerator.GenerateLand(
                grid, 10, 99, out _, out List<Vector2i> s2);

            // At least one seed should differ
            bool anyDiff = false;
            for (int i = 0; i < System.Math.Min(s1.Count, s2.Count); i++)
            {
                if (s1[i].x != s2[i].x || s1[i].y != s2[i].y)
                {
                    anyDiff = true;
                    break;
                }
            }

            Assert.True(anyDiff, "Expected different seed placement for different RNG seed");
        }

        // ── 6. Seed count scales with land area ───────────────────────

        [Fact]
        public void Seed_count_matches_target_density()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);
            int landCount = grid.Size * grid.Size; // 49

            var result = ProtoRegionGenerator.GenerateLand(
                grid, targetZonesPerRegion: 10, seedRng: 42,
                out _, out List<Vector2i> seeds);

            int expectedSeeds = System.Math.Max(1, landCount / 10);
            Assert.Equal(expectedSeeds, seeds.Count);
            Assert.Equal(expectedSeeds, result.RegionCount);
        }

        // ── 7. Single land zone → single region ──────────────────────

        [Fact]
        public void Single_land_zone_produces_one_region()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);
            grid[0, 0] = DepthClass.Land;

            var result = ProtoRegionGenerator.GenerateLand(
                grid, targetZonesPerRegion: 100, seedRng: 42,
                out int[,] regionIdGrid, out List<Vector2i> seeds);

            Assert.Equal(1, result.RegionCount);
            Assert.Equal(1, result.LandZoneCount);
            Assert.Equal(0, result.UnassignedLandCount);
            Assert.Equal(1, result.MinAreaZones);
            Assert.Equal(1, result.MaxAreaZones);
        }

        // ── 8. Stats are consistent ──────────────────────────────────

        [Fact]
        public void Stats_are_internally_consistent()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            var result = ProtoRegionGenerator.GenerateLand(
                grid, 10, 42, out _, out _);

            // Sum of all region areas = land count
            int totalArea = result.Regions.Sum(r => r.AreaZones);
            Assert.Equal(result.LandZoneCount, totalArea);
            Assert.Equal(0, result.UnassignedLandCount);
            Assert.True(result.MinAreaZones >= 1);
            Assert.True(result.MaxAreaZones >= result.MinAreaZones);
            Assert.True(result.AvgAreaZones >= result.MinAreaZones);
            Assert.True(result.AvgAreaZones <= result.MaxAreaZones);
        }

        // ── 9. Contiguity check ──────────────────────────────────────

        [Fact]
        public void Each_region_is_contiguous()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            var result = ProtoRegionGenerator.GenerateLand(
                grid, 10, 42, out int[,] regionIdGrid, out _);

            // For each region, BFS from any zone should reach all zones of that region
            var regionZones = new Dictionary<int, List<Vector2i>>();
            foreach (var c in grid.AllCoords())
            {
                int gy = c.y - grid.MinIndex;
                int gx = c.x - grid.MinIndex;
                int rid = regionIdGrid[gy, gx];
                if (rid < 0) continue;
                if (!regionZones.ContainsKey(rid))
                    regionZones[rid] = new List<Vector2i>();
                regionZones[rid].Add(c);
            }

            var dirs = new (int, int)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

            foreach (var kv in regionZones)
            {
                var zones = new HashSet<(int, int)>(kv.Value.Select(v => (v.x, v.y)));
                var start = kv.Value[0];
                var visited = new HashSet<(int, int)>();
                var bfsQueue = new Queue<(int, int)>();
                bfsQueue.Enqueue((start.x, start.y));
                visited.Add((start.x, start.y));

                while (bfsQueue.Count > 0)
                {
                    var (cx, cy) = bfsQueue.Dequeue();
                    foreach (var (dx, dy) in dirs)
                    {
                        var n = (cx + dx, cy + dy);
                        if (zones.Contains(n) && !visited.Contains(n))
                        {
                            visited.Add(n);
                            bfsQueue.Enqueue(n);
                        }
                    }
                }

                Assert.Equal(zones.Count, visited.Count);
            }
        }

        // ── 10. No land → no regions ─────────────────────────────────

        [Fact]
        public void No_land_produces_zero_regions()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);

            var result = ProtoRegionGenerator.GenerateLand(
                grid, 10, 42, out _, out List<Vector2i> seeds);

            Assert.Equal(0, result.RegionCount);
            Assert.Empty(seeds);
            Assert.Empty(result.Regions);
            Assert.Equal(0, result.LandZoneCount);
        }
    }
}
