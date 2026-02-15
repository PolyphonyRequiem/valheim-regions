using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class FindBlackVoid
    {
        readonly ITestOutputHelper output;
        
        public FindBlackVoid(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void SampleOutwardToFindBlack()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            
            this.output.WriteLine("Sampling along horizontal line from center to right edge:");
            this.output.WriteLine("");
            
            bool foundBlack = false;
            int blackStartX = -1;
            
            for (int x = centerX; x < png.Width; x++)
            {
                var color = png.GetPixel(x, centerY);
                
                // Sample every 100 pixels or when we hit black
                if ((x - centerX) % 100 == 0 || (color.R == 0 && color.G == 0 && color.B == 0))
                {
                    this.output.WriteLine($"  x={x} (distance={x-centerX}): RGB({color.R},{color.G},{color.B})");
                    
                    if (color.R == 0 && color.G == 0 && color.B == 0 && !foundBlack)
                    {
                        foundBlack = true;
                        blackStartX = x;
                        this.output.WriteLine($"    *** FOUND BLACK at distance {x - centerX} pixels ***");
                    }
                }
            }
            
            if (!foundBlack)
            {
                this.output.WriteLine("");
                this.output.WriteLine("NO BLACK PIXELS FOUND - image fades but never goes to true black");
            }
            else
            {
                this.output.WriteLine("");
                this.output.WriteLine($"Black void starts at distance {blackStartX - centerX} pixels from center");
            }
        }
    }
}
