using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class PngOnlyTest
    {
        readonly ITestOutputHelper output;
        
        public PngOnlyTest(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void TestPngCoordinateMapping()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("TESTING PNG COORDINATE MAPPING ONLY");
            this.output.WriteLine("====================================");
            this.output.WriteLine($"PNG size: {png.Width}x{png.Height}");
            this.output.WriteLine("");
            
            // Known facts:
            // - Center (0,0) should be at pixel (4096, 4096) - we verified this
            // - Void boundary at ~3495 pixels from center = ~10,500m
            
            // Test different world size assumptions
            var worldSizes = new[] { 21000f, 24000f, 24609f };
            
            // Spawn location - we know from tile data this is Meadows
            float spawnX = -127f;
            float spawnZ = 262f;
            
            this.output.WriteLine($"Testing spawn location: world ({spawnX}, {spawnZ})");
            this.output.WriteLine("(Tile data says this is Meadows)");
            this.output.WriteLine("");
            
            foreach (var worldSize in worldSizes)
            {
                float pixelToWorld = worldSize / png.Width;
                int px = (int)((spawnX / pixelToWorld) + png.Width / 2f);
                int py = (int)((-spawnZ / pixelToWorld) + png.Height / 2f);
                
                if (px >= 0 && px < png.Width && py >= 0 && py < png.Height)
                {
                    var pixel = png.GetPixel(px, py);
                    var biome = ColorToBiome(pixel);
                    
                    this.output.WriteLine($"WorldSize={worldSize,6:F0}: pixel ({px,4}, {py,4}) = RGB({pixel.R:D3},{pixel.G:D3},{pixel.B:D3}) = {biome}");
                }
            }
            
            this.output.WriteLine("");
            this.output.WriteLine("Which worldSize shows Meadows at spawn?");
        }
        
        string ColorToBiome(Color c)
        {
            int tolerance = 5;
            if (IsColor(c, 0, 0, 153, tolerance)) return "Ocean";
            if (IsColor(c, 102, 102, 255, tolerance)) return "Shallows";
            if (IsColor(c, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(c, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(c, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(c, 255, 255, 255, tolerance)) return "Mountain";
            if (IsColor(c, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(c, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(c, 255, 0, 0, tolerance)) return "AshLands";
            return $"Unknown(RGB:{c.R},{c.G},{c.B})";
        }
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
    }
}
