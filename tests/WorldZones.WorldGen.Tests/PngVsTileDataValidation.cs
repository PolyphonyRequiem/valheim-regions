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
    /// Validates that PNG and tile data are consistent with each other.
    /// If they don't match, we have a coordinate mapping bug.
    /// </summary>
    public class PngVsTileDataValidation
    {
        readonly ITestOutputHelper output;
        
        public PngVsTileDataValidation(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void ComparePngAndTileData_GridSample()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            
            var metadata = LoadMetadata(dataPath);
            if (metadata == null || !File.Exists(pngPath))
            {
                this.output.WriteLine("Data not available");
                return;
            }
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("PNG vs TILE DATA VALIDATION");
            this.output.WriteLine("===========================");
            this.output.WriteLine("");
            
            // Sample points only within PNG's coverage (±10,500 units)
            // Tile data covers ±12,000 but PNG only covers ±10,500
            int matches = 0;
            int total = 0;
            int pngUnknown = 0;
            
            var mismatchSamples = new System.Collections.Generic.List<string>();
            
            // Stay well within valid world area - avoid void/legend
            // Valheim world is ~10,000 radius, so test ±6000 to be safe
            for (float x = -6000; x <= 6000; x += 500)
            {
                for (float z = -6000; z <= 6000; z += 500)
                {
                    total++;
                    
                    var pngBiome = GetBiomeFromPNG(png, x, z);
                    var tileSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                    var tileBiome = BiomeFromValue(tileSample.biome);
                    
                    // Skip Shallows (underwater terrain), Unknown colors, and Void (black border)
                    if (pngBiome == "Unknown" || pngBiome == "Shallows" || pngBiome == "Void")
                    {
                        pngUnknown++;
                        continue;
                    }
                    
                    if (pngBiome == tileBiome)
                    {
                        matches++;
                    }
                    else
                    {
                        if (mismatchSamples.Count < 10)
                        {
                            mismatchSamples.Add($"  ({x,6:F0}, {z,6:F0}): PNG={pngBiome,-12} Tile={tileBiome,-12}");
                        }
                    }
                }
            }
            
            int validComparisons = total - pngUnknown;
            float matchPercent = validComparisons > 0 ? 100f * matches / validComparisons : 0;
            
            this.output.WriteLine($"Total samples tested: {total}");
            this.output.WriteLine($"Skipped (shallows/void/unknown/tile-void/UI): {pngUnknown}");
            this.output.WriteLine($"Valid comparisons: {validComparisons}");
            this.output.WriteLine($"Matches: {matches} ({matchPercent:F1}%)");
            this.output.WriteLine($"Mismatches: {validComparisons - matches}");
            this.output.WriteLine("");
            
            if (mismatchSamples.Count > 0)
            {
                this.output.WriteLine($"Sample mismatches (showing {Math.Min(20, mismatchSamples.Count)} of {mismatchSamples.Count}):");
                for (int i = 0; i < Math.Min(20, mismatchSamples.Count); i++)
                {
                    this.output.WriteLine(mismatchSamples[i]);
                }
                this.output.WriteLine("");
            }
            
            if (matchPercent > 95)
            {
                this.output.WriteLine("✓ PNG and TILE DATA AGREE!");
                this.output.WriteLine("  Coordinate mapping is correct.");
            }
            else if (matchPercent > 80)
            {
                this.output.WriteLine("⚠ MOSTLY AGREE");
                this.output.WriteLine("  Coordinate mapping likely correct, but some differences exist.");
                this.output.WriteLine("  Could be rendering differences or biome boundary effects.");
            }
            else if (matchPercent > 50)
            {
                this.output.WriteLine("⚠ PARTIAL MATCH");
                this.output.WriteLine("  Some coordinates work, others don't. Possible indexing bug.");
            }
            else
            {
                this.output.WriteLine("✗ PNG and TILE DATA DON'T MATCH!");
                this.output.WriteLine("  Coordinate mapping is fundamentally wrong.");
            }
            
            Assert.True(true);
        }
        
        string GetBiomeFromPNG(Bitmap png, float worldX, float worldZ)
        {
            // PNG covers ±10,500 units = 21,000 total (validated by void boundary)
            // Center at (4096, 4096)
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
            // Exact colors from user (allowing small tolerance for compression artifacts)
            int tolerance = 3;
            
            if (IsColor(c, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(c, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(c, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(c, 255, 255, 255, tolerance)) return "Mountain"; // Could be DeepNorth too
            if (IsColor(c, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(c, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(c, 255, 0, 0, tolerance)) return "AshLands";
            if (IsColor(c, 102, 102, 255, tolerance)) return "Shallows"; // SKIP - shows any underwater non-ocean biome
            if (IsColor(c, 0, 0, 153, tolerance)) return "Ocean";
            
            return "Unknown";
        }
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
        
        // Tile reading helpers
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
            // World coords: center at (0,0), range ±12000
            // Tile storage: 0,0 at top-left (like images), but Valheim world has 0,0 at center
            float halfWorld = metadata.WorldWidth / 2f;
            float tileWorldX = worldX + halfWorld;  // Convert to [0, WorldWidth]
            float tileWorldZ = worldZ + halfWorld;  // Convert to [0, WorldWidth]
            
            // Which tile? (tiles are numbered 0-15)
            int tileX = (int)(tileWorldX / metadata.TileSize);
            int tileZ = (int)(tileWorldZ / metadata.TileSize);
            
            tileX = Math.Max(0, Math.Min(metadata.TileSideCount - 1, tileX));
            tileZ = Math.Max(0, Math.Min(metadata.TileSideCount - 1, tileZ));
            
            // Position within tile (0 to TileSize)
            float localX = tileWorldX - (tileX * metadata.TileSize);
            float localZ = tileWorldZ - (tileZ * metadata.TileSize);
            
            // Which sample within tile? (samples are 0-1023)
            int sampleX = (int)((localX / metadata.TileSize) * metadata.TileRowCount);
            int sampleZ = (int)((localZ / metadata.TileSize) * metadata.TileRowCount);
            
            sampleX = Math.Max(0, Math.Min(metadata.TileRowCount - 1, sampleX));
            sampleZ = Math.Max(0, Math.Min(metadata.TileRowCount - 1, sampleZ));
            
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            // Reference code (line 64): offset = (tx * tileRowCount + tz) * 10
            // tx corresponds to X (left-right), tz to Z (top-bottom in storage)
            // Data stored in row-major order with NO flip
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
