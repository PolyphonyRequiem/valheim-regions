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
    public class HeightmapBuilderValidationTests
    {
        const string DataPath = "../../../data/seeds/HHcLC5acQt/data";
        const int TileRowCount = 1024;
        const int TileSize = 1500;
        const int TileSideCount = 16;
        const int WorldWidth = 24000;
        const int HeightmapChunkSize = 32; // meters
        const float HeightmapScale = 1f; // 1 meter per vertex

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
        public void ValidateTile_8_8_WithHeightmapBuilder()
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

            Debug.Log($"=== Validating Tile 8,8 with HeightmapBuilder ===");
            Debug.Log($"Tile spans world coordinates ({tileWorldStartX:F2}, {tileWorldStartZ:F2}) to ({tileWorldStartX + TileSize:F2}, {tileWorldStartZ + TileSize:F2})");

            int biomeMatches = 0;
            int totalSamples = 0;
            var biomeStats = new Dictionary<string, (int correct, int total)>();

            // Sample every point (or every Nth point for speed)
            int sampleStep = 1; // Test all points
            
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

                    // Use heightmap-based biome detection
                    var ourBiome = HeightmapBuilder.GetHeightmapBiome(generator, new Vector2(worldX, worldZ), HeightmapChunkSize, HeightmapScale);

                    if (ourBiome == gtBiome)
                    {
                        biomeMatches++;
                    }

                    // Track per-biome stats
                    string biomeName = gtBiome.ToString();
                    if (!biomeStats.ContainsKey(biomeName))
                    {
                        biomeStats[biomeName] = (0, 0);
                    }
                    var stats = biomeStats[biomeName];
                    biomeStats[biomeName] = (stats.correct + (ourBiome == gtBiome ? 1 : 0), stats.total + 1);

                    totalSamples++;
                }
            }

            float biomeAccuracy = (biomeMatches / (float)totalSamples) * 100f;

            Debug.Log($"");
            Debug.Log($"Total samples: {totalSamples:N0}");
            Debug.Log($"OVERALL Biome Accuracy: {biomeAccuracy:F4}% ({biomeMatches}/{totalSamples})");
            Debug.Log($"");
            Debug.Log($"PER-BIOME Accuracy:");

            foreach (var kv in biomeStats.OrderBy(x => x.Key))
            {
                float accuracy = (kv.Value.correct / (float)kv.Value.total) * 100f;
                float pct = (kv.Value.total / (float)totalSamples) * 100f;
                Debug.Log($"  {kv.Key,-15}: {accuracy:F2}% ({kv.Value.correct:N0}/{kv.Value.total:N0}) - {pct:F1}% of tile");
            }

            if (biomeAccuracy >= 99.9f)
            {
                Debug.Log($"");
                Debug.Log($"✓ EXCELLENT! HeightmapBuilder achieves >99.9% accuracy!");
            }
            else if (biomeAccuracy > 96.9f)
            {
                Debug.Log($"");
                Debug.Log($"✓ IMPROVED! HeightmapBuilder is more accurate than direct WorldGenerator");
            }
            else
            {
                Debug.Log($"");
                Debug.Log($"⚠ Same accuracy as direct WorldGenerator - no improvement");
            }

            Assert.That(biomeAccuracy, Is.GreaterThan(96.9f), "HeightmapBuilder should improve accuracy");
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
