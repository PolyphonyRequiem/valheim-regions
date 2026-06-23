#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using Xunit;
using WorldZones.Cli;
using WorldZones.WorldGen;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Tests for the REAL extracted vegetation catalogue loader (VegetationCatalogue.Load) and its
    /// end-to-end wiring into VegetationModel.ModelZone. These prove Wall 2 is down: the catalogue is
    /// real Unity asset data (AssetRipper export of ZoneSystem.m_vegetation), it parses into the
    /// model's config type, the biome NAME->BiomeType mapping is correct, and feeding it to ModelZone
    /// produces NON-empty deterministic counts (not the honest-empty default).
    ///
    /// net8.0-only: the loader lives in the CLI (uses System.Text.Json, which net472 lacks).
    /// </summary>
    public class VegetationCatalogueTests
    {
        // Walk up from the test bin dir to the repo root, where data/ lives.
        static string CataloguePath()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "data", "valheim_vegetation_catalogue.json");
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Could not locate data/valheim_vegetation_catalogue.json from " + AppContext.BaseDirectory);
        }

        [Fact]
        public void Load_ParsesRealCatalogue_98Configs_8Resource()
        {
            var cat = VegetationCatalogue.Load(CataloguePath());
            Assert.Equal(98, cat.Count);
            Assert.Equal(8, cat.Count(c => c.IsResource));
        }

        [Fact]
        public void Load_BiomeMapping_CopperIsBlackForest()
        {
            var cat = VegetationCatalogue.Load(CataloguePath());
            var copper = cat.First(c => c.PrefabName == "rock4_copper");
            // copper spawns in BlackForest only — the name->BiomeType mapping must set that bit
            Assert.True((copper.Biome & BiomeType.BlackForest) != 0, "copper must map to BlackForest");
            Assert.True(copper.IsResource);
        }

        [Fact]
        public void Load_BiomeMapping_SilverIsMountainHighAltitude()
        {
            var cat = VegetationCatalogue.Load(CataloguePath());
            var silver = cat.First(c => c.PrefabName == "silvervein");
            Assert.True((silver.Biome & BiomeType.Mountain) != 0, "silver must map to Mountain");
            Assert.True(silver.MinAltitude >= 100f, "silver is high-mountain-only ore (alt >= 100m)");
        }

        [Fact]
        public void ModelZone_WithRealCatalogue_ProducesNonEmptyCounts()
        {
            var cat = VegetationCatalogue.Load(CataloguePath());
            // Flat BlackForest at +10m altitude so copper/tin can place — proves end-to-end wiring,
            // not the honest-empty default.
            Func<float, float, float> height = (x, z) => 40f;        // 40 - 30 sea = +10m
            Func<float, float, BiomeType> biome = (x, z) => BiomeType.BlackForest;

            // scan a handful of zones — determinism means each is fixed, but across zones at least one
            // must yield a placement for a BlackForest config.
            bool any = false;
            for (int zx = 0; zx < 20 && !any; zx++)
                for (int zy = 0; zy < 20 && !any; zy++)
                {
                    var counts = VegetationModel.ModelZone(
                        worldSeed: "ForTheWort".GetStableHashCode(), zoneX: zx, zoneY: zy,
                        catalogue: cat, height: height, biomeAt: biome);
                    if (counts.Count > 0) any = true;
                }
            Assert.True(any, "loaded catalogue must produce non-empty counts for BlackForest zones");
        }

        [Fact]
        public void ModelZone_WithRealCatalogue_IsDeterministic()
        {
            var cat = VegetationCatalogue.Load(CataloguePath());
            Func<float, float, float> height = (x, z) => 40f;
            Func<float, float, BiomeType> biome = (x, z) => BiomeType.BlackForest;
            int seed = "ForTheWort".GetStableHashCode();

            var a = VegetationModel.ModelZone(seed, 3, 7, cat, height, biome);
            var b = VegetationModel.ModelZone(seed, 3, 7, cat, height, biome);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].PrefabName, b[i].PrefabName);
                Assert.Equal(a[i].EstimatedCount, b[i].EstimatedCount);
            }
        }
    }
}
#endif
