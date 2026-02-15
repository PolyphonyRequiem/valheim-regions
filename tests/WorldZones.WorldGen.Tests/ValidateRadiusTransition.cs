using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class ValidateRadiusTransition
    {
        readonly ITestOutputHelper output;
        
        public ValidateRadiusTransition(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void TestRadiusBoundary()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            int radiusBiome = 3491;
            int radiusVoid = 3512;
            
            this.output.WriteLine($"Testing radius {radiusBiome} (biome) vs {radiusVoid} (void) at 360 angles");
            this.output.WriteLine($"Center: ({centerX}, {centerY})");
            this.output.WriteLine("");
            
            int biomeCountAt3491 = 0;
            int voidCountAt3491 = 0;
            int biomeCountAt3502 = 0;
            int voidCountAt3502 = 0;
            
            var voidAnglesAt3502 = new System.Collections.Generic.List<int>();
            
            for (int angle = 0; angle < 360; angle++)
            {
                double rad = angle * Math.PI / 180.0;
                
                // Sample at radius 3491 (should be biome)
                int x1 = centerX + (int)(radiusBiome * Math.Cos(rad));
                int y1 = centerY + (int)(radiusBiome * Math.Sin(rad));
                var color1 = png.GetPixel(x1, y1);
                bool isBiome1 = IsBiomeColor(color1);
                bool isVoid1 = IsVoid(color1);
                
                if (isBiome1) biomeCountAt3491++;
                if (isVoid1) voidCountAt3491++;
                
                // Sample at radius 3502 (should be void)
                int x2 = centerX + (int)(radiusVoid * Math.Cos(rad));
                int y2 = centerY + (int)(radiusVoid * Math.Sin(rad));
                var color2 = png.GetPixel(x2, y2);
                bool isBiome2 = IsBiomeColor(color2);
                bool isVoid2 = IsVoid(color2);
                
                if (isBiome2) biomeCountAt3502++;
                if (isVoid2) 
                {
                    voidCountAt3502++;
                    voidAnglesAt3502.Add(angle);
                }
            }
            
            this.output.WriteLine($"At radius {radiusBiome} (last biome content):");
            this.output.WriteLine($"  Biome colors: {biomeCountAt3491}/360");
            this.output.WriteLine($"  Void: {voidCountAt3491}/360");
            this.output.WriteLine("");
            
            this.output.WriteLine($"At radius {radiusVoid} (first void):");
            this.output.WriteLine($"  Biome colors: {biomeCountAt3502}/360");
            this.output.WriteLine($"  Void: {voidCountAt3502}/360");
            this.output.WriteLine("");
            
            if (voidCountAt3502 > 0)
            {
                this.output.WriteLine($"Void found at angles: {string.Join(", ", voidAnglesAt3502)}");
                this.output.WriteLine("");
            }
            
            if (biomeCountAt3491 == 360 && voidCountAt3502 == 360)
            {
                this.output.WriteLine($"✓ PERFECT: All biomes at {radiusBiome}, all void at {radiusVoid}");
            }
            else
            {
                this.output.WriteLine("✗ Boundaries don't match expectations");
            }
        }
        
        bool IsBiomeColor(Color c)
        {
            int tolerance = 5;
            
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
        
        bool IsVoid(Color c)
        {
            return c.R == 0 && c.G == 0 && c.B == 0;
        }
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
    }
}
