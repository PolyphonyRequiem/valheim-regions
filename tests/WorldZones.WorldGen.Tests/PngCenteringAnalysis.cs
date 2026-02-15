using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class PngCenteringAnalysis
    {
        readonly ITestOutputHelper output;
        
        public PngCenteringAnalysis(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void VerifyPngCentering()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            var metadata = LoadMetadata(dataPath);
            if (metadata == null) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("VERIFYING PNG CENTERING");
            this.output.WriteLine("=======================");
            this.output.WriteLine($"PNG size: {png.Width}x{png.Height}");
            this.output.WriteLine($"PNG center pixel: ({png.Width/2}, {png.Height/2})");
            this.output.WriteLine("");
            
            // We KNOW from tile data that world (0,0) is Ocean
            var tileSample = GetSampleAtCoordinate(dataPath, metadata, 0, 0);
            this.output.WriteLine($"World (0, 0) from tile data:");
            this.output.WriteLine($"  Biome: {BiomeFromValue(tileSample.biome)}");
            this.output.WriteLine($"  Height: {tileSample.height:F1}m");
            this.output.WriteLine("");
            
            // Now test different PNG world sizes and offsets to find what matches
            this.output.WriteLine("Testing different PNG mappings:");
            this.output.WriteLine("");
            
            var testConfigs = new[]
            {
                (18000f, 0, 0, "18000 units, centered"),
                (21000f, 0, 0, "21000 units, centered"),
                (18000f, 100, 100, "18000 units, offset +100px"),
                (18000f, -100, -100, "18000 units, offset -100px"),
            };
            
            foreach (var config in testConfigs)
            {
                float worldSize = config.Item1;
                int offsetX = config.Item2;
                int offsetY = config.Item3;
                string description = config.Item4;
                
                float pixelToWorld = worldSize / png.Width;
                int px = (int)((0 / pixelToWorld) + png.Width / 2f + offsetX);
                int py = (int)((-0 / pixelToWorld) + png.Height / 2f + offsetY);
                
                if (px >= 0 && px < png.Width && py >= 0 && py < png.Height)
                {
                    var pixel = png.GetPixel(px, py);
                    var pngBiome = ColorToBiome(pixel);
                    var match = pngBiome == BiomeFromValue(tileSample.biome) ? "✓" : "✗";
                    
                    this.output.WriteLine($"  {description}:");
                    this.output.WriteLine($"    Pixel: ({px}, {py}) = RGB({pixel.R},{pixel.G},{pixel.B})");
                    this.output.WriteLine($"    Biome: {pngBiome} {match}");
                }
            }
            
            this.output.WriteLine("");
            this.output.WriteLine("Checking void boundary with 18000 world size:");
            
            // Recheck void with corrected world size
            float pngWorldSize = 18000f;
            float voidNorth = FindVoid(png, 0, 1, pngWorldSize);
            float voidEast = FindVoid(png, 1, 0, pngWorldSize);
            
            this.output.WriteLine($"  Void North: {voidNorth:F0}m");
            this.output.WriteLine($"  Void East:  {voidEast:F0}m");
            this.output.WriteLine($"  Expected: ±9000m");
        }
        
        float FindVoid(Bitmap png, int xDir, int zDir, float pngWorldSize)
        {
            float pixelToWorld = pngWorldSize / png.Width;
            
            for (float dist = 0; dist < 12000; dist += 100)
            {
                float worldX = dist * xDir;
                float worldZ = dist * zDir;
                
                int px = (int)((worldX / pixelToWorld) + png.Width / 2f);
                int py = (int)((-worldZ / pixelToWorld) + png.Height / 2f);
                
                if (px < 0 || px >= png.Width || py < 0 || py >= png.Height)
                    return dist;
                
                var pixel = png.GetPixel(px, py);
                if (pixel.R < 10 && pixel.G < 10 && pixel.B < 10)
                    return xDir != 0 ? worldX : worldZ;
            }
            
            return 12000;
        }
        
        string ColorToBiome(Color c)
        {
            int tolerance = 3;
            if (c.R < 10 && c.G < 10 && c.B < 10) return "Void";
            if (IsColor(c, 0, 0, 153, tolerance)) return "Ocean";
            if (IsColor(c, 102, 102, 255, tolerance)) return "Shallows";
            if (IsColor(c, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(c, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(c, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(c, 255, 255, 255, tolerance)) return "Mountain";
            if (IsColor(c, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(c, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(c, 255, 0, 0, tolerance)) return "AshLands";
            return "Unknown";
        }
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
        
        class MapMetadata
        {
            public string? WorldSeed { get; set; }
            public int TileRowCount { get; set; }
            public float TileSize { get; set; }
            public int TileSideCount { get; set; }
            public float WorldWidth { get; set; }
        }
        
        struct MapSample
        {
            public ushort biome;
            public float height;
            public float forestFactor;
        }
        
        MapMetadata? LoadMetadata(string dataPath)
        {
            var mapJsonPath = Path.Combine(dataPath, "map.json");
            if (!File.Exists(mapJsonPath)) return null;
            return JsonSerializer.Deserialize<MapMetadata>(File.ReadAllText(mapJsonPath));
        }
        
        MapSample GetSampleAtCoordinate(string dataPath, MapMetadata metadata, float worldX, float worldZ)
        {
            float halfWorld = metadata.WorldWidth / 2f;
            float tileWorldX = worldX + halfWorld;
            float tileWorldZ = worldZ + halfWorld;
            
            int tileX = (int)(tileWorldX / metadata.TileSize);
            int tileZ = (int)(tileWorldZ / metadata.TileSize);
            
            tileX = Math.Max(0, Math.Min(metadata.TileSideCount - 1, tileX));
            tileZ = Math.Max(0, Math.Min(metadata.TileSideCount - 1, tileZ));
            
            float localX = tileWorldX - (tileX * metadata.TileSize);
            float localZ = tileWorldZ - (tileZ * metadata.TileSize);
            
            int sampleX = (int)((localX / metadata.TileSize) * metadata.TileRowCount);
            int sampleZ = (int)((localZ / metadata.TileSize) * metadata.TileRowCount);
            
            sampleX = Math.Max(0, Math.Min(metadata.TileRowCount - 1, sampleX));
            sampleZ = Math.Max(0, Math.Min(metadata.TileRowCount - 1, sampleZ));
            
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            int index = sampleX * metadata.TileRowCount + sampleZ;
            return samples[index];
        }
        
        MapSample[] LoadTile(string tilePath, int tileRowCount)
        {
            using var fileStream = File.OpenRead(tilePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);
            
            var bytes = memoryStream.GetBuffer();
            int sampleCount = tileRowCount * tileRowCount;
            var samples = new MapSample[sampleCount];
            
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * 10;
                samples[i].biome = BitConverter.ToUInt16(bytes, offset + 0);
                samples[i].height = BitConverter.ToSingle(bytes, offset + 2);
                samples[i].forestFactor = BitConverter.ToSingle(bytes, offset + 6);
            }
            
            return samples;
        }
        
        string BiomeFromValue(ushort value)
        {
            return value switch
            {
                0 => "None",
                1 => "Meadows",
                2 => "Swamp",
                4 => "Mountain",
                8 => "BlackForest",
                16 => "Plains",
                32 => "AshLands",
                64 => "DeepNorth",
                256 => "Ocean",
                512 => "Mistlands",
                _ => $"Unknown({value})"
            };
        }
    }
}
