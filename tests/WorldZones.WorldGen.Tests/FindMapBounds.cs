using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class FindMapBounds
    {
        readonly ITestOutputHelper output;
        
        public FindMapBounds(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void LocateMapCenterAndRadius()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("EMPIRICALLY LOCATING MAP CENTER & RADIUS");
            this.output.WriteLine("=========================================");
            this.output.WriteLine($"PNG size: {png.Width}x{png.Height}");
            this.output.WriteLine("");
            
            // Scan horizontal line through middle (y=4096)
            int scanY = png.Height / 2;
            int leftEdge = -1;
            int rightEdge = -1;
            
            this.output.WriteLine($"Scanning horizontal line y={scanY}:");
            for (int x = 0; x < png.Width; x++)
            {
                var pixel = png.GetPixel(x, scanY);
                if (IsBiomeColor(pixel))
                {
                    if (leftEdge == -1) leftEdge = x;
                    rightEdge = x;
                }
            }
            
            int centerX = (leftEdge + rightEdge) / 2;
            int radiusX = (rightEdge - leftEdge) / 2;
            
            this.output.WriteLine($"  Left edge: {leftEdge}");
            this.output.WriteLine($"  Right edge: {rightEdge}");
            this.output.WriteLine($"  Center X: {centerX}");
            this.output.WriteLine($"  Radius X: {radiusX}");
            this.output.WriteLine("");
            
            // Scan vertical line through middle (x=4096)
            int scanX = png.Width / 2;
            int topEdge = -1;
            int bottomEdge = -1;
            
            this.output.WriteLine($"Scanning vertical line x={scanX}:");
            for (int y = 0; y < png.Height; y++)
            {
                var pixel = png.GetPixel(scanX, y);
                if (IsBiomeColor(pixel))
                {
                    if (topEdge == -1) topEdge = y;
                    bottomEdge = y;
                }
            }
            
            int centerY = (topEdge + bottomEdge) / 2;
            int radiusY = (bottomEdge - topEdge) / 2;
            
            this.output.WriteLine($"  Top edge: {topEdge}");
            this.output.WriteLine($"  Bottom edge: {bottomEdge}");
            this.output.WriteLine($"  Center Y: {centerY}");
            this.output.WriteLine($"  Radius Y: {radiusY}");
            this.output.WriteLine("");
            
            this.output.WriteLine("RESULTS:");
            this.output.WriteLine($"  Map center: ({centerX}, {centerY})");
            this.output.WriteLine($"  Average radius: {(radiusX + radiusY) / 2}");
            
            if (Math.Abs(centerX - png.Width/2) < 10 && Math.Abs(centerY - png.Height/2) < 10)
            {
                this.output.WriteLine($"  ✓ Center is approximately at image center ({png.Width/2}, {png.Height/2})");
            }
            else
            {
                this.output.WriteLine($"  ✗ Center is offset from image center by ({centerX - png.Width/2}, {centerY - png.Height/2}) pixels");
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
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
    }
}
