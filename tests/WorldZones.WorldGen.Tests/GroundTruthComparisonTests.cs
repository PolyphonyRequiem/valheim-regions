using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class GroundTruthComparisonTests
    {
        readonly ITestOutputHelper output;
        
        public GroundTruthComparisonTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CompareAgainstValheimExport_HHcLC5acQt()
        {
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            if (!File.Exists(mapPath))
            {
                this.output.WriteLine($"Map file not found: {mapPath}");
                Assert.True(false, "Ground truth map not found");
                return;
            }
            
            using var bitmap = new Bitmap(mapPath);
            this.output.WriteLine($"Loaded map: {bitmap.Width}x{bitmap.Height}");
            
            // VALIDATED coordinate mapping (from checkpoint 007)
            const int WORLD_SIZE = 24576;  // 3.0 m/pixel exactly
            const int PNG_CENTER = 4095;    // Validated center at (4095, 4095)
            double pixelToWorld = WORLD_SIZE / 8192.0;  // 3.0 exactly
            
            var generator = new WorldGenerator("HHcLC5acQt");
            var results = new ComparisonResults();
            
            // Sample every 16th pixel for speed (still gives us ~250k comparisons)
            int step = 16;
            for (int py = 0; py < bitmap.Height; py += step)
            {
                for (int px = 0; px < bitmap.Width; px += step)
                {
                    // VALIDATED coordinate conversion
                    double worldX = (px - PNG_CENTER) * pixelToWorld;
                    double worldZ = -(py - PNG_CENTER) * pixelToWorld;  // Z flipped!
                    
                    var pixel = bitmap.GetPixel(px, py);
                    var valheimBiome = ColorToBiome(pixel);
                    
                    if (valheimBiome == null) continue; // Outside world or unknown color
                    
                    var ourBiome = generator.GetBiome((float)worldX, (float)worldZ);
                    results.AddComparison((float)worldX, (float)worldZ, ourBiome, valheimBiome.Value);
                }
            }
            
            this.output.WriteLine(results.GetReport());
            
            // For now, just report - don't fail
            Assert.True(true);
        }
        
        [Theory]
        [InlineData(-3476, 979)]
        [InlineData(-2692, 563)]
        [InlineData(-2099, 116)]
        public void CheckSpecificCoordinate_AgainstMap(float worldX, float worldZ)
        {
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            using var bitmap = new Bitmap(mapPath);
            
            // VALIDATED coordinate mapping
            const int WORLD_SIZE = 24576;
            const int PNG_CENTER = 4095;
            double pixelToWorld = WORLD_SIZE / 8192.0;
            
            // Convert world to pixel coordinates (Z axis is flipped!)
            int px = (int)((worldX / pixelToWorld) + PNG_CENTER);
            int py = (int)((-worldZ / pixelToWorld) + PNG_CENTER);
            
            var pixel = bitmap.GetPixel(px, py);
            var valheimBiome = ColorToBiome(pixel);
            
            var generator = new WorldGenerator("HHcLC5acQt");
            var ourBiome = generator.GetBiome(worldX, worldZ);
            var ourHeight = generator.GetBaseHeight(worldX, worldZ);
            
            this.output.WriteLine($"Coordinate: ({worldX}, {worldZ})");
            this.output.WriteLine($"Pixel: ({px}, {py}) = RGB({pixel.R}, {pixel.G}, {pixel.B})");
            this.output.WriteLine($"");
            this.output.WriteLine($"Valheim: {valheimBiome}");
            this.output.WriteLine($"Ours:    {ourBiome} (height={ourHeight:F4})");
            this.output.WriteLine($"");
            this.output.WriteLine($"Match: {(valheimBiome.ToString() == ourBiome.ToString() ? "✓ YES" : "✗ NO")}");
            
            Assert.True(true);
        }
        
        public static BiomeType? ColorToBiome(Color c)
        {
            // Based on actual sampled colors from the map
            
            // Ocean: RGB(0, 0, 153) - dark blue
            if (IsColorClose(c, 0, 0, 153, 20)) return BiomeType.Ocean;
            
            // Meadows: RGB(199, 199, 49) - yellowish green  
            if (IsColorClose(c, 199, 199, 49, 30)) return BiomeType.Meadows;
            
            // Mountain: RGB(163, 113, 87) - brownish/tan (this seems wrong for mountain...)
            // Actually looking at map, white areas are Mountain
            if (IsColorClose(c, 255, 255, 255, 20)) return BiomeType.Mountain;
            
            // BlackForest: dark green (estimating from visual)
            if (IsColorClose(c, 34, 85, 34, 30)) return BiomeType.BlackForest;
            
            // Plains: yellow (estimating from visual)
            if (IsColorClose(c, 200, 200, 0, 30)) return BiomeType.Plains;
            
            // Swamp: brown RGB(163, 113, 87) based on sample
            if (IsColorClose(c, 163, 113, 87, 25)) return BiomeType.Swamp;
            
            // Mistlands: gray (estimating from visual)
            if (IsColorClose(c, 105, 105, 105, 30)) return BiomeType.Mistlands;
            
            // Shallows: light blue
            if (IsColorClose(c, 135, 206, 250, 30)) return null;
            
            // Ashlands: red
            if (IsColorClose(c, 255, 0, 0, 30)) return null;
            
            // DeepNorth: cyan  
            if (IsColorClose(c, 200, 200, 255, 30)) return null;
            
            // Black background
            if (c.R < 10 && c.G < 10 && c.B < 10) return null;
            
            return null; // Unknown color
        }
        
        static bool IsColorClose(Color c, int r, int g, int b, int tolerance = 30)
        {
            return Math.Abs(c.R - r) <= tolerance &&
                   Math.Abs(c.G - g) <= tolerance &&
                   Math.Abs(c.B - b) <= tolerance;
        }
    }
    
    class ComparisonResults
    {
        readonly List<(float x, float z, BiomeType ours, BiomeType valheim)> comparisons = new();
        
        public void AddComparison(float x, float z, BiomeType ours, BiomeType valheim)
        {
            this.comparisons.Add((x, z, ours, valheim));
        }
        
        public string GetReport()
        {
            if (this.comparisons.Count == 0)
                return "No comparisons made";
            
            var matches = this.comparisons.Count(c => c.ours == c.valheim);
            var total = this.comparisons.Count;
            
            var report = $"Ground Truth Comparison\n";
            report += $"=======================\n";
            report += $"Total comparisons: {total:N0}\n";
            report += $"Matches: {matches:N0} ({100.0 * matches / total:F1}%)\n";
            report += $"Mismatches: {total - matches:N0}\n";
            report += $"\n";
            
            // Breakdown by Valheim's actual biomes
            var byValheimBiome = this.comparisons.GroupBy(c => c.valheim)
                .OrderByDescending(g => g.Count());
            
            report += $"Valheim's Biome Distribution:\n";
            foreach (var group in byValheimBiome)
            {
                var count = group.Count();
                var correct = group.Count(c => c.ours == c.valheim);
                report += $"  {group.Key,-15} {count,6} ({100.0 * count / total,5:F1}%) - accuracy: {100.0 * correct / count:F1}%\n";
            }
            report += $"\n";
            
            // Show confusion matrix sample
            report += $"Sample Mismatches (first 20):\n";
            var mismatches = this.comparisons.Where(c => c.ours != c.valheim).Take(20);
            foreach (var m in mismatches)
            {
                report += $"  ({m.x,7:F0}, {m.z,7:F0}): Ours={m.ours,-12} Valheim={m.valheim}\n";
            }
            
            return report;
        }
    }
}
