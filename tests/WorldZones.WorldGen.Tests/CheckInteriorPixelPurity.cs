using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CheckInteriorPixelPurity
    {
        readonly ITestOutputHelper output;
        
        public CheckInteriorPixelPurity(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckAllPixelsWithinRadius3490()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            int maxRadius = 3490;
            
            this.output.WriteLine($"Checking all pixels within radius {maxRadius} from center ({centerX}, {centerY})");
            this.output.WriteLine("");
            
            var biomeCounts = new Dictionary<string, int>();
            int gradientPixels = 0;
            int voidPixels = 0;
            int totalPixels = 0;
            
            var distinctColors = new Dictionary<(int r, int g, int b), int>();
            
            for (int y = centerY - maxRadius; y <= centerY + maxRadius; y++)
            {
                for (int x = centerX - maxRadius; x <= centerX + maxRadius; x++)
                {
                    if (x < 0 || x >= png.Width || y < 0 || y >= png.Height) continue;
                    
                    double distance = Math.Sqrt(Math.Pow(x - centerX, 2) + Math.Pow(y - centerY, 2));
                    if (distance > maxRadius) continue;
                    
                    totalPixels++;
                    var color = png.GetPixel(x, y);
                    var rgb = (color.R, color.G, color.B);
                    
                    if (!distinctColors.ContainsKey(rgb))
                        distinctColors[rgb] = 0;
                    distinctColors[rgb]++;
                    
                    // Check if it's a pure biome color
                    var biome = GetExactBiomeMatch(color.R, color.G, color.B);
                    
                    if (biome != null)
                    {
                        if (!biomeCounts.ContainsKey(biome))
                            biomeCounts[biome] = 0;
                        biomeCounts[biome]++;
                    }
                    else if (color.R == 0 && color.G == 0 && color.B == 0)
                    {
                        voidPixels++;
                    }
                    else
                    {
                        gradientPixels++;
                    }
                }
            }
            
            this.output.WriteLine($"Total pixels sampled: {totalPixels:N0}");
            this.output.WriteLine($"Distinct RGB values: {distinctColors.Count}");
            this.output.WriteLine("");
            
            this.output.WriteLine("Pure biome color pixels:");
            foreach (var kvp in biomeCounts.OrderByDescending(k => k.Value))
            {
                double pct = (kvp.Value * 100.0) / totalPixels;
                this.output.WriteLine($"  {kvp.Key,-15}: {kvp.Value,10:N0} ({pct:F2}%)");
            }
            
            int totalBiomePixels = biomeCounts.Values.Sum();
            double biomePct = (totalBiomePixels * 100.0) / totalPixels;
            double gradientPct = (gradientPixels * 100.0) / totalPixels;
            double voidPct = (voidPixels * 100.0) / totalPixels;
            
            this.output.WriteLine("");
            this.output.WriteLine($"Total pure biome pixels: {totalBiomePixels:N0} ({biomePct:F2}%)");
            this.output.WriteLine($"Gradient/anti-aliased pixels: {gradientPixels:N0} ({gradientPct:F2}%)");
            this.output.WriteLine($"Void pixels: {voidPixels:N0} ({voidPct:F2}%)");
            this.output.WriteLine("");
            
            if (voidPixels > 0)
            {
                this.output.WriteLine($"⚠ Found {voidPixels} void pixels inside map boundary!");
            }
            
            if (gradientPct > 10)
            {
                this.output.WriteLine($"⚠ High gradient percentage ({gradientPct:F2}%) - significant anti-aliasing inside map");
            }
            else if (gradientPct > 1)
            {
                this.output.WriteLine($"✓ Moderate gradient percentage ({gradientPct:F2}%) - some biome transitions have anti-aliasing");
            }
            else
            {
                this.output.WriteLine($"✓ Low gradient percentage ({gradientPct:F2}%) - mostly pure biome colors");
            }
        }
        
        string GetExactBiomeMatch(int r, int g, int b)
        {
            int tolerance = 3;
            
            if (IsColor(r, g, b, 0, 0, 153, tolerance)) return "Ocean";
            if (IsColor(r, g, b, 102, 102, 255, tolerance)) return "Shallows";
            if (IsColor(r, g, b, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(r, g, b, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(r, g, b, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(r, g, b, 255, 255, 255, tolerance)) return "Mountain";
            if (IsColor(r, g, b, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(r, g, b, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(r, g, b, 255, 0, 0, tolerance)) return "AshLands";
            
            return null;
        }
        
        bool IsColor(int cr, int cg, int cb, int r, int g, int b, int tolerance)
        {
            return Math.Abs(cr - r) <= tolerance && 
                   Math.Abs(cg - g) <= tolerance && 
                   Math.Abs(cb - b) <= tolerance;
        }
    }
}
