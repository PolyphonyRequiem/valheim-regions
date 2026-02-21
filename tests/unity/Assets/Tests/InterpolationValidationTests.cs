using NUnit.Framework;
using UnityEngine;
using WorldZones.WorldGen;
using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.WorldGen.Tests
{
    [TestFixture]
    public class InterpolationValidationTests
    {
        const string DataPath = "../../../data/seeds/HHcLC5acQt/data";
        const int TileRowCount = 1024;
        const int TileSize = 1500;
        const int TileSideCount = 16;
        const int WorldWidth = 24000;
        const int HeightmapChunkSize = 32; // meters

        struct MapSample
        {
            public ushort biome;
            public float height;
            public float forestFactor;
        }

        WorldGenerator CreateGenerator(string seed)
        {
            UnityEngine.Random.InitState(seed.GetHashCode());
            int offset0 = UnityEngine.Random.Range(-10000, 10000);
            int offset1 = UnityEngine.Random.Range(-10000, 10000);
            int offset2 = UnityEngine.Random.Range(-10000, 10000);
            int offset3 = UnityEngine.Random.Range(-10000, 10000);
            int offset4 = UnityEngine.Random.Range(-10000, 10000);
            
            return new WorldGenerator(seed, offset0, offset1, offset2, offset3, offset4);
        }

        // Heightmap interpolation matching Valheim's Heightmap.GetBiome() logic
        // chunkCornerBiomes should be the 4 corner biomes for the chunk this point is in
        BiomeType GetBiomeWithInterpolation(float worldX, float worldZ, BiomeType[] chunkCornerBiomes, float chunkWorldX, float chunkWorldZ)
        {
            // If all corners are the same, no interpolation needed
            if (chunkCornerBiomes[0] == chunkCornerBiomes[1] && 
                chunkCornerBiomes[0] == chunkCornerBiomes[2] && 
                chunkCornerBiomes[0] == chunkCornerBiomes[3])
            {
                return chunkCornerBiomes[0];
            }

            // Normalize position within chunk [0, 1]
            float localX = (worldX - chunkWorldX) / HeightmapChunkSize;
            float localZ = (worldZ - chunkWorldZ) / HeightmapChunkSize;

            // Distance-weighted voting (matching Heightmap.cs lines 460-463)
            var weights = new Dictionary<BiomeType, float>();
            
            float dist0 = Distance(localX, localZ, 0f, 0f);
            float dist1 = Distance(localX, localZ, 1f, 0f);
            float dist2 = Distance(localX, localZ, 0f, 1f);
            float dist3 = Distance(localX, localZ, 1f, 1f);

            AddWeight(weights, chunkCornerBiomes[0], dist0);
            AddWeight(weights, chunkCornerBiomes[1], dist1);
            AddWeight(weights, chunkCornerBiomes[2], dist2);
            AddWeight(weights, chunkCornerBiomes[3], dist3);

            // Return biome with highest weight
            return weights.OrderByDescending(kv => kv.Value).First().Key;
        }

        void AddWeight(Dictionary<BiomeType, float> weights, BiomeType biome, float weight)
        {
            if (!weights.ContainsKey(biome))
            {
                weights[biome] = 0f;
            }
            weights[biome] += weight;
        }

        // Distance calculation matching Heightmap.cs Distance method
        float Distance(float x, float y, float tx, float ty)
        {
            float dx = x - tx;
            float dy = y - ty;
            return Mathf.Max(0f, 1f - Mathf.Sqrt(dx * dx + dy * dy));
        }

        [Test]
        public void CompareDirectVsInterpolatedBiomes_Tile_8_8()
        {
            var dataPath = Path.Combine(Application.dataPath, DataPath);
            if (!Directory.Exists(dataPath))
            {
                Assert.Ignore("Ground truth data not found");
                return;
            }

            var generator = CreateGenerator("HHcLC5acQt");

            // Load tile 8,8
            var tilePath = Path.Combine(dataPath, "tiles", "08-08.bin.gz");
            var samples = LoadTile(tilePath);

            float tileWorldStartX = 8 * TileSize - (WorldWidth / 2f);
            float tileWorldStartZ = 8 * TileSize - (WorldWidth / 2f);

            int directMatches = 0;
            int interpolatedMatches = 0;
            int totalSamples = 0;

            // Build a cache of heightmap chunks and their corner biomes
            // Tile spans from tileWorldStart to tileWorldStart+1500
            // We need chunks covering this area plus edges
            int minChunkX = Mathf.FloorToInt(tileWorldStartX / HeightmapChunkSize) - 1;
            int maxChunkX = Mathf.FloorToInt((tileWorldStartX + TileSize) / HeightmapChunkSize) + 1;
            int minChunkZ = Mathf.FloorToInt(tileWorldStartZ / HeightmapChunkSize) - 1;
            int maxChunkZ = Mathf.FloorToInt((tileWorldStartZ + TileSize) / HeightmapChunkSize) + 1;

            var chunkCache = new Dictionary<(int, int), BiomeType[]>();

            Debug.Log($"=== Building Heightmap Chunk Cache ===");
            Debug.Log($"Tile covers chunks X=[{minChunkX}, {maxChunkX}] Z=[{minChunkZ}, {maxChunkZ}]");

            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                for (int cz = minChunkZ; cz <= maxChunkZ; cz++)
                {
                    float chunkWorldX = cx * HeightmapChunkSize;
                    float chunkWorldZ = cz * HeightmapChunkSize;

                    var corners = new BiomeType[4];
                    corners[0] = generator.GetBiome(chunkWorldX, chunkWorldZ);
                    corners[1] = generator.GetBiome(chunkWorldX + HeightmapChunkSize, chunkWorldZ);
                    corners[2] = generator.GetBiome(chunkWorldX, chunkWorldZ + HeightmapChunkSize);
                    corners[3] = generator.GetBiome(chunkWorldX + HeightmapChunkSize, chunkWorldZ + HeightmapChunkSize);

                    chunkCache[(cx, cz)] = corners;
                }
            }

            Debug.Log($"Built {chunkCache.Count} heightmap chunks");
            Debug.Log($"");
            Debug.Log($"=== Comparing Biome Detection Methods ===");

            // Sample every 10th point for speed
            int sampleStep = 10;

            for (int r = 0; r < TileRowCount; r += sampleStep)
            {
                for (int c = 0; c < TileRowCount; c += sampleStep)
                {
                    float sampleFracX = (float)r / TileRowCount;
                    float sampleFracZ = (float)c / TileRowCount;
                    float worldX = tileWorldStartX + sampleFracX * TileSize;
                    float worldZ = tileWorldStartZ + sampleFracZ * TileSize;

                    int index = r * TileRowCount + c;
                    var gtSample = samples[index];
                    var gtBiome = BiomeFromValue(gtSample.biome);

                    // Test direct method (our current implementation)
                    var directBiome = generator.GetBiome(worldX, worldZ);
                    if (directBiome == gtBiome)
                    {
                        directMatches++;
                    }

                    // Test interpolated method using proper heightmap chunks
                    int chunkX = Mathf.FloorToInt(worldX / HeightmapChunkSize);
                    int chunkZ = Mathf.FloorToInt(worldZ / HeightmapChunkSize);
                    float chunkWorldX = chunkX * HeightmapChunkSize;
                    float chunkWorldZ = chunkZ * HeightmapChunkSize;

                    var corners = chunkCache[(chunkX, chunkZ)];
                    var interpBiome = GetBiomeWithInterpolation(worldX, worldZ, corners, chunkWorldX, chunkWorldZ);
                    
                    if (interpBiome == gtBiome)
                    {
                        interpolatedMatches++;
                    }

                    totalSamples++;
                }
            }

            float directAccuracy = (directMatches / (float)totalSamples) * 100f;
            float interpAccuracy = (interpolatedMatches / (float)totalSamples) * 100f;

            Debug.Log($"Samples tested: {totalSamples:N0}");
            Debug.Log($"");
            Debug.Log($"Direct (WorldGenerator.GetBiome):       {directAccuracy:F2}% ({directMatches}/{totalSamples})");
            Debug.Log($"Interpolated (Heightmap.GetBiome):      {interpAccuracy:F2}% ({interpolatedMatches}/{totalSamples})");
            Debug.Log($"");
            Debug.Log($"Improvement: {(interpAccuracy - directAccuracy):+0.00;-0.00}%");

            if (interpAccuracy > directAccuracy + 1.0f)
            {
                Debug.Log($"");
                Debug.Log($"✓ HYPOTHESIS CONFIRMED: Ground truth uses Heightmap.GetBiome() interpolation!");
            }
            else if (directAccuracy > interpAccuracy + 1.0f)
            {
                Debug.Log($"");
                Debug.Log($"✗ HYPOTHESIS REJECTED: Ground truth uses direct WorldGenerator.GetBiome()");
            }
            else
            {
                Debug.Log($"");
                Debug.Log($"⚠ INCONCLUSIVE: Methods are too similar to determine");
            }
        }

        MapSample[] LoadTile(string tilePath)
        {
            using var fileStream = File.OpenRead(tilePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);

            var bytes = memoryStream.GetBuffer();
            int sampleCount = TileRowCount * TileRowCount;
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

        BiomeType BiomeFromValue(ushort value)
        {
            return value switch
            {
                0 => BiomeType.None,
                1 => BiomeType.Meadows,
                2 => BiomeType.Swamp,
                4 => BiomeType.Mountain,
                8 => BiomeType.BlackForest,
                16 => BiomeType.Plains,
                32 => BiomeType.AshLands,
                64 => BiomeType.DeepNorth,
                256 => BiomeType.Ocean,
                512 => BiomeType.Mistlands,
                _ => BiomeType.None
            };
        }
    }
}
