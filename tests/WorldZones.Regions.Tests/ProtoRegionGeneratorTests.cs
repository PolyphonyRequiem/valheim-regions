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

        /// <summary>Runs ComponentLabeler to get land components for the grid.</summary>
        private static List<LandComponent> LabelLand(ZoneGrid grid)
        {
            return ComponentLabeler.LabelLand(grid, out _);
        }

        /// <summary>Shorthand for calling GenerateLand with component labeling.</summary>
        private static ProtoRegionResult Generate(
            ZoneGrid grid, int targetZonesPerRegion, int seedRng,
            out int[,] regionIdGrid, out List<Vector2i> seeds,
            int minRegionZones = ProtoRegionGenerator.DefaultMinRegionZones,
            int minComponentZonesForProto = ProtoRegionGenerator.DefaultMinComponentZonesForProto)
        {
            var land = LabelLand(grid);
            return ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion, seedRng,
                out regionIdGrid, out seeds, minRegionZones, minComponentZonesForProto);
        }

        // ── 1. Every qualifying land zone is assigned ─────────────────

        [Fact]
        public void All_qualifying_land_zones_assigned()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            // 49 zones, all one component → well above threshold
            var result = Generate(grid, targetZonesPerRegion: 10, seedRng: 42,
                out int[,] regionIdGrid, out List<Vector2i> seeds);

            // No minor islets — single component is large
            Assert.Equal(0, result.MinorIsletCount);

            // All land assigned to proto-regions
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
            // Put a 4×4 land block (16 zones, above default threshold of 12)
            FillRect(grid, -2, -2, 1, 1, DepthClass.Land);

            var result = Generate(grid, targetZonesPerRegion: 10, seedRng: 42,
                out int[,] regionIdGrid, out _);

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
            // 4×4 land block
            FillRect(grid, -2, -2, 1, 1, DepthClass.Land);

            var result = Generate(grid, targetZonesPerRegion: 10, seedRng: 42,
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

            // Island A: 3×3 = 9 land (below threshold, but we'll lower it)
            FillRect(grid, -3, -1, -1, 1, DepthClass.Land);
            // Island B: 3×3 = 9 land
            FillRect(grid, 1, -1, 3, 1, DepthClass.Land);

            // Lower threshold so both islands qualify
            var result = Generate(grid, targetZonesPerRegion: 5, seedRng: 42,
                out int[,] regionIdGrid, out _,
                minComponentZonesForProto: 5);

            // Collect region IDs for each island
            var idsA = new HashSet<int>();
            var idsB = new HashSet<int>();

            for (int y = -1; y <= 1; y++)
            {
                for (int x = -3; x <= -1; x++)
                    idsA.Add(regionIdGrid[y - grid.MinIndex, x - grid.MinIndex]);
                for (int x = 1; x <= 3; x++)
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

            var r1 = Generate(grid, 10, 42, out int[,] g1, out List<Vector2i> s1);
            var r2 = Generate(grid, 10, 42, out int[,] g2, out List<Vector2i> s2);

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

            Generate(grid, 10, 42, out _, out List<Vector2i> s1);
            Generate(grid, 10, 99, out _, out List<Vector2i> s2);

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

        // ── 6. Single large component → one region ───────────────────

        [Fact]
        public void Single_large_component_produces_one_region()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            // 4×4 = 16 land zones (above 12 threshold)
            FillRect(grid, -2, -2, 1, 1, DepthClass.Land);

            var result = Generate(grid, targetZonesPerRegion: 100, seedRng: 42,
                out int[,] regionIdGrid, out List<Vector2i> seeds);

            Assert.Equal(1, result.RegionCount);
            Assert.Equal(16, result.LandZoneCount);
            Assert.Equal(0, result.MinorIsletCount);
            Assert.Equal(0, result.UnassignedLandCount);
        }

        // ── 7. Stats are consistent ──────────────────────────────────

        [Fact]
        public void Stats_are_internally_consistent()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            var result = Generate(grid, 10, 42, out _, out _);

            // Sum of region areas + minor islet area = land count
            int totalRegionArea = result.Regions.Sum(r => r.AreaZones);
            Assert.Equal(result.LandZoneCount,
                totalRegionArea + result.MinorIsletTotalArea);

            // UnassignedLandCount == MinorIsletTotalArea
            Assert.Equal(result.MinorIsletTotalArea, result.UnassignedLandCount);

            Assert.True(result.MinAreaZones >= 1);
            Assert.True(result.MaxAreaZones >= result.MinAreaZones);
            Assert.True(result.AvgAreaZones >= result.MinAreaZones);
            Assert.True(result.AvgAreaZones <= result.MaxAreaZones);
        }

        // ── 8. Contiguity check ──────────────────────────────────────

        [Fact]
        public void Each_region_is_contiguous()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);

            var result = Generate(grid, 10, 42, out int[,] regionIdGrid, out _);

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

        // ── 9. No land → no regions ─────────────────────────────────

        [Fact]
        public void No_land_produces_zero_regions()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);

            var result = Generate(grid, 10, 42, out _, out List<Vector2i> seeds);

            Assert.Equal(0, result.RegionCount);
            Assert.Empty(seeds);
            Assert.Empty(result.Regions);
            Assert.Equal(0, result.LandZoneCount);
            Assert.Equal(0, result.MinorIsletCount);
        }

        // ── 10. Minor islets: small components do not get regions ─────

        [Fact]
        public void Small_component_becomes_minor_islet()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);

            // Small island: 2 land zones (well below default threshold of 12)
            grid[0, 0] = DepthClass.Land;
            grid[1, 0] = DepthClass.Land;

            var result = Generate(grid, targetZonesPerRegion: 100, seedRng: 42,
                out int[,] regionIdGrid, out List<Vector2i> seeds);

            // No proto-regions created
            Assert.Equal(0, result.RegionCount);
            Assert.Empty(seeds);

            // One minor islet with 2 zones
            Assert.Equal(1, result.MinorIsletCount);
            Assert.Equal(2, result.MinorIsletTotalArea);
            Assert.Equal(2, result.UnassignedLandCount);

            // Both zones unassigned in grid
            Assert.Equal(-1, regionIdGrid[0 - grid.MinIndex, 0 - grid.MinIndex]);
            Assert.Equal(-1, regionIdGrid[0 - grid.MinIndex, 1 - grid.MinIndex]);
        }

        // ── 11. Tiny region merge ────────────────────────────────────

        [Fact]
        public void Tiny_regions_are_merged()
        {
            var grid = new ZoneGrid(320f); // 11×11 grid = 121 zones
            FillAll(grid, DepthClass.Land);

            // 121 zones, 1 component, target=20 → 6 seeds
            // minRegionZones=6 → no region should be < 6 after merge
            var result = Generate(grid, targetZonesPerRegion: 20, seedRng: 42,
                out _, out _, minRegionZones: 6, minComponentZonesForProto: 1);

            Assert.Equal(0, result.UnassignedLandCount);
            foreach (var region in result.Regions)
            {
                Assert.True(region.AreaZones >= 6 || result.RegionCount == 1,
                    $"Region {region.Id} has area {region.AreaZones} < 6");
            }
        }

        // ── 12. Merge disabled when minRegionZones=0 ─────────────────

        [Fact]
        public void No_merge_when_minRegionZones_zero()
        {
            var grid = new ZoneGrid(320f); // 11×11
            FillAll(grid, DepthClass.Land);

            var result = Generate(grid, targetZonesPerRegion: 20, seedRng: 42,
                out _, out _, minRegionZones: 0, minComponentZonesForProto: 1);

            Assert.Equal(0, result.MergedRegionCount);
        }

        // ── 13. Mixed: large component gets regions, small becomes islet ─

        [Fact]
        public void Mixed_large_and_small_components()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);

            // Large island: 5×5 = 25 zones (above threshold)
            FillRect(grid, -3, -3, 1, 1, DepthClass.Land);
            // Tiny island: 2 zones (below threshold)
            grid[3, 3] = DepthClass.Land;
            grid[3, 2] = DepthClass.Land;

            var result = Generate(grid, targetZonesPerRegion: 50, seedRng: 42,
                out int[,] regionIdGrid, out List<Vector2i> seeds);

            // Large island gets 1+ regions
            Assert.True(result.RegionCount >= 1);
            Assert.Equal(1, result.SeededComponentCount);

            // Small island is a minor islet
            Assert.Equal(1, result.MinorIsletCount);
            Assert.Equal(2, result.MinorIsletTotalArea);

            // Tiny island zones are unassigned
            Assert.Equal(-1, regionIdGrid[3 - grid.MinIndex, 3 - grid.MinIndex]);
            Assert.Equal(-1, regionIdGrid[2 - grid.MinIndex, 3 - grid.MinIndex]);

            // Large island zones are assigned
            for (int y = -3; y <= 1; y++)
                for (int x = -3; x <= 1; x++)
                    Assert.True(regionIdGrid[y - grid.MinIndex, x - grid.MinIndex] >= 0);
        }

        // ── 14. UnassignedLandCount equals minor islet total area ────

        [Fact]
        public void Unassigned_equals_minor_islet_area()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);

            // Several small islands below threshold
            grid[-3, -3] = DepthClass.Land;
            grid[-3, -2] = DepthClass.Land;
            grid[3, 3] = DepthClass.Land;
            grid[2, 3] = DepthClass.Land;

            var result = Generate(grid, targetZonesPerRegion: 100, seedRng: 42,
                out _, out _);

            Assert.Equal(result.MinorIsletTotalArea, result.UnassignedLandCount);
            Assert.Equal(4, result.UnassignedLandCount);
            Assert.Equal(0, result.RegionCount);
        }

        // ── 15. Custom minComponentZonesForProto threshold ───────────

        [Fact]
        public void Custom_threshold_controls_islet_classification()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);

            // 3×3 = 9 land zones
            FillRect(grid, -1, -1, 1, 1, DepthClass.Land);

            // With default threshold (12): this is a minor islet
            var result1 = Generate(grid, 100, 42, out _, out _,
                minComponentZonesForProto: 12);
            Assert.Equal(1, result1.MinorIsletCount);
            Assert.Equal(0, result1.RegionCount);

            // With lower threshold (5): this gets a proto-region
            var result2 = Generate(grid, 100, 42, out _, out _,
                minComponentZonesForProto: 5);
            Assert.Equal(0, result2.MinorIsletCount);
            Assert.Equal(1, result2.RegionCount);
        }
    }
}
