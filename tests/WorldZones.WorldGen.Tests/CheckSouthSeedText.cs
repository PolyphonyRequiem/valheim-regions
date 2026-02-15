using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CheckSouthSeedText
    {
        readonly ITestOutputHelper output;
        
        public CheckSouthSeedText(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckMiddle50PercentOfBottom5Percent()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            // Bottom 5% = last 410 pixels in Y (95% to 100%)
            int startY = (int)(png.Height * 0.95);
            int endY = png.Height;
            
            // Middle 50% in X = 25% to 75% of width
            int startX = (int)(png.Width * 0.25);
            int endX = (int)(png.Width * 0.75);
            
            this.output.WriteLine($"Checking middle 50% of bottom 5%:");
            this.output.WriteLine($"  X range: {startX} to {endX}");
            this.output.WriteLine($"  Y range: {startY} to {endY}");
            this.output.WriteLine("");
            
            int totalPixels = 0;
            int blackPixels = 0;
            int nonBlackPixels = 0;
            
            for (int y = startY; y < endY; y++)
            {
                for (int x = startX; x < endX; x++)
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
                this.output.WriteLine($"✓ South seed text area has {nonBlackPercent:F2}% non-black");
            }
            else
            {
                this.output.WriteLine($"✗ South seed text area only has {nonBlackPercent:F2}% non-black");
            }
        }
    }
}
