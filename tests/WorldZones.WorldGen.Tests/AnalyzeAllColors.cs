using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class AnalyzeAllColors
    {
        readonly ITestOutputHelper output;
        
        public AnalyzeAllColors(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CountAllDistinctRgbValues()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine($"Scanning entire image: {png.Width}x{png.Height}");
            this.output.WriteLine("");
            
            var colorCounts = new Dictionary<(int r, int g, int b), int>();
            
            for (int y = 0; y < png.Height; y++)
            {
                for (int x = 0; x < png.Width; x++)
                {
                    var color = png.GetPixel(x, y);
                    var rgb = (color.R, color.G, color.B);
                    
                    if (!colorCounts.ContainsKey(rgb))
                        colorCounts[rgb] = 0;
                    colorCounts[rgb]++;
                }
            }
            
            var sortedColors = colorCounts.OrderByDescending(kvp => kvp.Value).ToList();
            
            this.output.WriteLine($"Found {sortedColors.Count} distinct RGB values");
            this.output.WriteLine("");
            this.output.WriteLine("Top colors by pixel count:");
            this.output.WriteLine("RGB            | Count      | Label");
            this.output.WriteLine("---------------|------------|-------------------");
            
            foreach (var kvp in sortedColors.Take(50))
            {
                var rgb = kvp.Key;
                string label = IdentifyColor(rgb.r, rgb.g, rgb.b);
                this.output.WriteLine($"RGB({rgb.r,3},{rgb.g,3},{rgb.b,3}) | {kvp.Value,10} | {label}");
            }
            
            if (sortedColors.Count > 50)
            {
                this.output.WriteLine($"... and {sortedColors.Count - 50} more colors");
            }
        }
        
        string IdentifyColor(int r, int g, int b)
        {
            int tolerance = 5;
            
            if (r == 0 && g == 0 && b == 0) return "Void";
            if (IsColor(r, g, b, 0, 0, 153, tolerance)) return "Ocean";
            if (IsColor(r, g, b, 102, 102, 255, tolerance)) return "Shallows";
            if (IsColor(r, g, b, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(r, g, b, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(r, g, b, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(r, g, b, 255, 255, 255, tolerance)) return "Mountain";
            if (IsColor(r, g, b, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(r, g, b, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(r, g, b, 255, 0, 0, tolerance)) return "AshLands";
            
            // Check if it's a gradient (equal RGB values suggest grayscale)
            if (r == g && g == b) return $"Grayscale gradient";
            
            // Check if it looks like ocean gradient (low R/G, higher B)
            if (r < 20 && g < 20 && b > 50) return "Ocean gradient";
            
            // Check if it looks like shallows gradient
            if (Math.Abs(r - g) < 10 && Math.Abs(r - b) < 10 && b > 150) return "Shallows gradient";
            
            return "Unknown";
        }
        
        bool IsColor(int cr, int cg, int cb, int r, int g, int b, int tolerance)
        {
            return Math.Abs(cr - r) <= tolerance && 
                   Math.Abs(cg - g) <= tolerance && 
                   Math.Abs(cb - b) <= tolerance;
        }
    }
}
