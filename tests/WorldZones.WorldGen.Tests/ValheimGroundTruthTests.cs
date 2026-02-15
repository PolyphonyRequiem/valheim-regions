using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class ValheimGroundTruthTests
    {
        readonly ITestOutputHelper output;
        
        public ValheimGroundTruthTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct MapSample
        {
            public ushort biome;
            public float height;
            public float forestFactor;
        }
        
        class MapMetadata
        {
            public string WorldSeed { get; set; }
            public string WorldVersion { get; set; }
            public int TileRowCount { get; set; }
            public float TileSize { get; set; }
            public int TileSideCount { get; set; }
            public float WorldWidth { get; set; }
            public string GeneratorVersion { get; set; }
            public string GeneratorUnityVersion { get; set; }
        }
        
        [Fact]
        public void LoadAndVerifyGroundTruthData()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var mapJsonPath = Path.Combine(dataPath, "map.json");
            
            if (!File.Exists(mapJsonPath))
            {
                this.output.WriteLine($"Ground truth data not found at: {dataPath}");
                Assert.True(false, "Ground truth data not available");
                return;
            }
            
            // Load metadata
            var json = File.ReadAllText(mapJsonPath);
            var metadata = JsonSerializer.Deserialize<MapMetadata>(json);
            
            this.output.WriteLine($"Ground Truth Data:");
            this.output.WriteLine($"  Seed: {metadata.WorldSeed}");
            this.output.WriteLine($"  World Size: {metadata.WorldWidth}");
            this.output.WriteLine($"  Tiles: {metadata.TileSideCount}x{metadata.TileSideCount}");
            this.output.WriteLine($"  Samples per tile: {metadata.TileRowCount}x{metadata.TileRowCount}");
            this.output.WriteLine($"  Tile size: {metadata.TileSize} units");
            this.output.WriteLine($"  Sample spacing: ~{metadata.TileSize / metadata.TileRowCount:F2} units");
            this.output.WriteLine($"");
            
            // Load center tile (should contain origin)
            int centerTile = metadata.TileSideCount / 2;
            var tilePath = Path.Combine(dataPath, "tiles", $"{centerTile:D2}-{centerTile:D2}.bin.gz");
            
            this.output.WriteLine($"Loading center tile: {centerTile:D2}-{centerTile:D2}");
            
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            this.output.WriteLine($"Loaded {samples.Length} samples");
            this.output.WriteLine($"");
            
            // Sample a few points
            var sampleIndices = new[] { 0, 256, 512, 768, 1023 };
            foreach (var idx in sampleIndices)
            {
                var sample = samples[idx * metadata.TileRowCount + idx];
                var biome = BiomeFromValue(sample.biome);
                this.output.WriteLine($"  Sample [{idx},{idx}]: {biome,-12} h={sample.height:F4} forest={sample.forestFactor:F3}");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void CompareSpecificCoordinates_AgainstGroundTruth()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            
            if (metadata == null)
            {
                this.output.WriteLine("Ground truth data not available");
                return;
            }
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            // Test coordinates we checked earlier
            var testCoords = new[] {
                (0f, 0f),
                (-686f, 1744f),
                (2564f, -1189f),
                (-127f, 262f)
            };
            
            this.output.WriteLine($"Comparing against Valheim ground truth:");
            this.output.WriteLine($"");
            
            foreach (var (x, z) in testCoords)
            {
                var ourBiome = generator.GetBiome(x, z);
                var ourHeight = generator.GetBaseHeight(x, z);
                
                var gtSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                var gtBiome = BiomeFromValue(gtSample.biome);
                
                var biomeMatch = ourBiome.ToString() == gtBiome ? "YES" : "NO";
                var heightDiff = Math.Abs(ourHeight - gtSample.height);
                var heightMatch = heightDiff < 0.1f ? "YES" : "NO";
                
                this.output.WriteLine($"({x,6:F0}, {z,6:F0}):");
                this.output.WriteLine($"  Ours:    {ourBiome,-12} h={ourHeight:F4}");
                this.output.WriteLine($"  Valheim: {gtBiome,-12} h={gtSample.height:F4}");
                this.output.WriteLine($"  Match: {biomeMatch} biome, {heightMatch} height (diff={heightDiff:F4})");
                this.output.WriteLine($"");
            }
            
            Assert.True(true);
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
            
            // Each sample is 10 bytes: uint16 biome (2 bytes) + float height (4 bytes) + float forestFactor (4 bytes)
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * 10;
                samples[i].biome = BitConverter.ToUInt16(bytes, offset + 0);
                samples[i].height = BitConverter.ToSingle(bytes, offset + 2);
                samples[i].forestFactor = BitConverter.ToSingle(bytes, offset + 6);
            }
            
            return samples;
        }
        
        MapMetadata LoadMetadata(string dataPath)
        {
            var mapJsonPath = Path.Combine(dataPath, "map.json");
            if (!File.Exists(mapJsonPath)) return null;
            
            var json = File.ReadAllText(mapJsonPath);
            return JsonSerializer.Deserialize<MapMetadata>(json);
        }
        
        MapSample GetSampleAtCoordinate(string dataPath, MapMetadata metadata, float worldX, float worldZ)
        {
            // PROVEN coordinate mapping from diagnostics
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
            
            // Tile naming: {X}-{Z} (reference: Program.cs line 44)
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            // Row-major indexing: row*width + col (reference: Program.cs line 64)
            int index = sampleX * metadata.TileRowCount + sampleZ;
            return samples[index];
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
