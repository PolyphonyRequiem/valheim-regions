using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class PngBoundaryTest
    {
        readonly ITestOutputHelper output;
        
        public PngBoundaryTest(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void FindExactCircularBoundary()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("FINDING EXACT CIRCULAR BOUNDARY IN PNG");
            this.output.WriteLine("=======================================");
            this.output.WriteLine($"PNG size: {png.Width}x{png.Height}");
            this.output.WriteLine($"Testing corrected center: ({png.Width/2 - 49}, {png.Height/2})");
            this.output.WriteLine("");
            
            int centerX = png.Width / 2 - 49;  // Shifted 49 pixels WEST
            int centerY = png.Height / 2;
            
            // Sample 36 angles (every 10 degrees)
            int numAngles = 36;
            
            // Find the transition distance
            int lastAllBiomes = -1;
            int firstAllVoid = -1;
            
            for (int radius = 0; radius < 5000; radius++)
            {
                int biomeCount = 0;
                int voidCount = 0;
                int unknownCount = 0;
                
                for (int i = 0; i < numAngles; i++)
                {
                    double angle = (i * 360.0 / numAngles) * Math.PI / 180.0;
                    int px = centerX + (int)(radius * Math.Cos(angle));
                    int py = centerY + (int)(radius * Math.Sin(angle));
                    
                    if (px < 0 || px >= png.Width || py < 0 || py >= png.Height)
                    {
                        unknownCount++;
                        continue;
                    }
                    
                    var pixel = png.GetPixel(px, py);
                    
                    if (IsVoid(pixel))
                        voidCount++;
                    else if (IsBiomeColor(pixel))
                        biomeCount++;
                    else
                        unknownCount++;
                }
                
                // Track first and last important radii
                if (biomeCount == numAngles)
                    lastAllBiomes = radius;
                
                if (voidCount == numAngles && firstAllVoid == -1)
                {
                    firstAllVoid = radius;
                }
                
                // Report EVERY radius in transition zone
                if (lastAllBiomes > 0 && radius >= lastAllBiomes && radius <= lastAllBiomes + 300)
                {
                    this.output.WriteLine($"Radius {radius,4}: biome={biomeCount,2}, void={voidCount,2}, unknown={unknownCount,2}");
                }
                
                if (firstAllVoid > 0 && radius > firstAllVoid)
                    break; // Done
            }
            
            this.output.WriteLine("");
            this.output.WriteLine("RESULTS:");
            this.output.WriteLine($"  Last radius with all biomes: {lastAllBiomes}");
            this.output.WriteLine($"  First radius with all void: {firstAllVoid}");
            this.output.WriteLine($"  Transition width: {firstAllVoid - lastAllBiomes} pixels");
            this.output.WriteLine("");
            
            if (firstAllVoid - lastAllBiomes == 1)
            {
                this.output.WriteLine("✓ SHARP BOUNDARY - Center is correct!");
                this.output.WriteLine($"  Map radius: {lastAllBiomes} pixels");
            }
            else if (firstAllVoid - lastAllBiomes > 1 && firstAllVoid - lastAllBiomes < 20)
            {
                this.output.WriteLine("⚠ GRADUAL TRANSITION - Expected but acceptable");
                this.output.WriteLine($"  Map radius: ~{(lastAllBiomes + firstAllVoid) / 2} pixels");
            }
            else
            {
                this.output.WriteLine("✗ INCONSISTENT BOUNDARY - Center assumption is WRONG");
                this.output.WriteLine("  The map may not be centered at pixel (4096, 4096)");
                this.output.WriteLine("  OR the map is not perfectly circular");
            }
        }
        
        bool IsVoid(Color c)
        {
            return c.R < 10 && c.G < 10 && c.B < 10;
        }
        
        bool IsBiomeColor(Color c)
        {
            int tolerance = 3;
            
            // Known biome colors
            if (IsColor(c, 0, 0, 153, tolerance)) return true; // Ocean
            if (IsColor(c, 102, 102, 255, tolerance)) return true; // Shallows
            if (IsColor(c, 52, 94, 59, tolerance)) return true; // BlackForest
            if (IsColor(c, 145, 167, 91, tolerance)) return true; // Meadows
            if (IsColor(c, 82, 82, 82, tolerance)) return true; // Mistlands
            if (IsColor(c, 255, 255, 255, tolerance)) return true; // Mountain
            if (IsColor(c, 199, 199, 49, tolerance)) return true; // Plains
            if (IsColor(c, 163, 113, 87, tolerance)) return true; // Swamp
            if (IsColor(c, 255, 0, 0, tolerance)) return true; // AshLands
            
            return false;
        }
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
    }
}
