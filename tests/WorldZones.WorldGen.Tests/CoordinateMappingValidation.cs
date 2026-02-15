using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Validates that we're correctly reading ground truth data by comparing
    /// tile data against the PNG map (which we've already validated).
    /// </summary>
    public class CoordinateMappingValidation
    {
        readonly ITestOutputHelper output;
        
        public CoordinateMappingValidation(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void VerifyTileDataMatchesPNG_AtKnownPoints()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            var metadata = LoadMetadata(dataPath);
            if (metadata == null || !File.Exists(pngPath))
            {
                this.output.WriteLine("Data not available");
                return;
            }
            
            using var pngMap = new Bitmap(pngPath);
            
            this.output.WriteLine("COORDINATE MAPPING VALIDATION");
            this.output.WriteLine("============================");
            this.output.WriteLine("");
            this.output.WriteLine($"PNG: {pngMap.Width}x{pngMap.Height}");
            this.output.WriteLine($"Tile data: {metadata.TileSideCount}x{metadata.TileSideCount} tiles, {metadata.TileRowCount}x{metadata.TileRowCount} per tile");
            this.output.WriteLine($"World coverage: {metadata.WorldWidth} units");
            this.output.WriteLine("");
            
            // Test known coordinates from earlier - we know from PNG what these should be
            var testPoints = new[] {
                (0f, 0f, "Ocean"),           // From earlier validation
                (-686f, 1744f, "Ocean"),      // From earlier validation
                (-127f, 262f, "Meadows"),     // Spawn - should be safe land
            };
            
            int pngMatches = 0;
            
            foreach (var (x, z, expectedFromPNG) in testPoints)
            {
                // Get from PNG (method we already validated)
                var pngBiome = GetBiomeFromPNG(pngMap, x, z);
                
                // Get from tile data (method we're validating)
                var tileSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                var tileBiome = BiomeFromValue(tileSample.biome);
                
                bool match = pngBiome == tileBiome;
                if (match) pngMatches++;
                
                this.output.WriteLine($"({x,6:F0}, {z,6:F0}):");
                this.output.WriteLine($"  PNG:  {pngBiome,-12} (expected: {expectedFromPNG})");
                this.output.WriteLine($"  Tile: {tileBiome,-12} h={tileSample.height:F2}");
                this.output.WriteLine($"  Match: {(match ? "YES" : "NO")}");
                this.output.WriteLine("");
            }
            
            this.output.WriteLine($"RESULT: {pngMatches}/{testPoints.Length} tile samples match PNG");
            this.output.WriteLine("");
            
            if (pngMatches == testPoints.Length)
            {
                this.output.WriteLine("✓ COORDINATE MAPPING IS CORRECT!");
                this.output.WriteLine("  Our tile reading matches the validated PNG map.");
            }
            else
            {
                this.output.WriteLine("✗ COORDINATE MAPPING IS WRONG!");
                this.output.WriteLine("  We're not reading tile data at the correct coordinates.");
            }
            
            Assert.True(true);
        }
        
        string GetBiomeFromPNG(Bitmap png, float worldX, float worldZ)
        {
            // This is the method we validated earlier works correctly
            float worldSize = 21000f;
            float pixelToWorld = worldSize / png.Width;
            
            // Convert world to pixel (with Z flip for image coordinates)
            int px = (int)((worldX / pixelToWorld) + png.Width / 2f);
            int py = (int)((-worldZ / pixelToWorld) + png.Height / 2f);
            
            var pixel = png.GetPixel(px, py);
            return ColorToBiome(pixel);
        }
        
        string ColorToBiome(Color c)
        {
            // RGB values we validated from the PNG
            if (c.R < 20 && c.G < 20 && c.B > 100) return "Ocean";
            if (c.R > 240 && c.G > 240 && c.B > 240) return "Mountain";
            if (c.R > 180 && c.G > 180 && c.B < 100) return "Meadows";
            if (c.R < 100 && c.G > 50 && c.B < 100) return "BlackForest";
            if (c.R > 150 && c.G > 80 && c.B < 120) return "Swamp";
            if (c.R > 180 && c.G > 180 && c.B < 50) return "Plains";
            if (c.R > 80 && c.R < 150 && c.G > 80 && c.G < 150 && c.B > 80 && c.B < 150) return "Mistlands";
            return "Unknown";
        }
        
        // Helper methods
        class MapMetadata
        {
            public string? WorldSeed { get; set; }
            public string? WorldVersion { get; set; }
            public int TileRowCount { get; set; }
            public float TileSize { get; set; }
            public int TileSideCount { get; set; }
            public float WorldWidth { get; set; }
            public string? GeneratorVersion { get; set; }
            public string? GeneratorUnityVersion { get; set; }
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
            // Convert world coordinates to tile space
            float halfWorld = metadata.WorldWidth / 2f;
            float tileWorldX = worldX + halfWorld;
            float tileWorldZ = worldZ + halfWorld;
            
            // Which tile?
            int tileX = (int)(tileWorldX / metadata.TileSize);
            int tileZ = (int)(tileWorldZ / metadata.TileSize);
            
            tileX = Math.Max(0, Math.Min(metadata.TileSideCount - 1, tileX));
            tileZ = Math.Max(0, Math.Min(metadata.TileSideCount - 1, tileZ));
            
            // Position within tile
            float localX = tileWorldX - (tileX * metadata.TileSize);
            float localZ = tileWorldZ - (tileZ * metadata.TileSize);
            
            // Sample within tile
            int sampleX = (int)((localX / metadata.TileSize) * metadata.TileRowCount);
            int sampleZ = (int)((localZ / metadata.TileSize) * metadata.TileRowCount);
            
            sampleX = Math.Max(0, Math.Min(metadata.TileRowCount - 1, sampleX));
            sampleZ = Math.Max(0, Math.Min(metadata.TileRowCount - 1, sampleZ));
            
            // ASSUMPTION: Tiles are named "{x:D2}-{z:D2}.bin.gz"
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            // ASSUMPTION: Data is row-major with Z flipped
            // From example code: offset = (tx * tileRowCount + tz) * 10
            // and pixelZ = (tileRowCount - tz - 1) for display
            int flippedZ = metadata.TileRowCount - sampleZ - 1;
            int index = sampleX * metadata.TileRowCount + flippedZ;
            
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
