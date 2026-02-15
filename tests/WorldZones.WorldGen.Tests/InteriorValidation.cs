using System;
using System.Drawing;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class InteriorValidation
    {
        readonly ITestOutputHelper output;
        
        public InteriorValidation(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void ValidateInteriorOnly_Under8000m()
        {
            // Only compare biomes within 8000m where PNG is accurate
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            const int WORLD_SIZE = 24576;
            const int PNG_CENTER = 4095;
            double pixelToWorld = WORLD_SIZE / 8192.0;
            const double MAX_DISTANCE = 8000.0;
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            int matches = 0;
            int mismatches = 0;
            int skipped = 0;
            
            // Sample every 32nd pixel in valid region
            int step = 32;
            for (int py = 0; py < bitmap.Height; py += step)
            {
                for (int px = 0; px < bitmap.Width; px += step)
                {
                    double worldX = (px - PNG_CENTER) * pixelToWorld;
                    double worldZ = -(py - PNG_CENTER) * pixelToWorld;
                    
                    double distance = Math.Sqrt(worldX * worldX + worldZ * worldZ);
                    
                    // ONLY compare interior
                    if (distance > MAX_DISTANCE)
                    {
                        skipped++;
                        continue;
                    }
                    
                    var color = bitmap.GetPixel(px, py);
                    var valheimBiome = GroundTruthComparisonTests.ColorToBiome(color);
                    
                    if (valheimBiome == null)
                    {
                        skipped++;
                        continue;
                    }
                    
                    var ourBiome = generator.GetBiome((float)worldX, (float)worldZ);
                    
                    if (ourBiome == valheimBiome.Value)
                        matches++;
                    else
                        mismatches++;
                }
            }
            
            int total = matches + mismatches;
            double matchPct = total > 0 ? (matches * 100.0 / total) : 0;
            
            this.output.WriteLine($"Interior Validation (distance < 8000m)");
            this.output.WriteLine($"=====================================");
            this.output.WriteLine($"Matches: {matches:N0}");
            this.output.WriteLine($"Mismatches: {mismatches:N0}");
            this.output.WriteLine($"Skipped: {skipped:N0}");
            this.output.WriteLine($"Total compared: {total:N0}");
            this.output.WriteLine($"Match rate: {matchPct:F1}%");
        }
        
        [Fact]
        public void CheckOriginBiome()
        {
            // Verify what's actually at origin for this seed
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            // Origin in world coords
            float worldX = 0;
            float worldZ = 0;
            
            // Origin in pixel coords
            int px = 4095;
            int py = 4095;
            
            var color = bitmap.GetPixel(px, py);
            var pngBiome = GroundTruthComparisonTests.ColorToBiome(color);
            var ourBiome = generator.GetBiome(worldX, worldZ);
            var ourHeight = generator.GetBaseHeight(worldX, worldZ);
            
            this.output.WriteLine($"Origin (0, 0) validation:");
            this.output.WriteLine($"  PNG color: RGB({color.R}, {color.G}, {color.B})");
            this.output.WriteLine($"  PNG biome: {pngBiome}");
            this.output.WriteLine($"  Our biome: {ourBiome}");
            this.output.WriteLine($"  Our height: {ourHeight:F4}");
            this.output.WriteLine($"");
            
            if (ourBiome == pngBiome)
                this.output.WriteLine($"✓ MATCH at origin!");
            else
                this.output.WriteLine($"✗ MISMATCH at origin: {ourBiome} vs {pngBiome}");
        }
        
        [Fact]
        public void SampleVariousDistances()
        {
            // Check match rates at different distances
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            const int WORLD_SIZE = 24576;
            const int PNG_CENTER = 4095;
            double pixelToWorld = WORLD_SIZE / 8192.0;
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            double[] distances = { 1000, 2000, 3000, 4000, 5000, 6000, 7000 };
            
            foreach (var distance in distances)
            {
                int matches = 0;
                int total = 0;
                
                // Sample around a circle at this distance
                for (int angle = 0; angle < 360; angle += 10)
                {
                    double rad = angle * Math.PI / 180.0;
                    double worldX = Math.Cos(rad) * distance;
                    double worldZ = Math.Sin(rad) * distance;
                    
                    int px = (int)((worldX / pixelToWorld) + PNG_CENTER);
                    int py = (int)((-worldZ / pixelToWorld) + PNG_CENTER);
                    
                    if (px < 0 || px >= bitmap.Width || py < 0 || py >= bitmap.Height)
                        continue;
                    
                    var color = bitmap.GetPixel(px, py);
                    var valheimBiome = GroundTruthComparisonTests.ColorToBiome(color);
                    
                    if (valheimBiome == null) continue;
                    
                    var ourBiome = generator.GetBiome((float)worldX, (float)worldZ);
                    
                    total++;
                    if (ourBiome == valheimBiome.Value)
                        matches++;
                }
                
                double pct = total > 0 ? (matches * 100.0 / total) : 0;
                this.output.WriteLine($"Distance {distance,4}m: {matches,2}/{total,2} = {pct,5:F1}%");
            }
        }
    }
}
