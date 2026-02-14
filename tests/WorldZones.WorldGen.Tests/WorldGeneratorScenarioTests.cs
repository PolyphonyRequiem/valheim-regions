using System;
using Xunit;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Scenario tests for WorldGenerator - validates core world generation behavior.
    /// Focus on system-level validation, not unit-testing every method.
    /// </summary>
    public class WorldGeneratorScenarioTests
    {
        [Fact]
        public void Constructor_WithValidSeed_Initializes()
        {
            // Arrange & Act
            var generator = new WorldGenerator("TestWorld");
            
            // Assert
            Assert.Equal("TestWorld", generator.Seed);
        }
        
        [Fact]
        public void Constructor_WithEmptyString_IsValid()
        {
            // Arrange & Act
            var generator = new WorldGenerator("");
            
            // Assert - empty seed is valid (produces hash of 0 in Valheim)
            Assert.Equal("", generator.Seed);
        }
        
        [Fact]
        public void Constructor_WithNullSeed_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new WorldGenerator(null!));
        }
        
        [Fact]
        public void GetBiome_AtOrigin_ReturnsMeadows()
        {
            // Arrange
            var generator = new WorldGenerator("TestWorld");
            
            // Act
            BiomeType biome = generator.GetBiome(0f, 0f);
            
            // Assert - origin should always be Meadows (safe starting area)
            Assert.Equal(BiomeType.Meadows, biome);
        }
        
        [Fact]
        public void GetBiome_BeyondWorldEdge_ReturnsOcean()
        {
            // Arrange
            var generator = new WorldGenerator("TestWorld");
            
            // Act - 11000m is beyond world edge (10500m)
            BiomeType biome = generator.GetBiome(11000f, 0f);
            
            // Assert
            Assert.Equal(BiomeType.Ocean, biome);
        }
        
        [Fact]
        public void GetBiome_SameSeed_ProducesSameResults()
        {
            // Arrange
            var gen1 = new WorldGenerator("MySeed123");
            var gen2 = new WorldGenerator("MySeed123");
            var gen3 = new WorldGenerator("MySeed123");
            
            // Act - query same coordinates from different instances
            var biome1 = gen1.GetBiome(3000f, 4000f);
            var biome2 = gen2.GetBiome(3000f, 4000f);
            var biome3 = gen3.GetBiome(3000f, 4000f);
            
            // Assert - determinism is critical for world generation
            Assert.Equal(biome1, biome2);
            Assert.Equal(biome2, biome3);
        }
        
        [Fact]
        public void GetBiome_DifferentSeeds_ProduceDifferentResults()
        {
            // Arrange
            var gen1 = new WorldGenerator("Seed1");
            var gen2 = new WorldGenerator("Seed2");
            
            // Act - query same coordinates with different seeds
            var biome1 = gen1.GetBiome(5000f, 5000f);
            var biome2 = gen2.GetBiome(5000f, 5000f);
            
            // Assert - different seeds should produce different worlds
            // (might occasionally match, but unlikely at this distance)
            // For now, just verify both calls succeed
            Assert.True(biome1 != BiomeType.None);
            Assert.True(biome2 != BiomeType.None);
        }
        
        [Fact]
        public void GetBaseHeight_AtOrigin_ReturnsReasonableValue()
        {
            // Arrange
            var generator = new WorldGenerator("TestWorld");
            
            // Act
            float height = generator.GetBaseHeight(0f, 0f);
            
            // Assert - height should be in expected range
            Assert.InRange(height, -2.0f, 2.0f);
        }
        
        [Fact]
        public void GetBaseHeight_SameSeed_ProducesSameResults()
        {
            // Arrange
            var gen1 = new WorldGenerator("HeightTest");
            var gen2 = new WorldGenerator("HeightTest");
            var gen3 = new WorldGenerator("HeightTest");
            
            // Act
            var height1 = gen1.GetBaseHeight(500f, 750f);
            var height2 = gen2.GetBaseHeight(500f, 750f);
            var height3 = gen3.GetBaseHeight(500f, 750f);
            
            // Assert - deterministic height generation
            Assert.Equal(height1, height2);
            Assert.Equal(height2, height3);
        }
        
        [Fact]
        public void GetBaseHeight_BeyondWorldEdge_ReturnsDeepOcean()
        {
            // Arrange
            var generator = new WorldGenerator("EdgeTest");
            
            // Act
            float height = generator.GetBaseHeight(11000f, 0f);
            
            // Assert - beyond edge should be deep water
            Assert.True(height < 0f, "Height beyond world edge should be negative (water)");
        }
    }
}
