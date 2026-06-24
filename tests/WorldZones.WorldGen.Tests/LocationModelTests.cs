using System.Linq;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Regression locks for the offline location port (LocationModel.Generate). These pin the two facts
    /// proven bit-exact against the real Niflheim .db (seed ForTheWort, worldVersion 37) on 2026-06-24,
    /// so a future change to the RNG draw order, the filter chain, or GetForestFactor can't silently
    /// break them. Full validation lives in WorldZones.Cli's `locations`/`probe` harness + the real .db;
    /// these are the cheap, oracle-free invariants. See docs/design/location-port.md.
    /// </summary>
    public class LocationModelTests
    {
        const string Seed = "ForTheWort";

        /// <summary>
        /// The spawn temple (StartTemple) is centerFirst + InForest. It exposed the GetForestFactor
        /// Fbm *2-1 bug (placed 0/1 until fixed). With the game's raw-sum Fbm it places at exactly
        /// (134.16, 0.97) — the real Niflheim spawn. This single test guards the forest-factor fix,
        /// the centerFirst spiral, and the terrain-delta gate together.
        /// </summary>
        [Fact]
        public void StartTemple_PlacesAtRealSpawn_BitExact()
        {
            var gen = new WorldGenerator(Seed);
            int worldSeed = Seed.GetStableHashCode();

            var startTemple = new LocationModel.LocationConfig
            {
                PrefabName = "StartTemple",
                Enable = true, Quantity = 1, Prioritized = true, CenterFirst = true,
                Biome = BiomeType.Meadows, BiomeArea = 2,    // Median
                ExteriorRadius = 25f, MaxTerrainDelta = 3f,
                MinAltitude = 3f, MaxAltitude = 1000f,
                InForest = true, ForestTresholdMin = 1f, ForestTresholdMax = 5f,
                MinDistance = 0f, MaxDistance = 10000f,
            };

            var placed = LocationModel.Generate(worldSeed, gen,
                new[] { startTemple }, InsideUnitCircleStrategy.PolarRadiusFirst);

            Assert.Single(placed);
            var t = placed[0];
            Assert.Equal("StartTemple", t.PrefabName);
            // Real Niflheim spawn temple: (134.1628875732422, 0.9715867042541504). Bit-exact within 0.5 m.
            Assert.True(System.Math.Abs(t.X - 134.1628875732422f) < 0.5f, $"temple X was {t.X}");
            Assert.True(System.Math.Abs(t.Z - 0.9715867042541504f) < 0.5f, $"temple Z was {t.Z}");
        }

        /// <summary>
        /// InfestedTree01 (q=700, Swamp, the highest-quantity base type) fills its full quota — the
        /// accept/reject loop and quota termination both work. Count fidelity is the substrate-relevant
        /// property; this is the count-exact invariant in miniature.
        /// </summary>
        [Fact]
        public void InfestedTree_FillsFullQuota()
        {
            var gen = new WorldGenerator(Seed);
            int worldSeed = Seed.GetStableHashCode();

            var infested = new LocationModel.LocationConfig
            {
                PrefabName = "InfestedTree01",
                Enable = true, Quantity = 700, Prioritized = false,
                Biome = BiomeType.Swamp, BiomeArea = 3,
                ExteriorRadius = 5f, MaxTerrainDelta = 3f,
                MinAltitude = -1f, MaxAltitude = 1000f,
            };

            var placed = LocationModel.Generate(worldSeed, gen,
                new[] { infested }, InsideUnitCircleStrategy.PolarRadiusFirst);

            // Niflheim registers exactly 700 InfestedTree01 (quota satisfied — Swamp is plentiful).
            Assert.Equal(700, placed.Count);
            Assert.All(placed, p => Assert.Equal("InfestedTree01", p.PrefabName));
        }

        /// <summary>
        /// Determinism: same seed + catalogue + strategy → identical output, every run. The whole
        /// from-seed-offline premise rests on this.
        /// </summary>
        [Fact]
        public void Generate_IsDeterministic()
        {
            int worldSeed = Seed.GetStableHashCode();
            var cfg = new[]
            {
                new LocationModel.LocationConfig
                {
                    PrefabName = "InfestedTree01", Enable = true, Quantity = 50,
                    Biome = BiomeType.Swamp, BiomeArea = 3, ExteriorRadius = 5f,
                    MaxTerrainDelta = 3f, MinAltitude = -1f, MaxAltitude = 1000f,
                }
            };

            var a = LocationModel.Generate(worldSeed, new WorldGenerator(Seed), cfg, InsideUnitCircleStrategy.PolarRadiusFirst);
            var b = LocationModel.Generate(worldSeed, new WorldGenerator(Seed), cfg, InsideUnitCircleStrategy.PolarRadiusFirst);

            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].PrefabName, b[i].PrefabName);
                Assert.Equal(a[i].X, b[i].X);
                Assert.Equal(a[i].Z, b[i].Z);
            }
        }
    }
}
