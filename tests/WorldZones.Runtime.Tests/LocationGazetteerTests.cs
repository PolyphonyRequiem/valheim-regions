using System.IO;
using System.Linq;
using WorldZones.Runtime;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// End-to-end test of the LOCATION GAZETTEER join: WorldZonesRuntime.Build with a PortLocationSource
    /// binds the offline-computed location plan to regions, tags placement status, and groups unique
    /// candidates. Runs the real Niflheim world (seed ForTheWort) + the checked-in catalogue.
    /// See docs/design/location-gazetteer-api.md (provisional).
    /// </summary>
    public class LocationGazetteerTests : IClassFixture<NiflheimLocationFixture>
    {
        private readonly RegionWorld world;
        public LocationGazetteerTests(NiflheimLocationFixture fixture) => this.world = fixture.World;

        [Fact]
        public void Build_WithoutLocationSource_LeavesLocationCollectionsEmpty()
        {
            // Regression guard: the location join is opt-in; a regions-only build is unchanged.
            var w = WorldZonesRuntime.Build(PortWorldSampler.FromSeed(NiflheimLocationFixture.Seed));
            Assert.Empty(w.AllLocations);
            Assert.Empty(w.CandidateGroups);
            Assert.All(w.Regions, r => Assert.Empty(r.Locations));
        }

        [Fact]
        public void Build_WithLocationSource_PopulatesAndJoinsLocations()
        {
            // The port computes ~6,100 base-catalogue locations for Niflheim.
            Assert.True(world.AllLocations.Count > 5000,
                $"expected >5000 locations, got {world.AllLocations.Count}");

            // Every location with a region key resolves to a real region, and appears in that region's slice.
            int joined = world.AllLocations.Count(l => l.RegionKey != null);
            Assert.True(joined > 0, "no locations joined to any region");

            // Per-region slices reconcile with the flat set (every regioned location is in exactly its region).
            int sliceTotal = world.Regions.Sum(r => r.Locations.Count);
            Assert.Equal(joined, sliceTotal);

            // Each region's locations actually carry that region's key.
            foreach (var r in world.Regions)
                Assert.All(r.Locations, l => Assert.Equal(r.RegionKey, l.RegionKey));
        }

        [Fact]
        public void StartTemple_IsRegistered_AndInAMeadowsRegionNearSpawn()
        {
            var temple = world.AllLocations.SingleOrDefault(l => l.PrefabName == "StartTemple");
            Assert.NotNull(temple);

            // StartTemple is a normal (non-unique) location -> Registered offline.
            Assert.Equal(PlacementStatus.Registered, temple.Status);
            Assert.False(temple.IsUnique);

            // It sits near world origin (the real Niflheim spawn ~ (134, 1)).
            Assert.True(System.Math.Abs(temple.X) < 500 && System.Math.Abs(temple.Z) < 500,
                $"temple at ({temple.X},{temple.Z}) not near spawn");
            // And it joins to a region (the spawn meadows).
            Assert.NotNull(temple.RegionKey);
        }

        [Fact]
        public void Haldor_IsAUniqueCandidateGroup_Unresolved_Offline()
        {
            // Vendor_BlackForest (Haldor) is the one enabled unique in the base catalogue: N candidate
            // sites, exactly one realizes at runtime. Offline it must be an UNRESOLVED candidate group.
            var haldor = world.CandidateGroups.SingleOrDefault(g => g.PrefabName == "Vendor_BlackForest");
            Assert.NotNull(haldor);
            Assert.True(haldor.CandidateCount > 1,
                $"expected multiple Haldor candidate sites, got {haldor.CandidateCount}");
            Assert.False(haldor.Resolved, "offline build must not resolve a unique (the seed doesn't pick the winner)");
            Assert.Null(haldor.RealizedSite);

            // Every Haldor site is a Candidate, carries the group key, and reports IsUnique.
            var sites = world.AllLocations.Where(l => l.PrefabName == "Vendor_BlackForest").ToList();
            Assert.Equal(haldor.CandidateCount, sites.Count);
            Assert.All(sites, s =>
            {
                Assert.Equal(PlacementStatus.Candidate, s.Status);
                Assert.True(s.IsUnique);
                Assert.Equal("Vendor_BlackForest", s.CandidateGroupKey);
            });
        }

        [Fact]
        public void OfflineBuild_NeverReportsRealized()
        {
            // The offline source cannot know realization — no location should be Realized.
            Assert.DoesNotContain(world.AllLocations, l => l.Status == PlacementStatus.Realized);
        }
    }
}
