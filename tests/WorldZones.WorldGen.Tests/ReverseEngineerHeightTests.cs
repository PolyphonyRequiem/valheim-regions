using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class ReverseEngineerHeightTests
    {
        readonly ITestOutputHelper output;
        
        public ReverseEngineerHeightTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void AnalyzeOurHeights_WhereValheimHasSpecificBiomes()
        {
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            float worldSize = 21000f;
            float pixelToWorld = worldSize / bitmap.Width;
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            // Sample 1000 points and collect our heights for each Valheim biome
            var heightsByValheimBiome = new Dictionary<BiomeType, List<float>>();
            
            var random = new Random(42);
            for (int i = 0; i < 2000; i++)
            {
                int px = random.Next(bitmap.Width);
                int py = random.Next(bitmap.Height);
                
                float worldX = (px - bitmap.Width / 2f) * pixelToWorld;
                float worldZ = (py - bitmap.Height / 2f) * pixelToWorld;
                
                var pixel = bitmap.GetPixel(px, py);
                var valheimBiome = GroundTruthComparisonTests.ColorToBiome(pixel);
                
                if (valheimBiome == null) continue;
                
                var ourHeight = generator.GetBaseHeight(worldX, worldZ);
                
                if (!heightsByValheimBiome.ContainsKey(valheimBiome.Value))
                    heightsByValheimBiome[valheimBiome.Value] = new List<float>();
                
                heightsByValheimBiome[valheimBiome.Value].Add(ourHeight);
            }
            
            this.output.WriteLine("OUR HEIGHT RANGES where Valheim has specific biomes:");
            this.output.WriteLine("=====================================================");
            this.output.WriteLine("");
            this.output.WriteLine("Our thresholds: Ocean≤0.02, Mountain>0.4, Swamp(0.05-0.25)");
            this.output.WriteLine("");
            
            foreach (var biome in heightsByValheimBiome.Keys.OrderBy(b => b.ToString()))
            {
                var heights = heightsByValheimBiome[biome];
                if (heights.Count == 0) continue;
                
                var min = heights.Min();
                var max = heights.Max();
                var avg = heights.Average();
                var median = heights.OrderBy(h => h).ElementAt(heights.Count / 2);
                
                var belowOcean = heights.Count(h => h <= 0.02f);
                var inOcean = heights.Count(h => h > 0.02f && h <= 0.05f);
                var low = heights.Count(h => h > 0.05f && h <= 0.2f);
                var mid = heights.Count(h => h > 0.2f && h <= 0.4f);
                var high = heights.Count(h => h > 0.4f);
                
                this.output.WriteLine($"{biome,-12} (n={heights.Count,4}): min={min:F3} avg={avg:F3} median={median:F3} max={max:F3}");
                this.output.WriteLine($"             Heights: ≤0.02:{belowOcean,4} (0.02-0.05):{inOcean,4} (0.05-0.2):{low,4} (0.2-0.4):{mid,4} >0.4:{high,4}");
                this.output.WriteLine("");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void CompareBiomeDistributionByDistance()
        {
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            float worldSize = 21000f;
            float pixelToWorld = worldSize / bitmap.Width;
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            var distanceRanges = new[] { 
                (0f, 2000f, "0-2km"), 
                (2000f, 4000f, "2-4km"), 
                (4000f, 6000f, "4-6km"),
                (6000f, 8000f, "6-8km"),
                (8000f, 10000f, "8-10km")
            };
            
            this.output.WriteLine("BIOME DISTRIBUTION BY DISTANCE");
            this.output.WriteLine("==============================");
            this.output.WriteLine("");
            
            foreach (var (minDist, maxDist, label) in distanceRanges)
            {
                var valheimCounts = new Dictionary<BiomeType, int>();
                var ourCounts = new Dictionary<BiomeType, int>();
                
                // Sample 500 points in this ring
                var random = new Random(42);
                for (int i = 0; i < 500; i++)
                {
                    float angle = (float)(random.NextDouble() * 2 * Math.PI);
                    float dist = (float)(minDist + random.NextDouble() * (maxDist - minDist));
                    
                    float worldX = (float)(Math.Cos(angle) * dist);
                    float worldZ = (float)(Math.Sin(angle) * dist);
                    
                    // Get Valheim's biome from map
                    int px = (int)((worldX / pixelToWorld) + bitmap.Width / 2f);
                    int py = (int)((worldZ / pixelToWorld) + bitmap.Height / 2f);
                    
                    if (px < 0 || px >= bitmap.Width || py < 0 || py >= bitmap.Height) continue;
                    
                    var pixel = bitmap.GetPixel(px, py);
                    var valheimBiome = GroundTruthComparisonTests.ColorToBiome(pixel);
                    
                    if (valheimBiome != null)
                    {
                        if (!valheimCounts.ContainsKey(valheimBiome.Value))
                            valheimCounts[valheimBiome.Value] = 0;
                        valheimCounts[valheimBiome.Value]++;
                    }
                    
                    // Get our biome
                    var ourBiome = generator.GetBiome(worldX, worldZ);
                    if (!ourCounts.ContainsKey(ourBiome))
                        ourCounts[ourBiome] = 0;
                    ourCounts[ourBiome]++;
                }
                
                this.output.WriteLine($"Distance: {label}");
                this.output.WriteLine($"  Valheim: {string.Join(", ", valheimCounts.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
                this.output.WriteLine($"  Ours:    {string.Join(", ", ourCounts.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
                this.output.WriteLine("");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void AnalyzeEdgeOceanProblem()
        {
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            float worldSize = 21000f;
            float pixelToWorld = worldSize / bitmap.Width;
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            this.output.WriteLine("EDGE OCEAN PROBLEM ANALYSIS");
            this.output.WriteLine("============================");
            this.output.WriteLine("");
            this.output.WriteLine("Sampling at world edge where we predict Ocean but Valheim has BlackForest:");
            this.output.WriteLine("");
            
            // Sample the problematic coordinates from the mismatch list
            var testPoints = new[] {
                (-10008f, -10131f),
                (-9967f, -10131f),
                (-9000f, -9000f),
                (-10000f, 0f),
                (0f, -10000f)
            };
            
            foreach (var (x, z) in testPoints)
            {
                int px = (int)((x / pixelToWorld) + bitmap.Width / 2f);
                int py = (int)((z / pixelToWorld) + bitmap.Height / 2f);
                
                if (px < 0 || px >= bitmap.Width || py < 0 || py >= bitmap.Height)
                {
                    this.output.WriteLine($"  ({x,7:F0}, {z,7:F0}): OUT OF BOUNDS");
                    continue;
                }
                
                var pixel = bitmap.GetPixel(px, py);
                var valheimBiome = GroundTruthComparisonTests.ColorToBiome(pixel);
                
                var ourBiome = generator.GetBiome(x, z);
                var ourHeight = generator.GetBaseHeight(x, z);
                var dist = Math.Sqrt(x * x + z * z);
                
                this.output.WriteLine($"  ({x,7:F0}, {z,7:F0}): dist={dist:F0}m");
                this.output.WriteLine($"    Valheim: {valheimBiome}");
                this.output.WriteLine($"    Ours:    {ourBiome} (height={ourHeight:F4})");
                this.output.WriteLine("");
            }
            
            Assert.True(true);
        }
    }
}
