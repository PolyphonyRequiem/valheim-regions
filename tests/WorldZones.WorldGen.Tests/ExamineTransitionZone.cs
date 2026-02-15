using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class ExamineTransitionZone
    {
        readonly ITestOutputHelper output;
        
        public ExamineTransitionZone(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void ShowEveryPixelInTransition()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            
            this.output.WriteLine("Every pixel from 3490 to 3510:");
            this.output.WriteLine("");
            
            for (int dist = 3490; dist <= 3510; dist++)
            {
                int x = centerX + dist;
                var color = png.GetPixel(x, centerY);
                
                string marker = "";
                if (dist == 3492) marker = " <-- last detected biome color";
                if (dist == 3501) marker = " <-- first black pixel";
                
                this.output.WriteLine($"  distance {dist}: RGB({color.R},{color.G},{color.B}){marker}");
            }
        }
    }
}
