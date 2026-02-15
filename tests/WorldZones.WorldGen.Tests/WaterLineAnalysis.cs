using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class WaterLineAnalysis
    {
        readonly ITestOutputHelper output;
        
        public WaterLineAnalysis(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void FindWaterLine()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            var metadata = LoadMetadata(dataPath);
            if (metadata == null) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("FINDING WATER LINE HEIGHT");
            this.output.WriteLine("=========================");
            this.output.WriteLine("");
            
            var aboveWater = new System.Collections.Generic.List<float>();
            var shallows = new System.Collections.Generic.List<float>();
            var ocean = new System.Collections.Generic.List<float>();
            
            // Sample grid
            for (float x = -6000; x <= 6000; x += 1000)
            {
                for (float z = -6000; z <= 6000; z += 1000)
                {
                    var pngBiome = GetBiomeFromPNG(png, x, z);
                    var tileSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                    var tileBiome = BiomeFromValue(tileSample.biome);
                    var height = tileSample.height;
                    
                    if (pngBiome == "Shallows")
                    {
                        shallows.Add(height);
                    }
                    else if (pngBiome == "Ocean" && tileBiome == "Ocean")
                    {
                        ocean.Add(height);
                    }
                    else if (pngBiome != "Ocean" && pngBiome != "Unknown" && tileBiome != "Ocean")
                    {
                        aboveWater.Add(height);
                    }
                }
            }
            
            this.output.WriteLine($"Ocean (PNG=Ocean, Tile=Ocean): {ocean.Count} samples");
            if (ocean.Count > 0)
            {
                ocean.Sort();
                this.output.WriteLine($"  Min: {ocean[0]:F1}m, Max: {ocean[ocean.Count-1]:F1}m, Median: {ocean[ocean.Count/2]:F1}m");
            }
            
            this.output.WriteLine("");
            this.output.WriteLine($"Shallows (PNG=light blue): {shallows.Count} samples");
            if (shallows.Count > 0)
            {
                shallows.Sort();
                this.output.WriteLine($"  Min: {shallows[0]:F1}m, Max: {shallows[shallows.Count-1]:F1}m, Median: {shallows[shallows.Count/2]:F1}m");
            }
            
            this.output.WriteLine("");
            this.output.WriteLine($"Above water (PNG shows biome): {aboveWater.Count} samples");
            if (aboveWater.Count > 0)
            {
                aboveWater.Sort();
                this.output.WriteLine($"  Min: {aboveWater[0]:F1}m, Max: {aboveWater[aboveWater.Count-1]:F1}m, Median: {aboveWater[aboveWater.Count/2]:F1}m");
            }
            
            this.output.WriteLine("");
            this.output.WriteLine("CONCLUSION:");
            if (shallows.Count > 0 && aboveWater.Count > 0)
            {
                this.output.WriteLine($"  Water line appears to be around {aboveWater[0]:F1}m");
                this.output.WriteLine($"  Below ~{aboveWater[0]:F1}m: PNG shows Shallows or Ocean");
                this.output.WriteLine($"  Above ~{aboveWater[0]:F1}m: PNG shows actual biome");
            }
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
        
        string ColorToBiome(Color c)
        {
            int tolerance = 3;
            
            if (IsColor(c, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(c, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(c, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(c, 255, 255, 255, tolerance)) return "Mountain";
            if (IsColor(c, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(c, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(c, 255, 0, 0, tolerance)) return "AshLands";
            if (IsColor(c, 102, 102, 255, tolerance)) return "Shallows";
            if (IsColor(c, 0, 0, 153, tolerance)) return "Ocean";
            
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
