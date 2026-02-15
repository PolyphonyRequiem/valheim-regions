using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class RandomImplementationTests
    {
        readonly ITestOutputHelper output;
        
        public RandomImplementationTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CompareSystemRandomValues()
        {
            var seed = "HHcLC5acQt";
            var hashCode = seed.GetStableHashCode();
            
            this.output.WriteLine($"Seed: '{seed}'");
            this.output.WriteLine($"Hash: {hashCode}");
            this.output.WriteLine($"");
            
            // System.Random offsets
            var sysRandom = new Random(hashCode);
            this.output.WriteLine("System.Random offsets (what we're currently using):");
            for (int i = 0; i < 6; i++)
            {
                var value = sysRandom.NextDouble() * 100000.0;
                this.output.WriteLine($"  offset{i} = {value:F3}");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void SampleGroundTruthPatterns()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            if (metadata == null)
            {
                this.output.WriteLine("Ground truth not available");
                return;
            }
            
            // Sample a grid of points and see what Valheim generates
            this.output.WriteLine("Sampling Valheim ground truth at regular intervals:");
            this.output.WriteLine("");
            
            var samples = new[] {
                (0f, 0f),
                (1000f, 0f),
                (0f, 1000f),
                (1000f, 1000f),
                (-1000f, 0f),
                (0f, -1000f),
                (500f, 500f),
                (-500f, -500f)
            };
            
            foreach (var (x, z) in samples)
            {
                var gtSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                var biome = BiomeFromValue(gtSample.biome);
                
                // Normalize height to 0-1 range (rough guess - Valheim heights seem to be in -50 to +150 range)
                float normalizedHeight = (gtSample.height + 50f) / 200f;
                
                this.output.WriteLine($"({x,6:F0}, {z,6:F0}): {biome,-12} height={gtSample.height,6:F1} (normalized≈{normalizedHeight:F3})");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void TestDifferentOffsets_SeekBetterMatch()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            if (metadata == null)
            {
                this.output.WriteLine("Ground truth not available");
                return;
            }
            
            var seed = "HHcLC5acQt";
            var hashCode = seed.GetStableHashCode();
            
            // Test coordinates
            var testCoords = new[] {
                (0f, 0f),
                (-686f, 1744f),
                (2564f, -1189f),
                (-127f, 262f),
                (1000f, 0f),
                (0f, 1000f)
            };
            
            // Current System.Random offsets
            var sysRandom = new Random(hashCode);
            var currentOffsets = new double[6];
            for (int i = 0; i < 6; i++)
                currentOffsets[i] = sysRandom.NextDouble() * 100000.0;
            
            this.output.WriteLine("Testing with System.Random offsets:");
            var currentMatches = TestOffsets(currentOffsets, testCoords, dataPath, metadata);
            this.output.WriteLine($"  Biome matches: {currentMatches.biomeMatches}/{testCoords.Length}");
            this.output.WriteLine("");
            
            // Try some different offset patterns to see if ANY produce better results
            this.output.WriteLine("Testing alternative offset patterns:");
            
            // Pattern 1: Zero offsets
            var zeroOffsets = new double[6];
            this.output.WriteLine("  Zero offsets:");
            var zeroMatches = TestOffsets(zeroOffsets, testCoords, dataPath, metadata);
            this.output.WriteLine($"    Matches: {zeroMatches.biomeMatches}/{testCoords.Length}");
            
            // Pattern 2: Sequential offsets (0, 10000, 20000, etc)
            var seqOffsets = new double[6];
            for (int i = 0; i < 6; i++) seqOffsets[i] = i * 10000.0;
            this.output.WriteLine("  Sequential offsets (0, 10k, 20k...):");
            var seqMatches = TestOffsets(seqOffsets, testCoords, dataPath, metadata);
            this.output.WriteLine($"    Matches: {seqMatches.biomeMatches}/{testCoords.Length}");
            
            // Pattern 3: Hash-based offsets (what our PerlinNoise uses)
            var hashOffsets = new double[6];
            for (int i = 0; i < 6; i++) hashOffsets[i] = hashCode + i;
            this.output.WriteLine("  Hash-based offsets (hash+0, hash+1...):");
            var hashMatches = TestOffsets(hashOffsets, testCoords, dataPath, metadata);
            this.output.WriteLine($"    Matches: {hashMatches.biomeMatches}/{testCoords.Length}");
            
            this.output.WriteLine("");
            this.output.WriteLine("CONCLUSION:");
            if (currentMatches.biomeMatches == zeroMatches.biomeMatches && 
                currentMatches.biomeMatches == seqMatches.biomeMatches)
            {
                this.output.WriteLine("  Changing offsets doesn't improve results - the problem is likely NOT just Random implementation!");
                this.output.WriteLine("  This suggests the algorithm itself differs from Valheim's.");
            }
            else
            {
                this.output.WriteLine("  Different offsets produce different results!");
                this.output.WriteLine("  This suggests Random implementation could be the issue.");
            }
            
            Assert.True(true);
        }
        
        (int biomeMatches, int heightMatches) TestOffsets(double[] offsets, (float x, float z)[] coords, string dataPath, MapMetadata metadata)
        {
            var generator = new WorldGenerator("HHcLC5acQt", offsets[0], offsets[1], offsets[2], offsets[3], offsets[4], offsets[5]);
            
            int biomeMatches = 0;
            int heightMatches = 0;
            
            foreach (var (x, z) in coords)
            {
                var ourBiome = generator.GetBiome(x, z);
                var gtSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                var gtBiome = BiomeFromValue(gtSample.biome);
                
                if (ourBiome.ToString() == gtBiome)
                    biomeMatches++;
            }
            
            return (biomeMatches, heightMatches);
        }
        
        // Helper methods from ValheimGroundTruthTests
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
            float halfWorld = metadata.WorldWidth / 2f;
            float tileWorldX = worldX + halfWorld;
            float tileWorldZ = worldZ + halfWorld;
            
            int tileX = (int)(tileWorldX / metadata.TileSize);
            int tileZ = (int)(tileWorldZ / metadata.TileSize);
            
            float localX = tileWorldX - (tileX * metadata.TileSize);
            float localZ = tileWorldZ - (tileZ * metadata.TileSize);
            
            int sampleX = (int)((localX / metadata.TileSize) * metadata.TileRowCount);
            int sampleZ = (int)((localZ / metadata.TileSize) * metadata.TileRowCount);
            
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileZ:D2}-{tileX:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            int index = sampleZ * metadata.TileRowCount + sampleX;
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
