using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CheckNorthwestCorner
    {
        readonly ITestOutputHelper output;
        
        public CheckNorthwestCorner(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckFirst10PercentCorner()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            // First 10% of image = top-left 819x819 pixels (10% of 8192)
            int cornerSize = (int)(png.Width * 0.1);
            
            this.output.WriteLine($"Checking northwest corner: 0,0 to {cornerSize},{cornerSize}");
            this.output.WriteLine("");
            
            int totalPixels = 0;
            int blackPixels = 0;
            int nonBlackPixels = 0;
            
            for (int y = 0; y < cornerSize; y++)
            {
                for (int x = 0; x < cornerSize; x++)
                {
                    var color = png.GetPixel(x, y);
                    totalPixels++;
                    
                    if (color.R == 0 && color.G == 0 && color.B == 0)
                    {
                        blackPixels++;
                    }
                    else
                    {
                        nonBlackPixels++;
                    }
                }
            }
            
            double nonBlackPercent = (nonBlackPixels * 100.0) / totalPixels;
            
            this.output.WriteLine($"Total pixels: {totalPixels}");
            this.output.WriteLine($"Black pixels: {blackPixels} ({(blackPixels*100.0/totalPixels):F2}%)");
            this.output.WriteLine($"Non-black pixels: {nonBlackPixels} ({nonBlackPercent:F2}%)");
            this.output.WriteLine("");
            
            if (nonBlackPercent > 5.0)
            {
                this.output.WriteLine($"✓ Northwest corner has {nonBlackPercent:F2}% non-black (>5%)");
            }
            else
            {
                this.output.WriteLine($"✗ Northwest corner only has {nonBlackPercent:F2}% non-black (<5%)");
            }
        }
    }
}
