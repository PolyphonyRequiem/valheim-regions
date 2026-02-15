using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class LensingAnalysis
    {
        readonly ITestOutputHelper output;
        
        public LensingAnalysis(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckForCircularProjection()
        {
            var dataPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data";
            var metadata = LoadMetadata(dataPath);
            if (metadata == null) return;
            
            this.output.WriteLine("CHECKING VOID BOUNDARY TRANSITION");
            this.output.WriteLine("==================================");
            this.output.WriteLine("");
            
            // Sample densely around 10,500m to see transition
            this.output.WriteLine("Sampling along +X axis around 10,500m boundary:");
            this.output.WriteLine("");
            
            for (float dist = 9500; dist <= 11500; dist += 100)
            {
                var sample = GetSampleAtCoordinate(dataPath, metadata, dist, 0);
                var biome = BiomeFromValue(sample.biome);
                var isVoid = sample.height <= -390; // Close to -400
                var marker = isVoid ? " <- VOID" : "";
                
                this.output.WriteLine($"{dist,6:F0}m: {biome,-12} h={sample.height,7:F1}m{marker}");
            }
            
            this.output.WriteLine("");
            this.output.WriteLine("QUESTION: Is transition gradual or sharp?");
            this.output.WriteLine("  Gradual: Heights decrease smoothly toward -400");
            this.output.WriteLine("  Sharp: Sudden jump from normal heights to -400");
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
