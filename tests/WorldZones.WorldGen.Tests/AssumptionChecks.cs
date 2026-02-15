using System;
using System.Drawing;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class AssumptionChecks
    {
        readonly ITestOutputHelper output;
        
        public AssumptionChecks(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckAssumption1_VoidBoundary()
        {
            // ASSUMPTION: Coordinates beyond 10,500 should be void (black) in PNG
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            const int WORLD_SIZE = 24576;
            const int PNG_CENTER = 4095;
            double pixelToWorld = WORLD_SIZE / 8192.0;
            
            // Check the mismatch coordinate: 11,709, 11,853
            double worldX = 11709;
            double worldZ = 11853;
            int px = (int)((worldX / pixelToWorld) + PNG_CENTER);
            int py = (int)((-worldZ / pixelToWorld) + PNG_CENTER);
            
            var distance = Math.Sqrt(worldX * worldX + worldZ * worldZ);
            
            this.output.WriteLine($"Checking coordinate: ({worldX}, {worldZ})");
            this.output.WriteLine($"Distance from origin: {distance:F1}");
            this.output.WriteLine($"Pixel coordinate: ({px}, {py})");
            
            if (px >= 0 && px < bitmap.Width && py >= 0 && py < bitmap.Height)
            {
                var color = bitmap.GetPixel(px, py);
                this.output.WriteLine($"Color: RGB({color.R}, {color.G}, {color.B})");
                
                if (color.R == 0 && color.G == 0 && color.B == 0)
                {
                    this.output.WriteLine("✓ This IS void (black) as expected!");
                }
                else
                {
                    this.output.WriteLine($"✗ This is NOT void - it's a biome color!");
                    this.output.WriteLine($"  Expected: void beyond 10,500");
                    this.output.WriteLine($"  Actual: some biome color at distance {distance:F1}");
                }
            }
            else
            {
                this.output.WriteLine("OUT OF BOUNDS in PNG!");
            }
        }
        
        [Fact]
        public void CheckAssumption2_WhatDoesOurGeneratorReturn()
        {
            // ASSUMPTION: Our generator should return Ocean for coordinates beyond WorldEdgeRadius (10,500)
            var generator = new WorldGenerator("HHcLC5acQt");
            
            double worldX = 11709;
            double worldZ = 11853;
            var distance = Math.Sqrt(worldX * worldX + worldZ * worldZ);
            
            var biome = generator.GetBiome((float)worldX, (float)worldZ);
            var height = generator.GetBaseHeight((float)worldX, (float)worldZ);
            
            this.output.WriteLine($"Coordinate: ({worldX}, {worldZ})");
            this.output.WriteLine($"Distance: {distance:F1}");
            this.output.WriteLine($"Our biome: {biome}");
            this.output.WriteLine($"Our height: {height:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"WorldRadius constant: 10000");
            this.output.WriteLine($"WorldEdgeRadius constant: 10500");
            
            if (distance > 10500)
            {
                this.output.WriteLine($"✓ Distance {distance:F1} > 10500 (should be Ocean)");
                if (biome == BiomeType.Ocean)
                    this.output.WriteLine("✓ Our generator correctly returns Ocean");
                else
                    this.output.WriteLine($"✗ Our generator returns {biome}, not Ocean!");
            }
        }
        
        [Fact]
        public void CheckAssumption3_CompareInteriorVsEdge()
        {
            // Compare match rates for interior (< 8000) vs edge (> 10000) coordinates
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            const int WORLD_SIZE = 24576;
            const int PNG_CENTER = 4095;
            double pixelToWorld = WORLD_SIZE / 8192.0;
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            int interiorMatches = 0, interiorTotal = 0;
            int edgeMatches = 0, edgeTotal = 0;
            
            // Sample radially from center
            for (int angle = 0; angle < 360; angle += 15)
            {
                double rad = angle * Math.PI / 180.0;
                
                // Interior point (distance 5000)
                double ix = Math.Cos(rad) * 5000;
                double iz = Math.Sin(rad) * 5000;
                int ipx = (int)((ix / pixelToWorld) + PNG_CENTER);
                int ipy = (int)((-iz / pixelToWorld) + PNG_CENTER);
                
                if (ipx >= 0 && ipx < bitmap.Width && ipy >= 0 && ipy < bitmap.Height)
                {
                    var color = bitmap.GetPixel(ipx, ipy);
                    var valheimBiome = GroundTruthComparisonTests.ColorToBiome(color);
                    if (valheimBiome != null)
                    {
                        var ourBiome = generator.GetBiome((float)ix, (float)iz);
                        interiorTotal++;
                        if (ourBiome == valheimBiome.Value)
                            interiorMatches++;
                    }
                }
                
                // Edge point (distance 11000)
                double ex = Math.Cos(rad) * 11000;
                double ez = Math.Sin(rad) * 11000;
                int epx = (int)((ex / pixelToWorld) + PNG_CENTER);
                int epy = (int)((-ez / pixelToWorld) + PNG_CENTER);
                
                if (epx >= 0 && epx < bitmap.Width && epy >= 0 && epy < bitmap.Height)
                {
                    var color = bitmap.GetPixel(epx, epy);
                    var valheimBiome = GroundTruthComparisonTests.ColorToBiome(color);
                    if (valheimBiome != null)
                    {
                        var ourBiome = generator.GetBiome((float)ex, (float)ez);
                        edgeTotal++;
                        if (ourBiome == valheimBiome.Value)
                            edgeMatches++;
                    }
                }
            }
            
            double interiorPct = interiorTotal > 0 ? (interiorMatches * 100.0 / interiorTotal) : 0;
            double edgePct = edgeTotal > 0 ? (edgeMatches * 100.0 / edgeTotal) : 0;
            
            this.output.WriteLine($"Interior (r=5000): {interiorMatches}/{interiorTotal} = {interiorPct:F1}%");
            this.output.WriteLine($"Edge (r=11000): {edgeMatches}/{edgeTotal} = {edgePct:F1}%");
            this.output.WriteLine($"");
            
            if (interiorPct > edgePct)
                this.output.WriteLine("✓ Interior matches better than edge (expected)");
            else
                this.output.WriteLine("✗ Edge matches as well or better - something wrong!");
        }
        
        [Fact]
        public void CheckAssumption4_ColorMappingAccuracy()
        {
            // Check if our ColorToBiome function is accurate
            var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            using var bitmap = new Bitmap(mapPath);
            
            // Sample center (should be Meadows)
            var centerColor = bitmap.GetPixel(4095, 4095);
            var centerBiome = GroundTruthComparisonTests.ColorToBiome(centerColor);
            
            this.output.WriteLine($"Center pixel (4095, 4095):");
            this.output.WriteLine($"  Color: RGB({centerColor.R}, {centerColor.G}, {centerColor.B})");
            this.output.WriteLine($"  Mapped to: {centerBiome}");
            this.output.WriteLine($"  Expected: Meadows (origin is always Meadows)");
            
            if (centerBiome == BiomeType.Meadows)
                this.output.WriteLine("✓ Color mapping correct for center");
            else
                this.output.WriteLine("✗ Color mapping WRONG - can't trust any results!");
        }
    }
}
