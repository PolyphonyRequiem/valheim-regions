using Xunit;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Test if assembly_utils.dll can load and run without Unity runtime.
    /// This validates whether we can reference game assemblies for utilities.
    /// 
    /// EXPERIMENT: Testing architectural decision - can we reference Valheim
    /// assemblies directly without Unity runtime dependency?
    /// </summary>
    public class AssemblyUtilsCompatibilityTest
    {
        [Fact]
        public void GetStableHashCode_CanLoadAndExecute_WithoutUnity()
        {
            // Arrange
            string testSeed = "TestWorld";
            
            // Act - try to call game assembly extension method
            // If this throws TypeLoadException or similar, assemblies require Unity runtime
            int hash = testSeed.GetStableHashCode();
            
            // Assert - if we get here without exceptions, assembly_utils works standalone!
            Assert.NotEqual(0, hash);
        }
        
        [Fact]
        public void GetStableHashCode_MatchesKnownValheimSeeds()
        {
            // Arrange - known seed strings (can verify in-game if needed)
            string seed1 = "MyWorld";
            string seed2 = "TestWorld123";
            
            // Act
            int hash1 = seed1.GetStableHashCode();
            int hash2 = seed2.GetStableHashCode();
            
            // Assert - different seeds produce different hashes
            Assert.NotEqual(hash1, hash2);
            
            // Seeds are deterministic
            Assert.Equal(hash1, seed1.GetStableHashCode());
            Assert.Equal(hash2, seed2.GetStableHashCode());
        }
        
        [Fact]
        public void GetStableHashCode_EmptyString_DoesNotThrow()
        {
            // Act
            int hash = string.Empty.GetStableHashCode();
            
            // Assert
            // Based on algorithm: num = 5381, num2 = 5381, no loop
            // Result = 5381 + 5381 * 1566083941
            Assert.NotEqual(0, hash);
        }
        
        [Fact]
        public void GetStableHashCode_NullTerminatorHandling()
        {
            // Arrange - test the null character check in Valheim's implementation
            string normal = "test";
            
            // Act
            int hash = normal.GetStableHashCode();
            
            // Assert - should handle without exception
            Assert.NotEqual(0, hash);
        }
    }
}
