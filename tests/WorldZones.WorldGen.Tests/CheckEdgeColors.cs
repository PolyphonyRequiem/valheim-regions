using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CheckEdgeColors
    {
        readonly ITestOutputHelper output;
        
        public CheckEdgeColors(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void WhatColorsAreAtEdges()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int y = 4096;
            int leftEdge = 603;
            int rightEdge = 7588;
            
            this.output.WriteLine("Pixels around LEFT edge (603):");
            for (int offset = -5; offset <= 5; offset++)
            {
                int x = leftEdge + offset;
                var color = png.GetPixel(x, y);
                this.output.WriteLine($"  x={x}: RGB({color.R},{color.G},{color.B})");
            }
            this.output.WriteLine("");
            
            this.output.WriteLine("Pixels around RIGHT edge (7588):");
            for (int offset = -5; offset <= 5; offset++)
            {
                int x = rightEdge + offset;
                var color = png.GetPixel(x, y);
                this.output.WriteLine($"  x={x}: RGB({color.R},{color.G},{color.B})");
            }
        }
    }
}
