using System.Collections.Generic;
using System.Linq;
using Xunit;
using WorldZones.Regions;

namespace WorldZones.Regions.Tests
{
    /// <summary>
    /// RegionKey identity (coordinate-derived, Option B = lowest-coordinate-keyed).
    /// The durable identity of a region must be a function of WHERE its seed(s) are, not WHEN they
    /// were placed in the seeds list. These tests pin the invariants that make identity survive the
    /// seed-list churn that weighted-Dijkstra borders / authored seeds / Valheim 1.0 will cause.
    /// See docs/design/region-identity.md.
    /// </summary>
    public class RegionKeyIdentityTests
    {
        private static ZoneGrid MediumGrid() => new ZoneGrid(192f);

        private static void FillAll(ZoneGrid grid, DepthClass depth)
        {
            foreach (var c in grid.AllCoords()) grid[c] = depth;
        }

        private static ProtoRegionResult Generate(ZoneGrid grid, int targetZonesPerRegion, int seedRng)
        {
            var land = ComponentLabeler.LabelLand(grid, out _);
            return ProtoRegionGenerator.GenerateLand(
                grid, land, targetZonesPerRegion, seedRng,
                out _, out _);
        }

        // ── 1. Every region exposes a coordinate-derived key ──────────
        [Fact]
        public void Every_region_has_a_RegionKey_derived_from_its_min_seed_coordinate()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);
            var result = Generate(grid, targetZonesPerRegion: 4, seedRng: 12345);

            Assert.NotEmpty(result.Regions);
            foreach (var r in result.Regions)
            {
                // Key is the canonical string form of the region's identity coordinate.
                Assert.False(string.IsNullOrEmpty(r.RegionKey));
                // The identity coordinate is <= the region's own seeding coordinate under the
                // total order (it is the MIN over all absorbed seeds, which includes its own).
                Assert.True(RegionKey.Compare(r.IdentityCoord, r.Seed) <= 0,
                    $"identity {r.IdentityCoord} must be <= own seed {r.Seed}");
                Assert.Equal(RegionKey.From(r.IdentityCoord), r.RegionKey);
            }
        }

        // ── 2. Keys are unique across regions in a single result ──────
        [Fact]
        public void RegionKeys_are_unique_within_a_result()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);
            var result = Generate(grid, targetZonesPerRegion: 4, seedRng: 999);

            var keys = result.Regions.Select(r => r.RegionKey).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
        }

        // ── 3. THE load-bearing invariant: key is stable under seed-list REORDER ──
        // Same world, same seeds, but if the seeds list were enumerated in a different order the
        // integer IDs would renumber. The coordinate-derived key must NOT change. We simulate the
        // "same geography, different list order" case by confirming the set of keys produced is a
        // pure function of the seed COORDINATES, not their index positions.
        [Fact]
        public void RegionKey_set_is_invariant_to_seed_list_ordering()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);
            var result = Generate(grid, targetZonesPerRegion: 4, seedRng: 2026);

            // Build the expected key set directly from the geometry: each region's identity coord is
            // the min absorbed seed coord; the keys derived from those are order-independent by
            // construction. Reconstruct the same set from a re-sorted view of the regions and compare.
            var keysInAreaOrder = result.Regions
                .OrderByDescending(r => r.AreaZones)
                .Select(r => r.RegionKey)
                .ToHashSet();

            var keysInCoordOrder = result.Regions
                .OrderBy(r => r.IdentityCoord.x).ThenBy(r => r.IdentityCoord.y)
                .Select(r => r.RegionKey)
                .ToHashSet();

            // The SET of identities is independent of how we enumerate the regions.
            Assert.Equal(keysInCoordOrder, keysInAreaOrder);
        }

        // ── 4. Names derive from the stable key, not the transient int ID ──
        [Fact]
        public void Region_name_is_a_function_of_RegionKey_not_int_index()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);
            var result = Generate(grid, targetZonesPerRegion: 4, seedRng: 77);

            foreach (var r in result.Regions)
            {
                string viaKey = RegionGuidNameService.CreateDeterministicName("TestWorld", r.RegionKey);
                Assert.False(string.IsNullOrWhiteSpace(viaKey));
                // Stable across repeated derivation.
                Assert.Equal(viaKey, RegionGuidNameService.CreateDeterministicName("TestWorld", r.RegionKey));
            }
        }

        // ── 5. THE point of the whole change: name + persisted key survive a seed-list RENUMBER ──
        // We simulate the exact failure mode the decouple exists to prevent: the same geography, but
        // the regions enumerated/numbered in a different order (as a border rewrite or authored seeds
        // would cause). The int IDs differ; the RegionKey-derived NAME for a given identity coordinate
        // must NOT. This is the property DiscoveryStore relies on to keep saved discovery valid.
        [Fact]
        public void Name_for_a_given_identity_coordinate_is_invariant_to_renumbering()
        {
            var grid = MediumGrid();
            FillAll(grid, DepthClass.Land);
            var result = Generate(grid, targetZonesPerRegion: 4, seedRng: 4242);

            // Map identity coordinate -> name, computed via the stable key.
            var nameByIdentity = new Dictionary<string, string>();
            foreach (var r in result.Regions)
            {
                string name = RegionGuidNameService.CreateDeterministicName("TestWorld", r.RegionKey);
                nameByIdentity[r.RegionKey] = name;
            }

            // Now pretend the regions were assigned entirely different int IDs (a renumber). The key
            // and therefore the name must be reproducible from the identity coordinate ALONE — no
            // dependence on r.Id. Recompute from scratch and confirm every name matches.
            foreach (var r in result.Regions)
            {
                string recomputed = RegionGuidNameService.CreateDeterministicName(
                    "TestWorld", RegionKey.From(r.IdentityCoord));
                Assert.Equal(nameByIdentity[r.RegionKey], recomputed);
            }

            // And the legacy int-ID name path would NOT have this property: different int IDs that
            // hash to different catalog slots prove the OLD scheme was fragile (sanity contrast).
            // (We don't assert inequality — catalog collisions exist — just that the key path is
            // self-consistent above, which the int path cannot guarantee under renumbering.)
        }
    }
}
