using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Debugging tests to investigate biome placement logic.
    /// </summary>
    public class WorldGeneratorDebugTests
    {
        readonly ITestOutputHelper output;
        
        public WorldGeneratorDebugTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void DebugBiomePlacement_CheckDistanceRings()
        {
            var generator = new WorldGenerator("TestWorld");
            
            // Check biomes at specific distances
            float[] distances = { 500, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000 };
            
            this.output.WriteLine("Biome placement by distance (at angle 0):");
            this.output.WriteLine("");
            
            foreach (float dist in distances)
            {
                BiomeType biome = generator.GetBiome(dist, 0f);
                float height = generator.GetBaseHeight(dist, 0f);
                this.output.WriteLine($"  {dist,5}m: {biome,-15} (height: {height:F3})");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void DebugHeightGeneration_CheckElevationVariation()
        {
            var generator = new WorldGenerator("TestWorld");
            
            // Sample heights in a circle at 3000m radius
            this.output.WriteLine("Heights at 3000m radius (should show variation for Mountains):");
            this.output.WriteLine("");
            
            for (int angle = 0; angle < 360; angle += 45)
            {
                float rad = angle * (float)Math.PI / 180f;
                float x = (float)Math.Cos(rad) * 3000f;
                float z = (float)Math.Sin(rad) * 3000f;
                
                float height = generator.GetBaseHeight(x, z);
                BiomeType biome = generator.GetBiome(x, z);
                this.output.WriteLine($"  Angle {angle,3}°: height={height:F3}, biome={biome}");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void DebugNoiseValues_CheckThresholds()
        {
            var generator = new WorldGenerator("TestWorld");
            
            // Check if we're getting any noise values above thresholds
            int aboveThreshold = 0;
            int totalSamples = 1000;
            var random = new Random(42);
            
            this.output.WriteLine("Checking noise value distribution:");
            this.output.WriteLine("");
            
            for (int i = 0; i < totalSamples; i++)
            {
                float angle = (float)(random.NextDouble() * 2 * Math.PI);
                float distance = (float)(random.NextDouble() * 6000 + 1000); // 1000-7000m
                
                float x = (float)Math.Cos(angle) * distance;
                float z = (float)Math.Sin(angle) * distance;
                
                float height = generator.GetBaseHeight(x, z);
                
                // Count how many have height > 0.4 (Mountain threshold)
                if (height > 0.4f) aboveThreshold++;
            }
            
            float percent = (aboveThreshold * 100f) / totalSamples;
            this.output.WriteLine($"  Samples with height > 0.4: {aboveThreshold}/{totalSamples} ({percent:F1}%)");
            this.output.WriteLine($"  (Expected: Some Mountains should appear if > 0%)");
            
            Assert.True(true);
        }
    }
}
