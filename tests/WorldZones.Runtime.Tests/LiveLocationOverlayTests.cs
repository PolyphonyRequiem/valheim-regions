using System.Linq;
using WorldZones.Runtime;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Tests for the LIVE REALIZATION OVERLAY (<see cref="LiveLocationOverlay"/>) — the push surface that
    /// lays the running world's STATE over the built PLAN. Driven by NotifyRealized (what the mod's
    /// ZoneSystem.PlaceLocations Harmony patch calls), verified Unity-free against the real Niflheim build.
    ///
    /// <para>These build their OWN world (not the shared fixture) because the overlay MUTATES placement
    /// status. Consolidated into two builds (normal-location lifecycle + unique resolution) to keep the
    /// heavy location-gen cost down.</para>
    /// </summary>
    public class LiveLocationOverlayTests
    {
        [Fact]
        public void NormalLocation_RealizationLifecycle()
        {
            var world = NiflheimLocationFixture.BuildFreshSmall();
            var overlay = new LiveLocationOverlay(world);
            Assert.Equal(0, overlay.RealizedCount);

            // --- flip status + fire once ---
            var target = world.AllLocations.First(l => !l.IsUnique);
            Assert.Equal(PlacementStatus.Registered, target.Status);

            int fired = 0;
            GazetteerLocation got = null;
            overlay.OnLocationRealized += l => { fired++; got = l; };

            var matched = overlay.NotifyRealized(target.PrefabName, target.X, target.Z);
            Assert.NotNull(matched);
            Assert.Equal(PlacementStatus.Realized, matched.Status);
            Assert.Equal(1, fired);
            Assert.Same(target, got);

            // --- idempotent: re-notify same site does not re-fire ---
            overlay.NotifyRealized(target.PrefabName, target.X, target.Z);
            Assert.Equal(1, fired);

            // --- no-op: unknown prefab + real prefab far from any site ---
            Assert.Null(overlay.NotifyRealized("NotARealPrefab", 0, 0));
            Assert.Null(overlay.NotifyRealized(target.PrefabName, 99999f, 99999f));
            Assert.Equal(1, fired);

            // --- count tracks distinct realizations ---
            foreach (var loc in world.AllLocations.Where(l => !l.IsUnique && l.Status != PlacementStatus.Realized).Take(4))
                overlay.NotifyRealized(loc.PrefabName, loc.X, loc.Z);
            Assert.Equal(5, overlay.RealizedCount); // the first + 4 more
        }

        [Fact]
        public void RealizingAUniqueCandidate_ResolvesItsGroup_Once()
        {
            var world = NiflheimLocationFixture.BuildFreshSmall();
            var overlay = new LiveLocationOverlay(world);

            var haldor = world.CandidateGroups.Single(g => g.PrefabName == "Vendor_BlackForest");
            Assert.False(haldor.Resolved);
            var winner = haldor.Candidates.First();

            int groupFired = 0;
            CandidateGroup resolvedGroup = null;
            overlay.OnUniqueResolved += g => { groupFired++; resolvedGroup = g; };

            overlay.NotifyRealized(winner.PrefabName, winner.X, winner.Z);

            Assert.True(haldor.Resolved);
            Assert.NotNull(haldor.RealizedSite);
            Assert.Equal(PlacementStatus.Realized, haldor.RealizedSite.Status);
            Assert.Equal(1, groupFired);
            Assert.Same(haldor, resolvedGroup);

            // Realizing a SECOND candidate of the same group does not re-resolve.
            var second = haldor.Candidates.Skip(1).FirstOrDefault();
            if (second != null) overlay.NotifyRealized(second.PrefabName, second.X, second.Z);
            Assert.Equal(1, groupFired);
        }
    }
}
