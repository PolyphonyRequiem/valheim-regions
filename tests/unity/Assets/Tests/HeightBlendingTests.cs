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
    public class HeightBlendingTests
    {
        const string DataPath = "../../../data/seeds/HHcLC5acQt/data";
        const int TileRowCount = 1024;
        const int TileSize = 1500;
        const int WorldWidth = 24000;
        const int HeightmapChunkSize = 32;
        const float HeightmapScale = 1f;

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

        [Test]
        public void CompareDirectVsBlendedHeights_Tile_8_8()
        {
            var dataPath = Path.Combine(Application.dataPath, DataPath);
            if (!Directory.Exists(dataPath))
            {
                Assert.Ignore("Ground truth data not found");
                return;
            }

            var generator = CreateGenerator("HHcLC5acQt");
            var tilePath = Path.Combine(dataPath, "tiles", "08-08.bin.gz");
            var samples = LoadTile(tilePath);

            float tileWorldStartX = 8 * TileSize - (WorldWidth / 2f);
            float tileWorldStartZ = 8 * TileSize - (WorldWidth / 2f);

            Debug.Log($"=== Testing Height Blending Hypothesis ===");
            Debug.Log($"Comparing direct GetBiomeHeight() vs HeightmapBuilder blending");

            // Sample every 50th point for speed
            int sampleStep = 50;
            var directDiffs = new List<float>();
            var blendedDiffs = new List<float>();

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
                    float gtHeight = gtSample.height;

                    // Method 1: Direct GetBiomeHeight (what we've been using)
                    float directHeight = generator.GetBiomeHeight(gtBiome, worldX, worldZ);
                    directDiffs.Add(directHeight - gtHeight);

                    // Method 2: HeightmapBuilder blending
                    float blendedHeight = GetBlendedHeight(generator, new Vector2(worldX, worldZ));
                    blendedDiffs.Add(blendedHeight - gtHeight);
                }
            }

            float directMean = directDiffs.Average();
            float directStdDev = (float)Math.Sqrt(directDiffs.Select(d => Math.Pow(d - directMean, 2)).Average());
            float directMeanAbs = directDiffs.Select(d => Math.Abs(d)).Average();

            float blendedMean = blendedDiffs.Average();
            float blendedStdDev = (float)Math.Sqrt(blendedDiffs.Select(d => Math.Pow(d - blendedMean, 2)).Average());
            float blendedMeanAbs = blendedDiffs.Select(d => Math.Abs(d)).Average();

            Debug.Log($"");
            Debug.Log($"Samples tested: {directDiffs.Count:N0}");
            Debug.Log($"");
            Debug.Log($"Direct GetBiomeHeight():");
            Debug.Log($"  Mean error: {directMean:F6} meters");
            Debug.Log($"  StdDev: {directStdDev:F6}");
            Debug.Log($"  Mean absolute error: {directMeanAbs:F6}");
            Debug.Log($"");
            Debug.Log($"HeightmapBuilder Blending:");
            Debug.Log($"  Mean error: {blendedMean:F6} meters");
            Debug.Log($"  StdDev: {blendedStdDev:F6}");
            Debug.Log($"  Mean absolute error: {blendedMeanAbs:F6}");
            Debug.Log($"");

            if (blendedMeanAbs < directMeanAbs * 0.9f)
            {
                Debug.Log($"✓ BLENDING IS BETTER! Ground truth likely uses HeightmapBuilder");
            }
            else if (directMeanAbs < blendedMeanAbs * 0.9f)
            {
                Debug.Log($"✓ DIRECT IS BETTER! Ground truth likely uses direct GetBiomeHeight()");
            }
            else
            {
                Debug.Log($"⚠ INCONCLUSIVE: Methods produce similar accuracy");
            }
        }

        float GetBlendedHeight(WorldGenerator worldGen, Vector2 worldPos)
        {
            // Find the heightmap chunk this position belongs to
            float chunkWorldX = Mathf.Floor(worldPos.x / (HeightmapChunkSize * HeightmapScale)) * (HeightmapChunkSize * HeightmapScale);
            float chunkWorldY = Mathf.Floor(worldPos.y / (HeightmapChunkSize * HeightmapScale)) * (HeightmapChunkSize * HeightmapScale);
            var chunkCenter = new Vector2(chunkWorldX + (HeightmapChunkSize * HeightmapScale) * 0.5f, chunkWorldY + (HeightmapChunkSize * HeightmapScale) * 0.5f);
            
            // Get corner biomes
            Vector2 bottomLeft = chunkCenter + new Vector2(HeightmapChunkSize * HeightmapScale * -0.5f, HeightmapChunkSize * HeightmapScale * -0.5f);
            var corner0 = worldGen.GetBiome(bottomLeft.x, bottomLeft.y);
            var corner1 = worldGen.GetBiome(bottomLeft.x + HeightmapChunkSize * HeightmapScale, bottomLeft.y);
            var corner2 = worldGen.GetBiome(bottomLeft.x, bottomLeft.y + HeightmapChunkSize * HeightmapScale);
            var corner3 = worldGen.GetBiome(bottomLeft.x + HeightmapChunkSize * HeightmapScale, bottomLeft.y + HeightmapChunkSize * HeightmapScale);
            
            float wx = worldPos.x;
            float wy = worldPos.y;
            
            // If all corners are the same biome, no blending needed
            if (corner0 == corner1 && corner0 == corner2 && corner0 == corner3)
            {
                return worldGen.GetBiomeHeight(corner0, wx, wy);
            }
            
            // Blend heights from all 4 corner biomes
            float localX = (worldPos.x - bottomLeft.x) / (HeightmapChunkSize * HeightmapScale);
            float localY = (worldPos.y - bottomLeft.y) / (HeightmapChunkSize * HeightmapScale);
            
            float t2 = SmoothStep(0f, 1f, localX);
            float t = SmoothStep(0f, 1f, localY);
            
            float biomeHeight = worldGen.GetBiomeHeight(corner0, wx, wy);
            float biomeHeight2 = worldGen.GetBiomeHeight(corner1, wx, wy);
            float biomeHeight3 = worldGen.GetBiomeHeight(corner2, wx, wy);
            float biomeHeight4 = worldGen.GetBiomeHeight(corner3, wx, wy);
            
            float a = Lerp(biomeHeight, biomeHeight2, t2);
            float b = Lerp(biomeHeight3, biomeHeight4, t2);
            return Lerp(a, b, t);
        }

        float SmoothStep(float min, float max, float value)
        {
            float t = Mathf.Clamp01((value - min) / (max - min));
            return t * t * (3f - 2f * t);
        }

        float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
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
