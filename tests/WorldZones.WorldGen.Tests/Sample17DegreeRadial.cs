using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class Sample17DegreeRadial
    {
        readonly ITestOutputHelper output;
        
        public Sample17DegreeRadial(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void SampleEveryPixelAt17Degrees()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            double angle = 17.0; // degrees
            double rad = angle * Math.PI / 180.0;
            
            this.output.WriteLine($"Sampling at angle {angle}° (radians: {rad:F4})");
            this.output.WriteLine($"Center: ({centerX}, {centerY})");
            this.output.WriteLine("");
            this.output.WriteLine("Distance | RGB");
            this.output.WriteLine("---------|------------");
            
            for (int distance = 3490; distance <= 3515; distance++)
            {
                int x = centerX + (int)(distance * Math.Cos(rad));
                int y = centerY + (int)(distance * Math.Sin(rad));
                
                var color = png.GetPixel(x, y);
                this.output.WriteLine($"{distance,4} | RGB({color.R},{color.G},{color.B})");
            }
        }
    }
}
