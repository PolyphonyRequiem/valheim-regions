using System.IO;
using System.Linq;
using WorldZones.Runtime;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Shared, built-once Niflheim world WITH locations, for the location-gazetteer + overlay tests.
    /// Building the location plan runs the full ~6,100-placement port (~30s+), so an xUnit class fixture
    /// shares one instance across a test class instead of rebuilding per test. Tests that MUTATE the
    /// world (the overlay flips PlacementStatus) must build their own — use <see cref="BuildFresh"/>.
    /// </summary>
    public sealed class NiflheimLocationFixture
    {
        public const string Seed = "ForTheWort";

        public RegionWorld World { get; }

        public NiflheimLocationFixture()
        {
            World = BuildFresh();
        }

        public static string CataloguePath()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                var c = Path.Combine(dir.FullName, "data", "valheim_locations_catalogue.json");
                if (File.Exists(c)) return c;
                dir = dir.Parent;
            }
            throw new FileNotFoundException("catalogue not found from " + System.AppContext.BaseDirectory);
        }

        /// <summary>A fresh, independently-mutable world with locations (for overlay tests that flip status).</summary>
        public static RegionWorld BuildFresh()
        {
            var catalogue = LocationCatalogue.Load(CataloguePath());
            return WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(Seed),
                new RegionBuildOptions
                {
                    IncludeInlandWater = true,
                    LocationSource = PortLocationSource.FromSeed(Seed, catalogue),
                });
        }

        /// <summary>
        /// A fresh world built from a SMALL catalogue subset — same code paths, ~10x faster than the full
        /// ~11k-placement catalogue. For tests that exercise the overlay/event MECHANICS (status flips,
        /// candidate resolution) rather than real placement coverage. Keeps a couple of high-quantity
        /// normal types + the Haldor unique, which is all the overlay tests need.
        /// </summary>
        public static RegionWorld BuildFreshSmall()
        {
            var full = LocationCatalogue.Load(CataloguePath());
            var keep = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "InfestedTree01", "Greydwarf_camp1", "Vendor_BlackForest",
            };
            var subset = full.Where(c => keep.Contains(c.PrefabName)).ToList();
            return WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(Seed),
                new RegionBuildOptions
                {
                    IncludeInlandWater = true,
                    LocationSource = PortLocationSource.FromSeed(Seed, subset),
                });
        }
    }
}
