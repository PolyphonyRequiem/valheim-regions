using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class VoidBoundaryAnalysis
    {
        readonly ITestOutputHelper output;
        
        public VoidBoundaryAnalysis(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void FindVoidBoundary()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("FINDING VOID BOUNDARY IN PNG");
            this.output.WriteLine("=============================");
            this.output.WriteLine($"PNG size: {png.Width}x{png.Height}");
            this.output.WriteLine("");
            
            // Test from center outward in cardinal directions to find void
            float pngWorldSize = 21000f;
            float pixelToWorld = pngWorldSize / png.Width;
            
            this.output.WriteLine($"Assumed PNG world coverage: ±{pngWorldSize/2}m ({pngWorldSize} total)");
            this.output.WriteLine($"Pixel to world ratio: {pixelToWorld:F2} units/pixel");
            this.output.WriteLine("");
            
            // Test northward (+Z)
            float voidNorth = FindVoid(png, 0, 1, pngWorldSize);
            this.output.WriteLine($"Void starts North  (+Z): ~{voidNorth:F0}m");
            
            // Test southward (-Z)
            float voidSouth = FindVoid(png, 0, -1, pngWorldSize);
            this.output.WriteLine($"Void starts South  (-Z): ~{voidSouth:F0}m");
            
            // Test eastward (+X)
            float voidEast = FindVoid(png, 1, 0, pngWorldSize);
            this.output.WriteLine($"Void starts East   (+X): ~{voidEast:F0}m");
            
            // Test westward (-X)
            float voidWest = FindVoid(png, -1, 0, pngWorldSize);
            this.output.WriteLine($"Void starts West   (-X): ~{voidWest:F0}m");
            
            this.output.WriteLine("");
            this.output.WriteLine("CONCLUSION:");
            float avgRadius = (voidNorth + Math.Abs(voidSouth) + voidEast + Math.Abs(voidWest)) / 4f;
            this.output.WriteLine($"  Average void radius: {avgRadius:F0}m");
            this.output.WriteLine($"  Valheim world radius is typically 10,500m");
            
            if (Math.Abs(avgRadius - 10500) < 100)
            {
                this.output.WriteLine($"  ✓ MATCHES! PNG coordinate mapping appears correct.");
            }
            else
            {
                this.output.WriteLine($"  ✗ MISMATCH! Expected ~10,500m but got {avgRadius:F0}m");
                this.output.WriteLine($"    Our coordinate conversion may be wrong.");
            }
        }
        
        float FindVoid(Bitmap png, int xDir, int zDir, float pngWorldSize)
        {
            float pixelToWorld = pngWorldSize / png.Width;
            
            // Start from center, move outward in steps
            for (float dist = 0; dist < 12000; dist += 100)
            {
                float worldX = dist * xDir;
                float worldZ = dist * zDir;
                
                int px = (int)((worldX / pixelToWorld) + png.Width / 2f);
                int py = (int)((-worldZ / pixelToWorld) + png.Height / 2f);
                
                if (px < 0 || px >= png.Width || py < 0 || py >= png.Height)
                    return dist; // Hit edge of image
                
                var pixel = png.GetPixel(px, py);
                
                // Check if void (black)
                if (pixel.R < 10 && pixel.G < 10 && pixel.B < 10)
                {
                    return xDir != 0 ? worldX : worldZ; // Return signed distance
                }
            }
            
            return 12000; // Never found void
        }
    }
}
