using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CheckNorthRegions
    {
        readonly ITestOutputHelper output;
        
        public CheckNorthRegions(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckNortheastAndNorthMiddle()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int cornerSize = (int)(png.Width * 0.1);
            
            // NORTHEAST: top-right 10% corner
            this.output.WriteLine($"NORTHEAST CORNER (top-right 10%):");
            this.output.WriteLine($"  X range: {png.Width - cornerSize} to {png.Width}");
            this.output.WriteLine($"  Y range: 0 to {cornerSize}");
            
            int totalNE = 0;
            int blackNE = 0;
            
            for (int y = 0; y < cornerSize; y++)
            {
                for (int x = png.Width - cornerSize; x < png.Width; x++)
                {
                    var color = png.GetPixel(x, y);
                    totalNE++;
                    if (color.R == 0 && color.G == 0 && color.B == 0) blackNE++;
                }
            }
            
            double blackPercentNE = (blackNE * 100.0) / totalNE;
            this.output.WriteLine($"  Black: {blackNE}/{totalNE} ({blackPercentNE:F2}%)");
            this.output.WriteLine("");
            
            // NORTH MIDDLE: top 5%, middle 50%
            int topHeight = (int)(png.Height * 0.05);
            int startX = (int)(png.Width * 0.25);
            int endX = (int)(png.Width * 0.75);
            
            this.output.WriteLine($"NORTH MIDDLE (top 5%, middle 50%):");
            this.output.WriteLine($"  X range: {startX} to {endX}");
            this.output.WriteLine($"  Y range: 0 to {topHeight}");
            
            int totalNM = 0;
            int blackNM = 0;
            
            for (int y = 0; y < topHeight; y++)
            {
                for (int x = startX; x < endX; x++)
                {
                    var color = png.GetPixel(x, y);
                    totalNM++;
                    if (color.R == 0 && color.G == 0 && color.B == 0) blackNM++;
                }
            }
            
            double blackPercentNM = (blackNM * 100.0) / totalNM;
            this.output.WriteLine($"  Black: {blackNM}/{totalNM} ({blackPercentNM:F2}%)");
            this.output.WriteLine("");
            
            if (blackPercentNE > 95.0 && blackPercentNM > 95.0)
            {
                this.output.WriteLine("✓ Both regions are >95% black as expected");
            }
            else
            {
                this.output.WriteLine("✗ Regions don't meet 95% black threshold");
            }
        }
    }
}
