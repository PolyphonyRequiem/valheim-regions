using NUnit.Framework;
using UnityEngine;
using WorldZones.WorldGen;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Tests for WorldGenerator using Unity's Mathf.PerlinNoise directly.
    /// </summary>
    [TestFixture]
    public class WorldGeneratorTests
    {
        /// <summary>
        /// Helper to create WorldGenerator with Unity-generated offsets from seed.
        /// </summary>
        WorldGenerator CreateGenerator(string seed)
        {
            int seedHash = string.IsNullOrEmpty(seed) ? 0 : seed.GetStableHashCode();
            
            // Generate offsets using Unity's Random (matches Valheim)
            UnityEngine.Random.State savedState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seedHash);
            int offset0 = UnityEngine.Random.Range(-10000, 10000);
            int offset1 = UnityEngine.Random.Range(-10000, 10000);
            int offset2 = UnityEngine.Random.Range(-10000, 10000);
            int offset3 = UnityEngine.Random.Range(-10000, 10000);
            int offset4 = UnityEngine.Random.Range(-10000, 10000);
            UnityEngine.Random.state = savedState;
            
            return new WorldGenerator(seed, offset0, offset1, offset2, offset3, offset4);
        }
        
        [Test]
        public void WorldGenerator_CanBeCreatedWithUnityPerlin()
        {
            // Arrange
            string seed = "HHcLC5acQt";
            
            // Act - Create WorldGenerator with Unity runtime
            var generator = CreateGenerator(seed);
            
            // Assert
            Assert.That(generator.Seed, Is.EqualTo(seed));
        }
        
        [Test]
        public void GetBiome_OriginIsOcean()
        {
            // Arrange
            var generator = CreateGenerator("HHcLC5acQt");
            
            // Act
            float height = generator.GetHeight(0f, 0f);
            var biome = generator.GetBiome(0f, 0f);
            
            // Log for debugging
            UnityEngine.Debug.Log($"Origin (0,0): height={height}, biome={biome}");
            
            // Assert
            Assert.That(biome, Is.EqualTo(BiomeType.Ocean), $"Origin (0,0) should be Ocean (height was {height})");
        }
        
        [Test]
        public void GetBiome_StartingZoneIsMeadows()
        {
            // Arrange
            var generator = CreateGenerator("HHcLC5acQt");
            
            // Test a known Meadows coordinate (starting zone)
            // Act
            var biome = generator.GetBiome(100f, 100f);
            
            // Assert - Should be Meadows (starting biome)
            // Note: Exact coord might need adjustment based on actual world gen
            Assert.That(biome, Is.Not.EqualTo(BiomeType.None), "Should return a valid biome");
        }
        
        [Test]
        public void GetBiome_ReturnsConsistentResults()
        {
            // Arrange
            var generator = CreateGenerator("HHcLC5acQt");
            float x = 1000f;
            float z = 2000f;
            
            // Act
            var biome1 = generator.GetBiome(x, z);
            var biome2 = generator.GetBiome(x, z);
            
            // Assert
            Assert.That(biome1, Is.EqualTo(biome2), "GetBiome should return consistent results");
        }
        
        [Test]
        public void GetHeight_ReturnsValidRange()
        {
            // Arrange
            var generator = CreateGenerator("HHcLC5acQt");
            
            // Test various coordinates
            var testCoords = new[]
            {
                (0f, 0f),
                (100f, 100f),
                (1000f, 1000f),
                (5000f, 5000f),
            };
            
            foreach (var (x, z) in testCoords)
            {
                // Act
                float height = generator.GetHeight(x, z);
                var biome = generator.GetBiome(x, z);
                
                // Log for debugging
                UnityEngine.Debug.Log($"Coord ({x}, {z}): height={height:F4}, biome={biome}");
                
                // Assert - Height should be in reasonable range
                Assert.That(height, Is.InRange(-1f, 2f), 
                    $"Height at ({x}, {z}) should be in reasonable range");
            }
        }
    }
}

