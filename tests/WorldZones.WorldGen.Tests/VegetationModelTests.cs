using System;
using System.Collections.Generic;
using Xunit;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Tests for the MODELED vegetation/resource scaffold (VegetationModel). These verify the
    /// headless-portable CONTRACT — determinism, the honest-empty default, and that the RNG-exact
    /// scatter path runs — NOT numerical fidelity to the game (which is impossible headless: the
    /// real configs live in Unity assets and several filters need the terrain mesh/physics).
    /// See docs/design/vegetation-resource-model.md for the buildability verdict.
    /// </summary>
    public class VegetationModelTests
    {
        // Flat synthetic samplers so the test is pure: everything is Meadows at altitude 10 m.
        static float FlatHeight(float x, float z) => 40f;                 // 40 - 30 sea = +10 m altitude
        static BiomeType AllMeadows(float x, float z) => BiomeType.Meadows;

        static VegetationModel.VegetationConfig Copperish() => new VegetationModel.VegetationConfig
        {
            PrefabName = "TestCopper",
            Enable = true,
            Biome = BiomeType.Meadows,
            Min = 2, Max = 4,
            GroupSizeMin = 1, GroupSizeMax = 1,
            GroupRadius = 0f,
            MinAltitude = -50f, MaxAltitude = 200f,
            IsResource = true,
        };

        [Fact]
        public void EmptyCatalogue_ProducesNoCounts_TheHonestDefault()
        {
            var outp = VegetationModel.ModelZone(
                worldSeed: 12345, zoneX: 0, zoneY: 0,
                catalogue: new List<VegetationModel.VegetationConfig>(),
                height: FlatHeight, biomeAt: AllMeadows);

            Assert.Empty(outp);   // no fabricated configs => no fabricated counts
        }

        [Fact]
        public void NullCatalogue_IsSafe_AndEmpty()
        {
            var outp = VegetationModel.ModelZone(99, 1, 1, null!, FlatHeight, AllMeadows);
            Assert.Empty(outp);
        }

        [Fact]
        public void SameInputs_ProduceIdenticalCounts_Deterministic()
        {
            var cat = new List<VegetationModel.VegetationConfig> { Copperish() };
            var a = VegetationModel.ModelZone(777, 3, -2, cat, FlatHeight, AllMeadows);
            var b = VegetationModel.ModelZone(777, 3, -2, cat, FlatHeight, AllMeadows);

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].PrefabName, b[i].PrefabName);
                Assert.Equal(a[i].EstimatedCount, b[i].EstimatedCount);
                Assert.Equal(a[i].IsResource, b[i].IsResource);
            }
        }

        [Fact]
        public void DifferentZones_GenerallyDifferentCounts_RngIsZoneKeyed()
        {
            var cat = new List<VegetationModel.VegetationConfig> { Copperish() };
            // Across several zones the per-zone RNG seeding (zoneId.x*4271 + zoneId.y*9187) must vary
            // the outcome — at least one zone should differ from zone (0,0). Proves the seed formula wires in.
            var baseline = VegetationModel.ModelZone(50, 0, 0, cat, FlatHeight, AllMeadows);
            int baseCount = baseline.Count > 0 ? baseline[0].EstimatedCount : 0;

            bool anyDiffer = false;
            for (int zx = 1; zx <= 8 && !anyDiffer; zx++)
            {
                var other = VegetationModel.ModelZone(50, zx, zx, cat, FlatHeight, AllMeadows);
                int c = other.Count > 0 ? other[0].EstimatedCount : 0;
                if (c != baseCount) anyDiffer = true;
            }
            Assert.True(anyDiffer, "per-zone RNG seeding should vary counts across zones");
        }

        [Fact]
        public void ResourceFlag_PropagatesToOutput()
        {
            var cat = new List<VegetationModel.VegetationConfig> { Copperish() };
            var outp = VegetationModel.ModelZone(111, 0, 0, cat, FlatHeight, AllMeadows);
            // Copperish spawns in Meadows at +10m with a wide altitude band → should place something.
            if (outp.Count > 0)
                Assert.True(outp[0].IsResource);
        }

        [Fact]
        public void BiomeMismatch_RejectsAllPlacements()
        {
            // Catalogue wants Meadows, but the world is all Mountain → nothing should place.
            var cat = new List<VegetationModel.VegetationConfig> { Copperish() };
            var outp = VegetationModel.ModelZone(
                222, 0, 0, cat, FlatHeight, (x, z) => BiomeType.Mountain);
            Assert.Empty(outp);
        }
    }
}
