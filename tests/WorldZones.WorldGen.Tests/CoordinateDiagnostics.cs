using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CoordinateDiagnostics
    {
        readonly ITestOutputHelper output;
        
        public CoordinateDiagnostics(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void DiagnoseCoordinateMapping()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            var metadata = LoadMetadata(dataPath);
            if (metadata == null) return;
            
            using var png = new Bitmap(pngPath);
            
            // Test world (0,0) and other known points
            TestCoordinate(png, dataPath, metadata, 0, 0);
            TestCoordinate(png, dataPath, metadata, -686, 1744);
            TestCoordinate(png, dataPath, metadata, 2564, -1189);
            
            // Test spawn location
            TestCoordinate(png, dataPath, metadata, -127, 262);
            
            // Test a grid to find pattern
            this.output.WriteLine("\n========== GRID TEST ==========");
            int matches = 0;
            int total = 0;
            
            for (float x = -6000; x <= 6000; x += 2000)
            {
                for (float z = -6000; z <= 6000; z += 2000)
                {
                    var pngBiome = GetBiomeFromPNG(png, x, z);
                    var tileSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                    var tileBiome = BiomeFromValue(tileSample.biome);
                    
                    if (pngBiome != "Unknown")
                    {
                        total++;
                        if (pngBiome == tileBiome) matches++;
                    }
                }
            }
            
            this.output.WriteLine($"Grid matches: {matches}/{total} ({100f*matches/total:F1}%)");
            
            Assert.True(true);
        }
        
        void TestCoordinate(Bitmap png, string dataPath, MapMetadata metadata, float worldX, float worldZ)
        {
            this.output.WriteLine($"========== World ({worldX}, {worldZ}) ==========");
            
            // PNG reading
            float pngWorldSize = 21000f;
            float pixelToWorld = pngWorldSize / png.Width;
            int px = (int)((worldX / pixelToWorld) + png.Width / 2f);
            int py = (int)((-worldZ / pixelToWorld) + png.Height / 2f);  // Z-flip for PNG
            var pixel = png.GetPixel(px, py);
            string pngBiome = ColorToBiome(pixel);
            
            this.output.WriteLine($"PNG: pixel ({px}, {py}) = RGB({pixel.R},{pixel.G},{pixel.B}) = {pngBiome}");
            
            // Tile reading - detailed trace
            float halfWorld = metadata.WorldWidth / 2f;
            float tileWorldX = worldX + halfWorld;
            float tileWorldZ = worldZ + halfWorld;
            
            this.output.WriteLine($"Tile world coords: ({tileWorldX:F2}, {tileWorldZ:F2})");
            
            int tileX = (int)(tileWorldX / metadata.TileSize);
            int tileZ = (int)(tileWorldZ / metadata.TileSize);
            
            this.output.WriteLine($"Tile indices: ({tileX}, {tileZ})");
            
            float localX = tileWorldX - (tileX * metadata.TileSize);
            float localZ = tileWorldZ - (tileZ * metadata.TileSize);
            
            this.output.WriteLine($"Local coords: ({localX:F2}, {localZ:F2})");
            
            int sampleX = (int)((localX / metadata.TileSize) * metadata.TileRowCount);
            int sampleZ = (int)((localZ / metadata.TileSize) * metadata.TileRowCount);
            
            this.output.WriteLine($"Sample indices: ({sampleX}, {sampleZ})");
            
            int index = sampleX * metadata.TileRowCount + sampleZ;
            this.output.WriteLine($"Array index: {index}");
            
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            var sample = samples[index];
            string tileBiome = BiomeFromValue(sample.biome);
            
            this.output.WriteLine($"Tile: biome={sample.biome} ({tileBiome}), height={sample.height:F1}");
            
            if (pngBiome == tileBiome)
            {
                this.output.WriteLine("✓ MATCH!");
            }
            else
            {
                this.output.WriteLine($"✗ MISMATCH: PNG={pngBiome}, Tile={tileBiome}");
            }
            this.output.WriteLine("");
        }
        
        string GetBiomeFromPNG(Bitmap png, float worldX, float worldZ)
        {
            float pngWorldSize = 21000f;
            float pixelToWorld = pngWorldSize / png.Width;
            int px = (int)((worldX / pixelToWorld) + png.Width / 2f);
            int py = (int)((-worldZ / pixelToWorld) + png.Height / 2f);
            
            if (px < 0 || px >= png.Width || py < 0 || py >= png.Height)
                return "OutOfBounds";
            
            var pixel = png.GetPixel(px, py);
            return ColorToBiome(pixel);
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
        
        string ColorToBiome(Color c)
        {
            if (Math.Abs(c.R - 0) < 15 && Math.Abs(c.G - 0) < 15 && c.B > 140 && c.B < 170) return "Ocean";
            if (c.R > 240 && c.G > 240 && c.B > 240) return "Mountain";
            if (c.R > 170 && c.R < 220 && c.G > 170 && c.G < 220 && c.B > 30 && c.B < 80) return "Meadows";
            if (c.R > 20 && c.R < 70 && c.G > 70 && c.G < 110 && c.B > 40 && c.B < 80) return "BlackForest";
            if (c.R > 140 && c.R < 200 && c.G > 90 && c.G < 140 && c.B > 60 && c.B < 110) return "Swamp";
            if (c.R > 190 && c.G > 190 && c.B < 60) return "Plains";
            if (c.R > 85 && c.R < 125 && c.G > 85 && c.G < 125 && c.B > 85 && c.B < 125) return "Mistlands";
            return "Unknown";
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
            
            var json = File.ReadAllText(mapJsonPath);
            return JsonSerializer.Deserialize<MapMetadata>(json);
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
