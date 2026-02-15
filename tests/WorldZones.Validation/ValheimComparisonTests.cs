using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using OurGenerator = WorldZones.WorldGen.WorldGenerator;
using OurBiome = WorldZones.WorldGen.BiomeType;

namespace WorldZones.Validation
{
    public class ValheimComparisonTests
    {
        readonly ITestOutputHelper output;
        
        public ValheimComparisonTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Theory]
        [InlineData(-3476, 979)]
        [InlineData(-2692, 563)]
        [InlineData(-2099, 116)]
        public void CompareSpecificCoordinate_Seed1(float x, float z)
        {
            var ourGen = new OurGenerator("1");
            var valheimGen = new WorldGenerator();
            
            // Initialize Valheim's generator with seed "1"
            var seedHash = "1".GetStableHashCode();
            UnityEngine.Random.InitState(seedHash);
            valheimGen.Initialize(UnityEngine.Random.state);
            
            var ourBiome = ourGen.GetBiome(x, z);
            var ourHeight = ourGen.GetBaseHeight(x, z);
            
            var valheimBiome = valheimGen.GetBiome(x, z);
            float valheimHeight = valheimGen.GetBaseHeight(x, z, out _, out _);
            
            this.output.WriteLine($"Coordinate: ({x}, {z})");
            this.output.WriteLine($"");
            this.output.WriteLine($"Ours:    {ourBiome,-12} height={ourHeight:F4}");
            this.output.WriteLine($"Valheim: {valheimBiome,-12} height={valheimHeight:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"Match: {(ourBiome.ToString() == valheimBiome.ToString() ? "✓ BIOME" : "✗ biome")} " +
                                  $"{(Math.Abs(ourHeight - valheimHeight) < 0.01f ? "✓ HEIGHT" : "✗ height")}");
            
            Assert.True(true);
        }
        
        [Fact]
        public void CompareGrid_Seed1_1000Points()
        {
            var ourGen = new OurGenerator("1");
            var valheimGen = new WorldGenerator();
            
            // Initialize Valheim's generator with seed "1"
            var seedHash = "1".GetStableHashCode();
            UnityEngine.Random.InitState(seedHash);
            valheimGen.Initialize(UnityEngine.Random.state);
            
            var results = new ComparisonResults();
            
            // Sample 1000 points across the map
            UnityEngine.Random.InitState(42); // Consistent sampling
            for (int i = 0; i < 1000; i++)
            {
                float x = UnityEngine.Random.Range(-8000f, 8000f);
                float z = UnityEngine.Random.Range(-8000f, 8000f);
                
                var ourBiome = ourGen.GetBiome(x, z);
                var ourHeight = ourGen.GetBaseHeight(x, z);
                
                var valheimBiome = valheimGen.GetBiome(x, z);
                float valheimHeight = valheimGen.GetBaseHeight(x, z, out _, out _);
                
                results.AddComparison(x, z, ourBiome, ourHeight, valheimBiome, valheimHeight);
            }
            
            this.output.WriteLine(results.GetReport());
            
            Assert.True(true);
        }
    }
    
    class ComparisonResults
    {
        readonly List<(float x, float z, string ourBiome, float ourHeight, string valheimBiome, float valheimHeight)> comparisons = new();
        
        public void AddComparison(float x, float z, OurBiome ourBiome, float ourHeight, Heightmap.Biome valheimBiome, float valheimHeight)
        {
            this.comparisons.Add((x, z, ourBiome.ToString(), ourHeight, valheimBiome.ToString(), valheimHeight));
        }
        
        public string GetReport()
        {
            var biomeMatches = this.comparisons.Count(c => c.ourBiome == c.valheimBiome);
            var heightMatches = this.comparisons.Count(c => Math.Abs(c.ourHeight - c.valheimHeight) < 0.01f);
            
            var report = $"Comparison Results (n={this.comparisons.Count})\n";
            report += $"=====================================\n";
            report += $"Biome matches:  {biomeMatches}/{this.comparisons.Count} ({100.0 * biomeMatches / this.comparisons.Count:F1}%)\n";
            report += $"Height matches: {heightMatches}/{this.comparisons.Count} ({100.0 * heightMatches / this.comparisons.Count:F1}%)\n";
            report += $"\n";
            
            // Show first 20 mismatches
            var mismatches = this.comparisons.Where(c => c.ourBiome != c.valheimBiome || Math.Abs(c.ourHeight - c.valheimHeight) >= 0.01f).Take(20);
            report += $"Sample Mismatches:\n";
            foreach (var m in mismatches)
            {
                var biomeMatch = m.ourBiome == m.valheimBiome ? "✓" : "✗";
                var heightMatch = Math.Abs(m.ourHeight - m.valheimHeight) < 0.01f ? "✓" : "✗";
                report += $"  {biomeMatch}{heightMatch} ({m.x,7:F0}, {m.z,7:F0}): Ours={m.ourBiome,-12} h={m.ourHeight:F3}  Valheim={m.valheimBiome,-12} h={m.valheimHeight:F3}\n";
            }
            
            return report;
        }
    }
}
