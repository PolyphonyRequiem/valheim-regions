using System.Collections.Generic;
using System.Linq;
using Xunit;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Regions.Tests
{
    public class ComponentLabelerTests
    {
        // ── Helpers ───────────────────────────────────────────────────

        private static ZoneGrid SmallGrid() => new ZoneGrid(64f);   // 3×3
        private static ZoneGrid MediumGrid() => new ZoneGrid(192f); // 7×7

        private static void FillAll(ZoneGrid grid, DepthClass depth)
        {
            foreach (var c in grid.AllCoords())
                grid[c] = depth;
        }

        private static void FillRect(ZoneGrid grid, int x0, int y0, int x1, int y1, DepthClass depth)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    grid[x, y] = depth;
        }

        // ══════════════════════════════════════════════════════════════
        //  LabelLand
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void All_land_grid_produces_single_component()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Land);

            var comps = ComponentLabeler.LabelLand(grid, out var labels);

            Assert.Single(comps);
            Assert.Equal(grid.Size * grid.Size, comps[0].Zones.Count);
        }

        [Fact]
        public void All_deep_grid_produces_no_components()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);

            var comps = ComponentLabeler.LabelLand(grid, out _);

            Assert.Empty(comps);
        }

        [Fact]
        public void Two_islands_separated_by_deep_get_distinct_ids()
        {
            // 7×7 grid: two 3×3 land blocks separated by deep column
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            FillRect(grid, -3, -3, -1, -1, DepthClass.Land); // island A (9 zones)
            FillRect(grid,  1,  1,  3,  3, DepthClass.Land); // island B (9 zones)

            var comps = ComponentLabeler.LabelLand(grid, out var labels);

            Assert.Equal(2, comps.Count);
            // Both should have 9 zones
            Assert.All(comps, c => Assert.Equal(9, c.Zones.Count));
            // Distinct IDs
            Assert.NotEqual(comps[0].Id, comps[1].Id);
        }

        [Fact]
        public void Land_bridge_merges_into_single_component()
        {
            // 7×7 grid: two land blocks connected by a 1-zone bridge
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            FillRect(grid, -3, -3, -1, -1, DepthClass.Land); // west block
            FillRect(grid,  1, -1,  3, -1, DepthClass.Land); // east block
            grid[0, -1] = DepthClass.Land;                   // bridge

            var comps = ComponentLabeler.LabelLand(grid, out _);

            Assert.Single(comps);
        }

        [Fact]
        public void Shallow_zones_excluded_from_land_components()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Shallow);
            grid[0, 0] = DepthClass.Land;

            var comps = ComponentLabeler.LabelLand(grid, out var labels);

            Assert.Single(comps);
            Assert.Single(comps[0].Zones);
            // Non-land zones labeled -1
            Assert.Equal(-1, labels[0, 0]); // grid corner
        }

        [Fact]
        public void Label_grid_matches_component_zones()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            FillRect(grid, -3, -3, -2, -2, DepthClass.Land);
            FillRect(grid,  2,  2,  3,  3, DepthClass.Land);

            var comps = ComponentLabeler.LabelLand(grid, out var labels);
            int min = grid.MinIndex;

            foreach (var comp in comps)
            {
                foreach (var z in comp.Zones)
                {
                    Assert.Equal(comp.Id, labels[z.y - min, z.x - min]);
                }
            }
        }

        [Fact]
        public void Components_sorted_by_descending_size()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            FillRect(grid, -3, -3, -2, -2, DepthClass.Land); // 4 zones
            FillRect(grid,  1,  1,  3,  3, DepthClass.Land); // 9 zones

            var comps = ComponentLabeler.LabelLand(grid, out _);

            Assert.Equal(2, comps.Count);
            Assert.True(comps[0].Zones.Count >= comps[1].Zones.Count);
        }

        // ══════════════════════════════════════════════════════════════
        //  ComputeDistanceToLand
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void Land_zones_have_distance_zero()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Land);

            var dist = ComponentLabeler.ComputeDistanceToLand(grid);

            for (int y = 0; y < grid.Size; y++)
                for (int x = 0; x < grid.Size; x++)
                    Assert.Equal(0, dist[y, x]);
        }

        [Fact]
        public void Deep_zones_have_max_distance()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);

            var dist = ComponentLabeler.ComputeDistanceToLand(grid);

            for (int y = 0; y < grid.Size; y++)
                for (int x = 0; x < grid.Size; x++)
                    Assert.Equal(int.MaxValue, dist[y, x]);
        }

        [Fact]
        public void Shallow_zone_adjacent_to_land_has_distance_one()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);
            grid[0, 0] = DepthClass.Land;
            grid[1, 0] = DepthClass.Shallow;

            var dist = ComponentLabeler.ComputeDistanceToLand(grid);
            int min = grid.MinIndex;

            Assert.Equal(0, dist[0 - min, 0 - min]);
            Assert.Equal(1, dist[0 - min, 1 - min]);
        }

        [Fact]
        public void Deep_blocks_distance_propagation()
        {
            // Land at (-1,0), Deep at (0,0), Shallow at (1,0)
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);
            grid[-1, 0] = DepthClass.Land;
            grid[1, 0] = DepthClass.Shallow;

            var dist = ComponentLabeler.ComputeDistanceToLand(grid);
            int min = grid.MinIndex;

            Assert.Equal(0, dist[0 - min, -1 - min]);
            // (1,0) is shallow but separated by deep — unreachable
            Assert.Equal(int.MaxValue, dist[0 - min, 1 - min]);
        }

        // ══════════════════════════════════════════════════════════════
        //  LabelShelf
        // ══════════════════════════════════════════════════════════════

        [Fact]
        public void Shelf_connects_land_across_narrow_shallow()
        {
            // Two land zones separated by 1 shallow zone
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);
            grid[-1, 0] = DepthClass.Land;
            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Land;

            var landComps = ComponentLabeler.LabelLand(grid, out var landLabels);
            Assert.Equal(2, landComps.Count); // two separate land components

            var shelfComps = ComponentLabeler.LabelShelf(grid, landLabels, out _);

            Assert.Single(shelfComps); // connected via shallow
            Assert.Equal(3, shelfComps[0].Zones.Count);
            Assert.Equal(2, shelfComps[0].ContainedLandComponentIds.Count);
        }

        [Fact]
        public void Deep_severs_shelf_connection()
        {
            // Two land zones separated by deep — no shelf bridge
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);
            grid[-1, 0] = DepthClass.Land;
            grid[1, 0] = DepthClass.Land;
            // (0,0) stays deep

            var landComps = ComponentLabeler.LabelLand(grid, out var landLabels);
            var shelfComps = ComponentLabeler.LabelShelf(grid, landLabels, out _);

            Assert.Equal(2, shelfComps.Count);
        }

        [Fact]
        public void Shallow_gap_exceeding_max_distance_severs_shelf()
        {
            // 7×7 grid: land — 3 shallow — land (gap = 3, default max = 2)
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            grid[-3, 0] = DepthClass.Land;
            grid[-2, 0] = DepthClass.Shallow;
            grid[-1, 0] = DepthClass.Shallow;
            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Land;

            var landComps = ComponentLabeler.LabelLand(grid, out var landLabels);
            var shelfComps = ComponentLabeler.LabelShelf(grid, landLabels, out _);

            // Gap of 3 shallow > max 2 → severed
            Assert.Equal(2, shelfComps.Count);
        }

        [Fact]
        public void Shelf_maps_contained_land_component_ids()
        {
            var grid = SmallGrid();
            FillAll(grid, DepthClass.Deep);
            grid[-1, 0] = DepthClass.Land;
            grid[0, 0] = DepthClass.Shallow;
            grid[1, 0] = DepthClass.Land;

            var landComps = ComponentLabeler.LabelLand(grid, out var landLabels);
            var shelfComps = ComponentLabeler.LabelShelf(grid, landLabels, out _);

            Assert.Single(shelfComps);
            var ids = shelfComps[0].ContainedLandComponentIds;
            Assert.Equal(2, ids.Count);
            // Should contain both land component IDs
            Assert.Contains(landComps[0].Id, ids);
            Assert.Contains(landComps[1].Id, ids);
        }

        [Fact]
        public void Deep_zones_never_in_any_shelf()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Deep);
            FillRect(grid, -3, -3, -1, -1, DepthClass.Land);

            var landComps = ComponentLabeler.LabelLand(grid, out var landLabels);
            var shelfComps = ComponentLabeler.LabelShelf(grid, landLabels, out var shelfLabels);

            int min = grid.MinIndex;
            for (int zy = min; zy <= grid.MaxIndex; zy++)
                for (int zx = min; zx <= grid.MaxIndex; zx++)
                    if (grid[zx, zy] == DepthClass.Deep)
                        Assert.Equal(-1, shelfLabels[zy - min, zx - min]);
        }
    }
}
