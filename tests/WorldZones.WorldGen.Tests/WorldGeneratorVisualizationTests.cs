using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Visual tests for WorldGenerator - outputs ASCII maps for manual inspection.
    /// Not automated assertions, but useful for spotting biome placement issues.
    /// </summary>
    public class WorldGeneratorVisualizationTests
    {
        readonly ITestOutputHelper output;
        
        public WorldGeneratorVisualizationTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void VisualizeWorld_SmallArea_ShowsBiomeDistribution()
        {
            // Arrange
            var generator = new WorldGenerator("TestWorld");
            var sb = new StringBuilder();
            
            // Map parameters
            int size = 40;
            float scale = 300f; // meters per cell
            float halfSize = size * scale / 2f;
            
            sb.AppendLine("World Visualization (40x40, 300m/cell, 12km total):");
            sb.AppendLine("Legend: . = Meadows, # = BlackForest, ~ = Ocean, ^ = Mountain");
            sb.AppendLine("        S = Swamp, P = Plains, M = Mistlands, ? = Other");
            sb.AppendLine();
            
            // Generate map
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wx = (x * scale) - halfSize;
                    float wz = (y * scale) - halfSize;
                    
                    BiomeType biome = generator.GetBiome(wx, wz);
                    char symbol = BiomeToSymbol(biome);
                    sb.Append(symbol);
                }
                sb.AppendLine();
            }
            
            this.output.WriteLine(sb.ToString());
            
            // Not an assertion - just output for manual inspection
            Assert.True(true, "Visual test - check output");
        }
        
        [Fact]
        public void VisualizeHeight_SmallArea_ShowsTerrainShape()
        {
            // Arrange
            var generator = new WorldGenerator("TestWorld");
            var sb = new StringBuilder();
            
            // Map parameters
            int size = 40;
            float scale = 300f;
            float halfSize = size * scale / 2f;
            
            sb.AppendLine("Height Map (40x40, 300m/cell):");
            sb.AppendLine("Legend: . = low, + = medium, # = high, ~ = water");
            sb.AppendLine();
            
            // Generate height map
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float wx = (x * scale) - halfSize;
                    float wz = (y * scale) - halfSize;
                    
                    float height = generator.GetBaseHeight(wx, wz);
                    char symbol = HeightToSymbol(height);
                    sb.Append(symbol);
                }
                sb.AppendLine();
            }
            
            this.output.WriteLine(sb.ToString());
            
            Assert.True(true, "Visual test - check output");
        }
        
        [Fact]
        public void AnalyzeBiomeDistribution_10000Samples_ShowsStatistics()
        {
            // Arrange
            var generator = new WorldGenerator("TestWorld");
            var counts = new System.Collections.Generic.Dictionary<BiomeType, int>();
            int totalSamples = 10000;
            var random = new Random(42);
            
            // Sample random points within 8000m radius
            for (int i = 0; i < totalSamples; i++)
            {
                float angle = (float)(random.NextDouble() * 2 * Math.PI);
                float distance = (float)(random.NextDouble() * 8000);
                
                float wx = (float)Math.Cos(angle) * distance;
                float wz = (float)Math.Sin(angle) * distance;
                
                BiomeType biome = generator.GetBiome(wx, wz);
                if (!counts.ContainsKey(biome))
                {
                    counts[biome] = 0;
                }
                counts[biome]++;
            }
            
            // Output statistics
            var sb = new StringBuilder();
            sb.AppendLine($"Biome Distribution (n={totalSamples}, radius=8km):");
            sb.AppendLine();
            
            foreach (var kvp in counts)
            {
                float percent = (kvp.Value * 100f) / totalSamples;
                sb.AppendLine($"  {kvp.Key,-15} {kvp.Value,5} ({percent:F1}%)");
            }
            
            this.output.WriteLine(sb.ToString());
            
            // Basic sanity checks
            Assert.True(counts.ContainsKey(BiomeType.Meadows) && counts[BiomeType.Meadows] > 0, "Should have some Meadows");
            Assert.True(counts.ContainsKey(BiomeType.Ocean) && counts[BiomeType.Ocean] > 0, "Should have some Ocean");
        }
        
        static char BiomeToSymbol(BiomeType biome)
        {
            return biome switch
            {
                BiomeType.Meadows => '.',
                BiomeType.BlackForest => '#',
                BiomeType.Ocean => '~',
                BiomeType.Mountain => '^',
                BiomeType.Swamp => 'S',
                BiomeType.Plains => 'P',
                BiomeType.Mistlands => 'M',
                BiomeType.AshLands => 'A',
                BiomeType.DeepNorth => 'D',
                _ => '?'
            };
        }
        
        static char HeightToSymbol(float height)
        {
            if (height < 0) return '~';
            if (height < 0.1f) return '.';
            if (height < 0.3f) return '+';
            return '#';
        }
    }
}
