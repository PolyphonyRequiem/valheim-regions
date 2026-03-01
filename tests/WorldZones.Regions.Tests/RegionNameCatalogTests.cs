using Xunit;
using WorldZones.Regions;
using System.Collections.Generic;

namespace WorldZones.Regions.Tests
{
    public class RegionNameCatalogTests
    {
        [Fact]
        public void Catalog_has_exactly_500_names()
        {
            Assert.Equal(500, RegionNameCatalog.Count);
            Assert.Equal(500, RegionNameCatalog.Names.Length);
        }

        [Fact]
        public void Catalog_entries_are_non_empty_and_under_40_chars()
        {
            foreach (string name in RegionNameCatalog.Names)
            {
                Assert.False(string.IsNullOrWhiteSpace(name));
                Assert.True(name.Length <= 40, $"Name exceeds 40 chars: {name}");
            }
        }

        [Fact]
        public void Deterministic_name_is_stable_for_same_input()
        {
            string worldId = "HHcLC5acQt";
            int regionId = 91;

            string first = RegionGuidNameService.CreateDeterministicName(worldId, regionId);
            string second = RegionGuidNameService.CreateDeterministicName(worldId, regionId);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Deterministic_name_is_always_from_catalog()
        {
            string worldId = "VandradIsRadrad";

            for (int regionId = 0; regionId < 2000; regionId++)
            {
                string name = RegionGuidNameService.CreateDeterministicName(worldId, regionId);
                Assert.Contains(name, RegionNameCatalog.Names);
            }
        }

        [Fact]
        public void Adjacent_region_ids_are_well_distributed_in_catalog()
        {
            string worldId = "HHcLC5acQt";
            var names = new HashSet<string>();

            for (int regionId = 0; regionId < 30; regionId++)
            {
                names.Add(RegionGuidNameService.CreateDeterministicName(worldId, regionId));
            }

            Assert.True(names.Count >= 20, $"Expected at least 20 unique names for first 30 region ids, got {names.Count}");
        }
    }
}