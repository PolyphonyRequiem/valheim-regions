using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class PngColorAnalysis
    {
        readonly ITestOutputHelper output;
        
        public PngColorAnalysis(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void AnalyzePngColors()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            var metadata = LoadMetadata(dataPath);
            if (metadata == null) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("ANALYZING PNG COLORS AT KNOWN TILE BIOMES");
            this.output.WriteLine("==========================================");
            this.output.WriteLine("");
            
            // Test coordinates in center of map (away from edges)
            var testPoints = new[]
            {
                (0f, 0f, "Center"),
                (-686f, 1744f, "Ocean1"),
                (-127f, 262f, "Spawn"),
                (1000f, 1000f, "NE-Center"),
                (-1000f, -1000f, "SW-Center"),
                (2000f, 0f, "East"),
                (0f, 2000f, "North"),
            };
            
            foreach (var (x, z, label) in testPoints)
            {
                var tileSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                var tileBiome = BiomeFromValue(tileSample.biome);
                
                float pngWorldSize = 21000f;
                float pixelToWorld = pngWorldSize / png.Width;
                int px = (int)((x / pixelToWorld) + png.Width / 2f);
                int py = (int)((-z / pixelToWorld) + png.Height / 2f);
                
                if (px >= 0 && px < png.Width && py >= 0 && py < png.Height)
                {
                    var pixel = png.GetPixel(px, py);
                    var pngBiome = ColorToBiome(pixel);
                    
                    var match = pngBiome == tileBiome ? "✓" : "✗";
                    
                    this.output.WriteLine($"{label,-15} ({x,6:F0}, {z,6:F0})");
                    this.output.WriteLine($"  Tile: {tileBiome,-12} (value={tileSample.biome})");
                    this.output.WriteLine($"  PNG:  {pngBiome,-12} RGB({pixel.R:D3},{pixel.G:D3},{pixel.B:D3}) {match}");
                    this.output.WriteLine("");
                }
            }
            
            // Now sample unique colors
            this.output.WriteLine("UNIQUE COLORS IN PNG (sampling grid):");
            this.output.WriteLine("=====================================");
            var colorCounts = new System.Collections.Generic.Dictionary<string, int>();
            
            for (int px = 0; px < png.Width; px += 100)
            {
                for (int py = 0; py < png.Height; py += 100)
                {
                    var pixel = png.GetPixel(px, py);
                    var key = $"({pixel.R:D3},{pixel.G:D3},{pixel.B:D3})";
                    if (!colorCounts.ContainsKey(key))
                        colorCounts[key] = 0;
                    colorCounts[key]++;
                }
            }
            
            var sorted = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int>>(colorCounts);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
            
            this.output.WriteLine($"Found {colorCounts.Count} unique colors (top 20):");
            for (int i = 0; i < Math.Min(20, sorted.Count); i++)
            {
                this.output.WriteLine($"  {sorted[i].Key}: {sorted[i].Value} samples");
            }
        }
        
        string ColorToBiome(Color c)
        {
            // Current loose matching
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
