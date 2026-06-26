using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards the biome-aware SEEDING lever end-to-end through the runtime façade
    /// (<see cref="RegionSeedingFieldBuilder"/> + <see cref="RegionBuildOptions.UseBiomeAwareSeeding"/>).
    /// The builder lives in Runtime because it reads biomes; the field it produces is opaque to the
    /// biome-blind topology library. See docs/design/region-borders.md ("the SEEDING lever").
    /// </summary>
    public class RegionSeedingFieldTests
    {
        private const string NiflheimSeed = "ForTheWort";

        // A synthetic sampler with a 4-biome quadrant world: which quadrant a point is in picks the
        // biome, so the centre is a maximally-diverse junction and each corner is a mono-biome interior.
        private sealed class QuadrantSampler : IWorldSampler
        {
            public string WorldId => "quad";
            public float GetHeight(float x, float z) => 40f; // all land (sea level 30)
            public BiomeType GetBiome(float x, float z)
            {
                bool east = x >= 0, north = z >= 0;
                if (east && north) return BiomeType.Meadows;
                if (!east && north) return BiomeType.BlackForest;
                if (east && !north) return BiomeType.Plains;
                return BiomeType.Swamp;
            }
        }

        [Fact]
        public void Field_FlagsTheJunction_AndNotAMonoBiomeInterior()
        {
            var sampler = new QuadrantSampler();
            var grid = new ZoneGrid(2048f);
            ZoneClassifier.Classify(grid, new SamplerProvider(sampler));
            SeedingField field = RegionSeedingFieldBuilder.Build(sampler, grid,
                new RegionSeedingFieldOptions { Aggressiveness = 1.0, NeighbourhoodRadius = 2 });

            int min = grid.MinIndex;
            // Cell at the 4-biome junction (world origin) should read high diversity.
            int cgx = 0 - min, cgy = 0 - min;
            double junction = field.Weight(cgx, cgy);

            // A cell deep in one quadrant (far corner) should read ~0 (mono-biome neighbourhood).
            int fgx = (grid.MaxIndex - 1) - min, fgy = (grid.MaxIndex - 1) - min;
            double interior = field.Weight(fgx, fgy);

            Assert.True(junction > interior,
                $"junction weight {junction} should exceed interior {interior}");
            Assert.True(junction > 0.3, $"junction should read diverse (got {junction})");
            Assert.True(interior < 0.1, $"mono-biome interior should read ~0 (got {interior})");
        }

        [Fact]
        public void BiomeAwareSeeding_OnRealNiflheim_AddsRegions_AndDoesNotBreakInvariants()
        {
            // Baseline (lever off) vs lever on: more seeds → more regions, but the rich model must stay
            // well-formed (every region has land, biome counts still sum to sampled land).
            RegionWorld baseline = WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions { IncludeInlandWater = true, UseFeatureAwareBorders = true });

            RegionWorld levered = WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions
                {
                    IncludeInlandWater = true,
                    UseFeatureAwareBorders = true,
                    UseBiomeAwareSeeding = true,
                    SeedingFieldOptions = new RegionSeedingFieldOptions { Aggressiveness = 2.0 },
                });

            Assert.True(levered.Regions.Count > baseline.Regions.Count,
                $"seeding lever should add regions ({levered.Regions.Count} vs {baseline.Regions.Count})");

            foreach (var r in levered.Regions)
            {
                Assert.True(r.SampledLandZones > 0);
                Assert.Equal(r.SampledLandZones, r.BiomeZoneCounts.Values.Sum());
            }
        }

        [Fact]
        public void BiomeAwareSeeding_RaisesMeanDominantBiomeFraction_OnRealNiflheim()
        {
            // The headline composition claim: turning the lever up raises the mean dominant-biome
            // fraction (purer regions) — the thing 3 routing attempts could not move. Modest but real.
            double MeanDom(RegionWorld w)
            {
                double sum = 0; int n = 0;
                foreach (var r in w.Regions)
                {
                    int land = r.SampledLandZones;
                    if (land <= 0) continue;
                    int dc = r.BiomeZoneCounts.TryGetValue(r.DominantBiome, out int c) ? c : 0;
                    sum += (double)dc / land; n++;
                }
                return n > 0 ? sum / n : 0;
            }

            RegionWorld baseline = WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions { IncludeInlandWater = true, UseFeatureAwareBorders = true });
            RegionWorld levered = WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions
                {
                    IncludeInlandWater = true,
                    UseFeatureAwareBorders = true,
                    UseBiomeAwareSeeding = true,
                    SeedingFieldOptions = new RegionSeedingFieldOptions { Aggressiveness = 4.0 },
                });

            Assert.True(MeanDom(levered) > MeanDom(baseline),
                $"lever should raise mean dominant-biome fraction ({MeanDom(levered):F4} vs {MeanDom(baseline):F4})");
        }

        // Minimal IWorldDataProvider over a sampler for the synthetic-grid classify step.
        private sealed class SamplerProvider : IWorldDataProvider
        {
            private readonly IWorldSampler s;
            public SamplerProvider(IWorldSampler s) { this.s = s; }
            public string WorldId => this.s.WorldId;
            public float WaterLevel => ZoneClassifier.DefaultWaterLevel;
            public float GetTerrainHeight(float x, float z) => this.s.GetHeight(x, z);
        }
    }
}
