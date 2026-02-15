using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class BiomePatternAnalysisTests
    {
        readonly ITestOutputHelper output;
        
        public BiomePatternAnalysisTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void AnalyzeBiomePatternsByDistance()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            if (metadata == null)
            {
                this.output.WriteLine("Ground truth not available");
                return;
            }
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            // Sample points at different distances
            var distanceRings = new[] { 0f, 500f, 1000f, 2000f, 3000f, 4000f, 5000f, 6000f, 8000f, 10000f };
            
            this.output.WriteLine("BIOME DISTRIBUTION BY DISTANCE");
            this.output.WriteLine("==============================");
            this.output.WriteLine("");
            
            foreach (var distance in distanceRings)
            {
                var valheimCounts = new Dictionary<string, int>();
                var ourCounts = new Dictionary<string, int>();
                
                // Sample 36 points around the ring (every 10 degrees)
                int matches = 0;
                for (int angle = 0; angle < 360; angle += 10)
                {
                    float rad = angle * (float)Math.PI / 180f;
                    float x = distance * (float)Math.Cos(rad);
                    float z = distance * (float)Math.Sin(rad);
                    
                    var gtSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                    var valheimBiome = BiomeFromValue(gtSample.biome);
                    
                    var ourBiome = generator.GetBiome(x, z).ToString();
                    
                    if (!valheimCounts.ContainsKey(valheimBiome))
                        valheimCounts[valheimBiome] = 0;
                    valheimCounts[valheimBiome]++;
                    
                    if (!ourCounts.ContainsKey(ourBiome))
                        ourCounts[ourBiome] = 0;
                    ourCounts[ourBiome]++;
                    
                    if (valheimBiome == ourBiome)
                        matches++;
                }
                
                this.output.WriteLine($"Distance: {distance:F0}m ({matches}/36 matches = {100.0 * matches / 36:F1}%)");
                this.output.WriteLine($"  Valheim: {string.Join(", ", valheimCounts.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
                this.output.WriteLine($"  Ours:    {string.Join(", ", ourCounts.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key}:{kvp.Value}"))}");
                this.output.WriteLine("");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void FindAnyMatchingPoints_GridSample()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            if (metadata == null)
            {
                this.output.WriteLine("Ground truth not available");
                return;
            }
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            // Sample a 50x50 grid across the map
            int totalSamples = 0;
            int matches = 0;
            var matchedPoints = new List<(float x, float z, string biome)>();
            
            for (float x = -8000; x <= 8000; x += 320)
            {
                for (float z = -8000; z <= 8000; z += 320)
                {
                    totalSamples++;
                    
                    var gtSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                    var valheimBiome = BiomeFromValue(gtSample.biome);
                    var ourBiome = generator.GetBiome(x, z).ToString();
                    
                    if (valheimBiome == ourBiome)
                    {
                        matches++;
                        if (matchedPoints.Count < 20)
                            matchedPoints.Add((x, z, valheimBiome));
                    }
                }
            }
            
            this.output.WriteLine($"GRID SAMPLE ANALYSIS");
            this.output.WriteLine($"===================");
            this.output.WriteLine($"Total samples: {totalSamples}");
            this.output.WriteLine($"Matches: {matches} ({100.0 * matches / totalSamples:F1}%)");
            this.output.WriteLine($"");
            
            if (matchedPoints.Count > 0)
            {
                this.output.WriteLine($"Sample of matching points:");
                foreach (var (x, z, biome) in matchedPoints)
                {
                    this.output.WriteLine($"  ({x,6:F0}, {z,6:F0}): {biome}");
                }
            }
            else
            {
                this.output.WriteLine("NO MATCHES FOUND - Our algorithm is fundamentally different!");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void CompareOurHeightsWithGroundTruth()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            if (metadata == null)
            {
                this.output.WriteLine("Ground truth not available");
                return;
            }
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            // Sample points and compare what our "baseHeight" correlates to
            var samples = new List<(float x, float z, float ourHeight, float valheimHeight, string valheimBiome)>();
            
            for (float x = -4000; x <= 4000; x += 800)
            {
                for (float z = -4000; z <= 4000; z += 800)
                {
                    var gtSample = GetSampleAtCoordinate(dataPath, metadata, x, z);
                    var ourHeight = generator.GetBaseHeight(x, z);
                    var valheimBiome = BiomeFromValue(gtSample.biome);
                    
                    samples.Add((x, z, ourHeight, gtSample.height, valheimBiome));
                }
            }
            
            this.output.WriteLine("HEIGHT CORRELATION ANALYSIS");
            this.output.WriteLine("===========================");
            this.output.WriteLine("");
            
            // Group by Valheim biome and see what our heights look like
            var byBiome = samples.GroupBy(s => s.valheimBiome);
            
            foreach (var group in byBiome.OrderBy(g => g.Key))
            {
                var ourHeights = group.Select(s => s.ourHeight).ToArray();
                var min = ourHeights.Min();
                var max = ourHeights.Max();
                var avg = ourHeights.Average();
                
                this.output.WriteLine($"{group.Key,-12} (n={group.Count(),3}): our height range [{min:F3}, {max:F3}] avg={avg:F3}");
            }
            
            this.output.WriteLine("");
            this.output.WriteLine("Do our height ranges correspond to Valheim biomes?");
            this.output.WriteLine("If not, our height calculation is wrong.");
            
            Assert.True(true);
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
            
            // FIX 1: Tiles are named X-Z not Z-X!
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath, metadata.TileRowCount);
            
            // FIX 2: Data is stored row-major (X * rowCount + Z), and Z needs flipping for Valheim coordinates
            // The storage has 0,0 at top-left, but Valheim has 0,0 at bottom-left
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
