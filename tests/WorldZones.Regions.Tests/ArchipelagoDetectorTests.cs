using System.Collections.Generic;
using Xunit;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Regions.Tests
{
    public class ArchipelagoDetectorTests
    {
        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a LandComponent with the given ID and zone count.
        /// Zone coordinates are synthetic (sequential) — only count matters.
        /// </summary>
        private static LandComponent MakeLand(int id, int zoneCount)
        {
            var lc = new LandComponent(id);
            for (int i = 0; i < zoneCount; i++)
                lc.Zones.Add(new Vector2i(id * 1000 + i, 0));
            return lc;
        }

        /// <summary>
        /// Creates a ShelfComponent containing the specified land component IDs.
        /// </summary>
        private static ShelfComponent MakeShelf(int id, params int[] landIds)
        {
            var sc = new ShelfComponent(id);
            sc.ContainedLandComponentIds.AddRange(landIds);
            return sc;
        }

        // ── 1. Shelf with multiple small islands → flagged ────────────

        [Fact]
        public void Three_equal_islands_flagged_as_archipelago()
        {
            // 3 islands, each 10 zones — no dominant, meets min count
            var lands = new List<LandComponent>
            {
                MakeLand(0, 10),
                MakeLand(1, 10),
                MakeLand(2, 10)
            };
            var shelves = new List<ShelfComponent> { MakeShelf(0, 0, 1, 2) };

            var options = new ArchipelagoDetectorOptions
            {
                MinLandComponents = 3,
                MaxDominantShare  = 0.6f
            };

            var results = ArchipelagoDetector.Detect(shelves, lands, options);

            Assert.Single(results);
            var c = results[0];
            Assert.Equal(0, c.ShelfComponentId);
            Assert.Equal(3, c.LandComponentIds.Count);
            Assert.Equal(30, c.TotalLandZoneCount);
            Assert.True(c.DominantLandShare <= 0.6f);
        }

        // ── 2. Dominant landmass prevents classification ──────────────

        [Fact]
        public void One_dominant_plus_two_tiny_not_flagged()
        {
            // 1 large island (100 zones) + 2 tiny (5 each)
            // Dominant share = 100/110 ≈ 0.91 → exceeds 0.6 threshold
            var lands = new List<LandComponent>
            {
                MakeLand(0, 100),
                MakeLand(1, 5),
                MakeLand(2, 5)
            };
            var shelves = new List<ShelfComponent> { MakeShelf(0, 0, 1, 2) };

            var options = new ArchipelagoDetectorOptions
            {
                MinLandComponents = 3,
                MaxDominantShare  = 0.6f
            };

            var results = ArchipelagoDetector.Detect(shelves, lands, options);

            Assert.Empty(results);
        }

        // ── 3. Shelf with 1 land component → not flagged ──────────────

        [Fact]
        public void Single_land_component_not_flagged()
        {
            var lands = new List<LandComponent> { MakeLand(0, 50) };
            var shelves = new List<ShelfComponent> { MakeShelf(0, 0) };

            var results = ArchipelagoDetector.Detect(shelves, lands);

            Assert.Empty(results);
        }

        // ── 4. Below minimum count → not flagged ──────────────────────

        [Fact]
        public void Two_islands_below_default_minimum_not_flagged()
        {
            var lands = new List<LandComponent>
            {
                MakeLand(0, 10),
                MakeLand(1, 10)
            };
            var shelves = new List<ShelfComponent> { MakeShelf(0, 0, 1) };

            var results = ArchipelagoDetector.Detect(shelves, lands);

            Assert.Empty(results);
        }

        // ── 5. Multiple shelves — only qualifying ones flagged ────────

        [Fact]
        public void Only_qualifying_shelves_flagged()
        {
            var lands = new List<LandComponent>
            {
                MakeLand(0, 10),   // shelf 0: archipelago
                MakeLand(1, 10),
                MakeLand(2, 10),
                MakeLand(3, 200),  // shelf 1: single continent
            };
            var shelves = new List<ShelfComponent>
            {
                MakeShelf(0, 0, 1, 2),  // 3 equal islands
                MakeShelf(1, 3)          // single landmass
            };

            var results = ArchipelagoDetector.Detect(shelves, lands);

            Assert.Single(results);
            Assert.Equal(0, results[0].ShelfComponentId);
        }

        // ── 6. Dominant share at boundary ─────────────────────────────

        [Fact]
        public void Dominant_share_exactly_at_threshold_is_flagged()
        {
            // 3 islands: 60, 20, 20 → dominant share = 60/100 = 0.6
            // MaxDominantShare = 0.6 → share is NOT > threshold → flagged
            var lands = new List<LandComponent>
            {
                MakeLand(0, 60),
                MakeLand(1, 20),
                MakeLand(2, 20)
            };
            var shelves = new List<ShelfComponent> { MakeShelf(0, 0, 1, 2) };

            var options = new ArchipelagoDetectorOptions
            {
                MinLandComponents = 3,
                MaxDominantShare  = 0.6f
            };

            var results = ArchipelagoDetector.Detect(shelves, lands, options);

            Assert.Single(results);
            Assert.Equal(0.6f, results[0].DominantLandShare);
        }

        [Fact]
        public void Dominant_share_just_above_threshold_not_flagged()
        {
            // 3 islands: 61, 20, 19 → dominant share = 61/100 = 0.61 > 0.6
            var lands = new List<LandComponent>
            {
                MakeLand(0, 61),
                MakeLand(1, 20),
                MakeLand(2, 19)
            };
            var shelves = new List<ShelfComponent> { MakeShelf(0, 0, 1, 2) };

            var options = new ArchipelagoDetectorOptions
            {
                MinLandComponents = 3,
                MaxDominantShare  = 0.6f
            };

            var results = ArchipelagoDetector.Detect(shelves, lands, options);

            Assert.Empty(results);
        }

        // ── 7. Determinism ────────────────────────────────────────────

        [Fact]
        public void Detection_is_deterministic()
        {
            var lands = new List<LandComponent>
            {
                MakeLand(0, 10),
                MakeLand(1, 10),
                MakeLand(2, 10),
                MakeLand(3, 15),
                MakeLand(4, 15),
                MakeLand(5, 15)
            };
            var shelves = new List<ShelfComponent>
            {
                MakeShelf(0, 0, 1, 2),
                MakeShelf(1, 3, 4, 5)
            };

            var r1 = ArchipelagoDetector.Detect(shelves, lands);
            var r2 = ArchipelagoDetector.Detect(shelves, lands);

            Assert.Equal(r1.Count, r2.Count);
            for (int i = 0; i < r1.Count; i++)
            {
                Assert.Equal(r1[i].ShelfComponentId, r2[i].ShelfComponentId);
                Assert.Equal(r1[i].TotalLandZoneCount, r2[i].TotalLandZoneCount);
                Assert.Equal(r1[i].DominantLandShare, r2[i].DominantLandShare);
            }
        }

        // ── 8. Empty input ────────────────────────────────────────────

        [Fact]
        public void Empty_shelves_returns_empty()
        {
            var results = ArchipelagoDetector.Detect(
                new List<ShelfComponent>(),
                new List<LandComponent>());

            Assert.Empty(results);
        }
    }
}
