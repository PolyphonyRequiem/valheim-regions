using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Diagnostics for investigating Perlin noise discrepancies between our implementation
    /// and Unity's actual behavior as evidenced by tile data.
    /// </summary>
    public class PerlinNoiseDiagnostics
    {
        private readonly ITestOutputHelper output;
        private const string SEED = "HHcLC5acQt";
        
        public PerlinNoiseDiagnostics(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void SampleTileDataAndCompareHeights()
        {
            var generator = new WorldGenerator(SEED);
            
            // Sample from tile 08-08 (covers world [0, 1500])
            var tilePath = Path.Combine("data", "seeds", SEED, "data", "tiles", "08-08.bin.gz");
            var tile = LoadTile(tilePath);
            
            var samples = new List<(float x, float z, float actualHeight, float ourHeight, BiomeType actualBiome, BiomeType ourBiome)>();
            
            // Sample every 100m in the interior region
            for (int wx = 100; wx <= 1400; wx += 100)
            {
                for (int wz = 100; wz <= 1400; wz += 100)
                {
                    int sampleX = (int)((wx / 1500.0) * 1024);
                    int sampleZ = (int)((wz / 1500.0) * 1024);
                    int index = sampleZ * 1024 + sampleX;
                    
                    if (index >= 0 && index < tile.Heights.Length)
                    {
                        float actualHeight = tile.Heights[index];
                        float ourHeight = generator.GetBaseHeight(wx, wz);
                        var actualBiome = (BiomeType)tile.BiomeIds[index];
                        var ourBiome = generator.GetBiome(wx, wz);
                        
                        samples.Add((wx, wz, actualHeight, ourHeight, actualBiome, ourBiome));
                    }
                }
            }
            
            output.WriteLine($"Sampled {samples.Count} coordinates from tile 08-08\n");
            
            // Analyze the relationship between actual height and our normalized height
            output.WriteLine("=== HEIGHT ANALYSIS ===");
            output.WriteLine("Actual heights are in METERS, our heights are NORMALIZED [-2, 2]");
            output.WriteLine("Need to figure out the conversion formula\n");
            
            // Group by biome to see patterns
            var oceanSamples = samples.Where(s => s.actualBiome == BiomeType.Ocean).ToList();
            var meadowSamples = samples.Where(s => s.actualBiome == BiomeType.Meadows).ToList();
            var blackForestSamples = samples.Where(s => s.actualBiome == BiomeType.BlackForest).ToList();
            
            output.WriteLine($"Ocean samples: {oceanSamples.Count}");
            if (oceanSamples.Any())
            {
                output.WriteLine($"  Actual height range: [{oceanSamples.Min(s => s.actualHeight):F2}, {oceanSamples.Max(s => s.actualHeight):F2}] meters");
                output.WriteLine($"  Our normalized range: [{oceanSamples.Min(s => s.ourHeight):F4}, {oceanSamples.Max(s => s.ourHeight):F4}]");
                output.WriteLine($"  Biome match rate: {oceanSamples.Count(s => s.ourBiome == BiomeType.Ocean)}/{oceanSamples.Count} ({100.0 * oceanSamples.Count(s => s.ourBiome == BiomeType.Ocean) / oceanSamples.Count:F1}%)");
            }
            
            output.WriteLine($"\nMeadows samples: {meadowSamples.Count}");
            if (meadowSamples.Any())
            {
                output.WriteLine($"  Actual height range: [{meadowSamples.Min(s => s.actualHeight):F2}, {meadowSamples.Max(s => s.actualHeight):F2}] meters");
                output.WriteLine($"  Our normalized range: [{meadowSamples.Min(s => s.ourHeight):F4}, {meadowSamples.Max(s => s.ourHeight):F4}]");
                output.WriteLine($"  Biome match rate: {meadowSamples.Count(s => s.ourBiome == BiomeType.Meadows)}/{meadowSamples.Count} ({100.0 * meadowSamples.Count(s => s.ourBiome == BiomeType.Meadows) / meadowSamples.Count:F1}%)");
            }
            
            output.WriteLine($"\nBlackForest samples: {blackForestSamples.Count}");
            if (blackForestSamples.Any())
            {
                output.WriteLine($"  Actual height range: [{blackForestSamples.Min(s => s.actualHeight):F2}, {blackForestSamples.Max(s => s.actualHeight):F2}] meters");
                output.WriteLine($"  Our normalized range: [{blackForestSamples.Min(s => s.ourHeight):F4}, {blackForestSamples.Max(s => s.ourHeight):F4}]");
                output.WriteLine($"  Biome match rate: {blackForestSamples.Count(s => s.ourBiome == BiomeType.BlackForest)}/{blackForestSamples.Count} ({100.0 * blackForestSamples.Count(s => s.ourBiome == BiomeType.BlackForest) / blackForestSamples.Count:F1}%)");
            }
            
            // Show some specific examples
            output.WriteLine("\n=== SPECIFIC MISMATCHES ===");
            var mismatches = samples.Where(s => s.actualBiome != s.ourBiome).Take(20).ToList();
            foreach (var m in mismatches)
            {
                output.WriteLine($"({m.x,5}, {m.z,5}): Actual={m.actualBiome,-12} (h={m.actualHeight,7:F2}m) | Ours={m.ourBiome,-12} (h={m.ourHeight,7:F4})");
            }
        }

        [Fact(Skip = "Diagnostic test for custom PerlinNoise - now using Unity's implementation")]
        public void TestPerlinNoiseDirectly()
        {
            // This test was for the old custom PerlinNoise implementation
            // We now use Unity's native Mathf.PerlinNoise via UnityPerlinNoise.GetNoise()
            // Unity's implementation is the ground truth, so no need to test it
        }

        [Fact]
        public void CompareOffsetCalculations()
        {
            // Check if our seed -> offset calculation matches Unity
            var generator = new WorldGenerator(SEED);
            
            output.WriteLine("=== SEED OFFSET INVESTIGATION ===\n");
            output.WriteLine($"Seed: {SEED}");
            output.WriteLine($"Seed hash: {SEED.GetHashCode()}");
            
            // Use reflection to get private fields
            var field0 = typeof(WorldGenerator).GetField("offset0", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var field1 = typeof(WorldGenerator).GetField("offset1", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (field0 != null && field1 != null)
            {
                var offset0 = field0.GetValue(generator);
                var offset1 = field1.GetValue(generator);
                output.WriteLine($"offset0: {offset0}");
                output.WriteLine($"offset1: {offset1}");
            }
            else
            {
                output.WriteLine("Could not access offset fields via reflection");
            }
        }

        [Fact]
        public void ReverseEngineerHeightFormula()
        {
            // Try to figure out the relationship between actual height (meters)
            // and normalized height by looking at threshold boundaries
            
            output.WriteLine("=== REVERSE ENGINEER HEIGHT FORMULA ===\n");
            output.WriteLine("Known thresholds in NORMALIZED space:");
            output.WriteLine("  Ocean:      height <= 0.02");
            output.WriteLine("  Mountain:   height > 0.4");
            output.WriteLine("  Everything else: 0.02 < height <= 0.4\n");
            
            var generator = new WorldGenerator(SEED);
            var tilePath = Path.Combine("data", "seeds", SEED, "data", "tiles", "08-08.bin.gz");
            var tile = LoadTile(tilePath);
            
            // Find samples right at the ocean/land boundary
            var samples = new List<(float actualHeight, float ourHeight, BiomeType actualBiome)>();
            
            for (int wx = 0; wx <= 1500; wx += 50)
            {
                for (int wz = 0; wz <= 1500; wz += 50)
                {
                    int sampleX = (int)((wx / 1500.0) * 1024);
                    int sampleZ = (int)((wz / 1500.0) * 1024);
                    int index = sampleZ * 1024 + sampleX;
                    
                    if (index >= 0 && index < tile.Heights.Length)
                    {
                        float actualHeight = tile.Heights[index];
                        float ourHeight = generator.GetBaseHeight(wx, wz);
                        var actualBiome = (BiomeType)tile.BiomeIds[index];
                        
                        samples.Add((actualHeight, ourHeight, actualBiome));
                    }
                }
            }
            
            // Find samples near the ocean/land boundary (actual height near 0m)
            var boundaryOcean = samples.Where(s => s.actualBiome == BiomeType.Ocean && s.actualHeight > -5 && s.actualHeight < 0).ToList();
            var boundaryLand = samples.Where(s => s.actualBiome != BiomeType.Ocean && s.actualHeight > 0 && s.actualHeight < 5).ToList();
            
            output.WriteLine($"Ocean samples near sea level (0m): {boundaryOcean.Count}");
            if (boundaryOcean.Any())
            {
                output.WriteLine($"  Actual height: [{boundaryOcean.Min(s => s.actualHeight):F2}, {boundaryOcean.Max(s => s.actualHeight):F2}] meters");
                output.WriteLine($"  Our heights:   [{boundaryOcean.Min(s => s.ourHeight):F4}, {boundaryOcean.Max(s => s.ourHeight):F4}] normalized");
                output.WriteLine($"  Expected: our heights should be <= 0.02");
            }
            
            output.WriteLine($"\nLand samples near sea level (0m): {boundaryLand.Count}");
            if (boundaryLand.Any())
            {
                output.WriteLine($"  Actual height: [{boundaryLand.Min(s => s.actualHeight):F2}, {boundaryLand.Max(s => s.actualHeight):F2}] meters");
                output.WriteLine($"  Our heights:   [{boundaryLand.Min(s => s.ourHeight):F4}, {boundaryLand.Max(s => s.ourHeight):F4}] normalized");
                output.WriteLine($"  Expected: our heights should be > 0.02");
            }
            
            output.WriteLine("\n=== HYPOTHESIS ===");
            output.WriteLine("If our PerlinNoise is correct but GetBaseHeight formula is wrong,");
            output.WriteLine("we should see consistent OFFSET but similar RELATIVE patterns.");
            output.WriteLine("If PerlinNoise itself is wrong, patterns will be completely different.");
        }

        static double StdDev(List<float> values)
        {
            double avg = values.Average();
            double sumSquares = values.Sum(v => (v - avg) * (v - avg));
            return Math.Sqrt(sumSquares / values.Count);
        }

        static (float[] Heights, int[] BiomeIds) LoadTile(string path)
        {
            using var fileStream = File.OpenRead(path);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzipStream);
            
            reader.ReadInt32(); // version
            var heights = new float[1024 * 1024];
            var biomeIds = new int[1024 * 1024];
            
            for (int i = 0; i < 1024 * 1024; i++)
            {
                heights[i] = reader.ReadSingle();
            }
            for (int i = 0; i < 1024 * 1024; i++)
            {
                biomeIds[i] = reader.ReadInt32();
            }
            
            return (heights, biomeIds);
        }
    }
}
