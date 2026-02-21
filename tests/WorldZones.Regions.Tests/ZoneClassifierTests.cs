using System;
using Xunit;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Regions.Tests
{
    public class ZoneClassifierTests
    {
        // ±64 m → zone indices -1..1 → 3×3 = 9 zones
        private static ZoneGrid SmallGrid() => new ZoneGrid(64f);

        private static readonly ZoneClassifierOptions Defaults = new ZoneClassifierOptions
        {
            SeaLevelBaseHeight  = 0.05f,
            ShallowDepthBelowSea = 0.02f
        };

        // ── 1. Classification thresholds ──────────────────────────────

        [Fact]
        public void Height_at_sea_level_is_Land()
        {
            var grid = SmallGrid();
            ZoneClassifier.Classify(grid, (_, __) => 0.05f, Defaults);

            foreach (var c in grid.AllCoords())
                Assert.Equal(DepthClass.Land, grid[c]);
        }

        [Fact]
        public void Height_above_sea_level_is_Land()
        {
            var grid = SmallGrid();
            ZoneClassifier.Classify(grid, (_, __) => 0.10f, Defaults);

            foreach (var c in grid.AllCoords())
                Assert.Equal(DepthClass.Land, grid[c]);
        }

        [Fact]
        public void Height_just_below_sea_level_is_Shallow()
        {
            var grid = SmallGrid();
            // 0.04 is below seaLevel (0.05) but above shallowThreshold (0.03)
            ZoneClassifier.Classify(grid, (_, __) => 0.04f, Defaults);

            foreach (var c in grid.AllCoords())
                Assert.Equal(DepthClass.Shallow, grid[c]);
        }

        [Fact]
        public void Height_at_shallow_threshold_is_Shallow()
        {
            var grid = SmallGrid();
            // Use the same arithmetic the classifier uses to avoid float mismatch:
            // shallowThreshold = SeaLevelBaseHeight - ShallowDepthBelowSea
            float threshold = Defaults.SeaLevelBaseHeight - Defaults.ShallowDepthBelowSea;
            ZoneClassifier.Classify(grid, (_, __) => threshold, Defaults);

            foreach (var c in grid.AllCoords())
                Assert.Equal(DepthClass.Shallow, grid[c]);
        }

        [Fact]
        public void Height_below_shallow_threshold_is_Deep()
        {
            var grid = SmallGrid();
            // 0.029 is below shallowThreshold (0.03)
            ZoneClassifier.Classify(grid, (_, __) => 0.029f, Defaults);

            foreach (var c in grid.AllCoords())
                Assert.Equal(DepthClass.Deep, grid[c]);
        }

        // ── 2. Full grid coverage ─────────────────────────────────────

        [Fact]
        public void Classify_assigns_every_zone()
        {
            var grid = SmallGrid();
            // Deep maps to enum value 2; default (unvisited) would be Land (0).
            // If any zone is skipped it stays Land, so asserting Deep catches that.
            ZoneClassifier.Classify(grid, (_, __) => -1f, Defaults);

            int count = 0;
            foreach (var c in grid.AllCoords())
            {
                Assert.Equal(DepthClass.Deep, grid[c]);
                count++;
            }

            Assert.Equal(grid.Size * grid.Size, count);
        }

        // ── 3. Determinism ────────────────────────────────────────────

        [Fact]
        public void Classify_is_deterministic()
        {
            // Sampler that produces a mix of all three classes by position
            Func<float, float, float> sampler = (wx, wz) =>
            {
                if (wx > 0)  return 0.10f;  // Land
                if (wz > 0)  return 0.04f;  // Shallow
                return 0.01f;               // Deep
            };

            var grid1 = SmallGrid();
            var grid2 = SmallGrid();

            ZoneClassifier.Classify(grid1, sampler, Defaults);
            ZoneClassifier.Classify(grid2, sampler, Defaults);

            foreach (var c in grid1.AllCoords())
                Assert.Equal(grid1[c], grid2[c]);
        }

        [Fact]
        public void Determinism_check_contains_all_three_classes()
        {
            // Verify the determinism sampler actually produces a mix,
            // so the test above isn't trivially passing with one class.
            Func<float, float, float> sampler = (wx, wz) =>
            {
                if (wx > 0)  return 0.10f;
                if (wz > 0)  return 0.04f;
                return 0.01f;
            };

            var grid = SmallGrid();
            ZoneClassifier.Classify(grid, sampler, Defaults);

            bool hasLand = false, hasShallow = false, hasDeep = false;
            foreach (var c in grid.AllCoords())
            {
                switch (grid[c])
                {
                    case DepthClass.Land:    hasLand    = true; break;
                    case DepthClass.Shallow: hasShallow = true; break;
                    case DepthClass.Deep:    hasDeep    = true; break;
                }
            }

            Assert.True(hasLand,    "Expected at least one Land zone");
            Assert.True(hasShallow, "Expected at least one Shallow zone");
            Assert.True(hasDeep,    "Expected at least one Deep zone");
        }
    }
}
