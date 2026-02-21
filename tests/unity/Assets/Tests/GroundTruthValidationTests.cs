using NUnit.Framework;
using UnityEngine;
using WorldZones.WorldGen;
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Validates WorldGenerator against ground truth tile data from Valheim.
    /// Tile metadata for seed HHcLC5acQt: 16x16 tiles, 1024x1024 samples per tile, 1500 units per tile
    /// </summary>
    [TestFixture]
    public class GroundTruthValidationTests
    {
        // Hardcoded tile metadata for HHcLC5acQt dataset
        const int TileRowCount = 1024;
        const float TileSize = 1500f;
        const int TileSideCount = 16;
        const float WorldWidth = 24000f;
        const string DataPath = "../../../data/seeds/HHcLC5acQt/data";
        
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct MapSample
        {
            public ushort biome;
            public float height;
            public float forestFactor;
        }
        
        WorldGenerator CreateGenerator(string seed)
        {
            int seedHash = string.IsNullOrEmpty(seed) ? 0 : seed.GetStableHashCode();
            
            UnityEngine.Random.State savedState = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seedHash);
            int offset0 = UnityEngine.Random.Range(-10000, 10000);
            int offset1 = UnityEngine.Random.Range(-10000, 10000);
            int offset2 = UnityEngine.Random.Range(-10000, 10000);
            int offset3 = UnityEngine.Random.Range(-10000, 10000);
            int offset4 = UnityEngine.Random.Range(-10000, 10000);
            UnityEngine.Random.state = savedState;
            
            return new WorldGenerator(seed, offset0, offset1, offset2, offset3, offset4);
        }
        
        [Test]
        public void ValidateOriginCoordinate()
        {
            var dataPath = Path.Combine(Application.dataPath, DataPath);
            if (!Directory.Exists(dataPath))
            {
                Assert.Ignore("Ground truth data not found");
                return;
            }
            
            var generator = CreateGenerator("HHcLC5acQt");
            var ourBiome = generator.GetBiome(0f, 0f);
            var ourHeight = generator.GetBaseHeight(0f, 0f);
            
            var gtSample = GetSampleAtCoordinate(dataPath, 0f, 0f);
            var gtBiome = BiomeFromValue(gtSample.biome);
            
            Debug.Log($"Origin (0,0): Ours={ourBiome} h={ourHeight:F4}, Valheim={gtBiome} h={gtSample.height:F4}");
            
            Assert.That(ourBiome.ToString(), Is.EqualTo(gtBiome));
        }
        
        [Test]
        public void AnalyzeHeightBias_Tile_8_8()
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
            
            Debug.Log($"=== Height Bias Analysis (Tile 8,8) ===");
            
            // Sample every 50th point to get good coverage without taking forever
            int sampleStep = 50;
            var heightDiffs = new List<float>();
            var mountainHeights = new List<(float ours, float gt)>();
            
            for (int row = 0; row < TileRowCount; row += sampleStep)
            {
                for (int col = 0; col < TileRowCount; col += sampleStep)
                {
                    float sampleFracX = (float)row / TileRowCount;
                    float sampleFracZ = (float)col / TileRowCount;
                    float worldX = tileWorldStartX + sampleFracX * TileSize;
                    float worldZ = tileWorldStartZ + sampleFracZ * TileSize;
                    
                    int index = row * TileRowCount + col;
                    var gtSample = samples[index];
                    var gtBiome = BiomeFromValue(gtSample.biome);
                    
                    var ourHeight = generator.GetBaseHeight(worldX, worldZ);
                    
                    // GT height is in meters (baseHeight * 200), convert back to normalized
                    float gtNormalizedHeight = gtSample.height / 200f;
                    
                    float diff = ourHeight - gtNormalizedHeight;
                    heightDiffs.Add(diff);
                    
                    // Track Mountain boundary samples specifically
                    if (gtBiome == "Mountain" && gtNormalizedHeight >= 0.35f && gtNormalizedHeight <= 0.45f)
                    {
                        mountainHeights.Add((ourHeight, gtNormalizedHeight));
                    }
                }
            }
            
            // Calculate statistics
            float meanDiff = heightDiffs.Average();
            float stdDev = (float)Math.Sqrt(heightDiffs.Select(d => Math.Pow(d - meanDiff, 2)).Average());
            float minDiff = heightDiffs.Min();
            float maxDiff = heightDiffs.Max();
            
            Debug.Log($"");
            Debug.Log($"Samples analyzed: {heightDiffs.Count:N0}");
            Debug.Log($"Height difference (ours - GT normalized):");
            Debug.Log($"  Mean: {meanDiff:F6}");
            Debug.Log($"  StdDev: {stdDev:F6}");
            Debug.Log($"  Min: {minDiff:F6}");
            Debug.Log($"  Max: {maxDiff:F6}");
            
            // Analyze bias by height range
            var heightRanges = new[] {
                (min: -1f, max: 0.02f, name: "Ocean (<0.02)"),
                (min: 0.02f, max: 0.1f, name: "Low (0.02-0.1)"),
                (min: 0.1f, max: 0.3f, name: "Medium (0.1-0.3)"),
                (min: 0.3f, max: 0.4f, name: "High (0.3-0.4)"),
                (min: 0.4f, max: 1f, name: "Mountain (>0.4)")
            };
            
            Debug.Log($"");
            Debug.Log($"Bias by GT height range:");
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
                    float gtNorm = gtSample.height / 200f;
                    BiomeType gtBiome = BiomeFromBiomeValue(gtSample.biome);
                    var ourHeight = generator.GetBiomeHeight(gtBiome, worldX, worldZ) / 200f;
                    
                    foreach (var range in heightRanges)
                    {
                        if (gtNorm >= range.min && gtNorm < range.max)
                        {
                            // Store for range analysis
                            break;
                        }
                    }
                }
            }
            
            // Now calculate per-range stats
            var rangeStats = new List<(string name, float meanDiff, int count)>();
            foreach (var range in heightRanges)
            {
                var rangeDiffs = new List<float>();
                
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
                        float gtNorm = gtSample.height / 200f;
                        BiomeType gtBiome = BiomeFromBiomeValue(gtSample.biome);
                        
                        if (gtNorm >= range.min && gtNorm < range.max)
                        {
                            var ourHeight = generator.GetBiomeHeight(gtBiome, worldX, worldZ) / 200f;
                            rangeDiffs.Add(ourHeight - gtNorm);
                        }
                    }
                }
                
                if (rangeDiffs.Any())
                {
                    float rangeMean = rangeDiffs.Average();
                    rangeStats.Add((range.name, rangeMean, rangeDiffs.Count));
                    Debug.Log($"  {range.name,-20}: mean={rangeMean:F6} (n={rangeDiffs.Count})");
                }
            }
            
            if (mountainHeights.Any())
            {
                Debug.Log($"");
                Debug.Log($"Mountain boundary samples (0.35-0.45 range): {mountainHeights.Count}");
                float mountainMeanDiff = mountainHeights.Select(h => h.ours - h.gt).Average();
                Debug.Log($"  Mean difference: {mountainMeanDiff:F6}");
                
                Debug.Log($"  First 10 Mountain boundary samples:");
                foreach (var (ours, gt) in mountainHeights.Take(10))
                {
                    Debug.Log($"    Ours: {ours:F6}, GT: {gt:F6}, Diff: {(ours - gt):F6}");
                }
            }
            
            // Check if there's a consistent bias
            if (Math.Abs(meanDiff) > 0.001f)
            {
                Debug.Log($"");
                Debug.Log($"⚠ SYSTEMATIC BIAS DETECTED: {meanDiff:F6}");
                Debug.Log($"  This could explain Mountain misclassification at 0.4 threshold");
            }
            else
            {
                Debug.Log($"");
                Debug.Log($"✓ No significant systematic bias");
            }
        }
        
        [Test]
        public void ValidateRandomSample_1000Points()
        {
            var dataPath = Path.Combine(Application.dataPath, DataPath);
            if (!Directory.Exists(dataPath))
            {
                Assert.Ignore("Ground truth data not found");
                return;
            }
            
            var generator = CreateGenerator("HHcLC5acQt");
            var random = new System.Random(42);
            int sampleCount = 1000;
            int matches = 0;
            var confusionMatrix = new Dictionary<string, int>();
            
            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (float)(random.NextDouble() * 2 * Math.PI);
                float radius = (float)(random.NextDouble() * 10000);
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                
                var ourBiome = generator.GetBiome(x, z);
                var gtSample = GetSampleAtCoordinate(dataPath, x, z);
                var gtBiome = BiomeFromValue(gtSample.biome);
                
                if (ourBiome.ToString() == gtBiome)
                {
                    matches++;
                }
                else
                {
                    var key = $"{gtBiome}→{ourBiome}";
                    confusionMatrix[key] = confusionMatrix.GetValueOrDefault(key, 0) + 1;
                }
            }
            
            float accuracy = (float)matches / sampleCount * 100f;
            Debug.Log($"=== Validation (1000 samples) ===");
            Debug.Log($"Accuracy: {accuracy:F2}% ({matches}/{sampleCount})");
            
            if (confusionMatrix.Any())
            {
                Debug.Log($"Mismatches:");
                foreach (var kvp in confusionMatrix.OrderByDescending(x => x.Value))
                {
                    Debug.Log($"  {kvp.Key}: {kvp.Value}");
                }
            }
            
            Assert.That(accuracy, Is.GreaterThanOrEqualTo(95f), $"Expected >=95%, got {accuracy:F2}%");
        }
        
        [Test]
        public void ValidateSingleTile_8_8_Full()
        {
            var dataPath = Path.Combine(Application.dataPath, DataPath);
            if (!Directory.Exists(dataPath))
            {
                Assert.Ignore("Ground truth data not found");
                return;
            }
            
            var generator = CreateGenerator("HHcLC5acQt");
            
            // Load tile 8,8 (near center)
            var tilePath = Path.Combine(dataPath, "tiles", "08-08.bin.gz");
            var samples = LoadTile(tilePath);
            
            // Calculate tile world bounds
            float tileWorldStartX = 8 * TileSize - (WorldWidth / 2f);
            float tileWorldStartZ = 8 * TileSize - (WorldWidth / 2f);
            
            Debug.Log($"=== Validating FULL Tile 8,8 (ALL {TileRowCount * TileRowCount:N0} samples) ===");
            Debug.Log($"Tile bounds: X=[{tileWorldStartX:F1}, {tileWorldStartX + TileSize:F1}], Z=[{tileWorldStartZ:F1}, {tileWorldStartZ + TileSize:F1}]");
            
            int biomeMatches = 0;
            int totalSamples = TileRowCount * TileRowCount;
            var biomeConfusion = new Dictionary<string, int>();
            var perBiomeStats = new Dictionary<string, (int correct, int total)>();
            var firstBiomeMismatch = (x: 0f, z: 0f, ours: "", gt: "", height: 0f, gtHeight: 0f, found: false);
            var nonMountainErrors = new List<(float x, float z, int row, int col, string ours, string gt)>();
            
            for (int row = 0; row < TileRowCount; row++)
            {
                for (int col = 0; col < TileRowCount; col++)
                {
                    // Convert tile sample coordinates to world coordinates
                    float sampleFracX = (float)row / TileRowCount;
                    float sampleFracZ = (float)col / TileRowCount;
                    float worldX = tileWorldStartX + sampleFracX * TileSize;
                    float worldZ = tileWorldStartZ + sampleFracZ * TileSize;
                    
                    // Get ground truth
                    int index = row * TileRowCount + col;
                    var gtSample = samples[index];
                    var gtBiome = BiomeFromValue(gtSample.biome);
                    
                    // Get our predictions
                    var ourBiome = generator.GetBiome(worldX, worldZ);
                    var ourHeight = generator.GetBaseHeight(worldX, worldZ);
                    
                    // Track per-biome stats
                    if (!perBiomeStats.ContainsKey(gtBiome))
                    {
                        perBiomeStats[gtBiome] = (0, 0);
                    }
                    var (correct, total) = perBiomeStats[gtBiome];
                    total++;
                    
                    // Check biome match
                    if (ourBiome.ToString() == gtBiome)
                    {
                        biomeMatches++;
                        correct++;
                    }
                    else
                    {
                        var key = $"{gtBiome}→{ourBiome}";
                        biomeConfusion[key] = biomeConfusion.GetValueOrDefault(key, 0) + 1;
                        
                        // Track non-Mountain errors for spatial analysis
                        if (gtBiome != "Mountain" && ourBiome.ToString() != "Mountain")
                        {
                            nonMountainErrors.Add((worldX, worldZ, row, col, ourBiome.ToString(), gtBiome));
                        }
                        
                        if (!firstBiomeMismatch.found)
                        {
                            firstBiomeMismatch = (worldX, worldZ, ourBiome.ToString(), gtBiome, ourHeight, gtSample.height, true);
                        }
                    }
                    
                    perBiomeStats[gtBiome] = (correct, total);
                }
            }
            
            float biomeAccuracy = (float)biomeMatches / totalSamples * 100f;
            
            Debug.Log($"");
            Debug.Log($"OVERALL Biome Accuracy: {biomeAccuracy:F4}% ({biomeMatches:N0}/{totalSamples:N0})");
            
            Debug.Log($"");
            Debug.Log($"PER-BIOME Accuracy:");
            foreach (var kvp in perBiomeStats.OrderBy(x => x.Key))
            {
                var (correct, total) = kvp.Value;
                float accuracy = (float)correct / total * 100f;
                float population = (float)total / totalSamples * 100f;
                Debug.Log($"  {kvp.Key,-12}: {accuracy:F2}% ({correct:N0}/{total:N0}) - {population:F1}% of tile");
            }
            
            if (biomeConfusion.Any())
            {
                Debug.Log($"");
                Debug.Log($"Biome mismatches (top 10):");
                foreach (var kvp in biomeConfusion.OrderByDescending(x => x.Value).Take(10))
                {
                    float pct = (float)kvp.Value / totalSamples * 100f;
                    Debug.Log($"  {kvp.Key}: {kvp.Value:N0} ({pct:F2}%)");
                }
            }
            
            if (firstBiomeMismatch.found)
            {
                Debug.Log($"");
                Debug.Log($"First biome mismatch:");
                Debug.Log($"  Coord: ({firstBiomeMismatch.x:F2}, {firstBiomeMismatch.z:F2})");
                Debug.Log($"  Ours: {firstBiomeMismatch.ours} (height={firstBiomeMismatch.height:F6})");
                Debug.Log($"  GT:   {firstBiomeMismatch.gt} (height={firstBiomeMismatch.gtHeight:F6} meters)");
            }
            
            // Analyze non-Mountain error spatial distribution
            if (nonMountainErrors.Any())
            {
                Debug.Log($"");
                Debug.Log($"Non-Mountain error spatial analysis ({nonMountainErrors.Count} errors):");
                
                int edgeErrors = 0;
                int interiorErrors = 0;
                int edgeThreshold = 10; // Within 10 samples of edge
                
                foreach (var error in nonMountainErrors)
                {
                    bool isEdge = error.row < edgeThreshold || 
                                  error.row >= (TileRowCount - edgeThreshold) ||
                                  error.col < edgeThreshold || 
                                  error.col >= (TileRowCount - edgeThreshold);
                    
                    if (isEdge)
                    {
                        edgeErrors++;
                    }
                    else
                    {
                        interiorErrors++;
                        // Log interior errors for debugging
                        Debug.Log($"  INTERIOR error at [{error.row},{error.col}] ({error.x:F2},{error.z:F2}): {error.gt}→{error.ours}");
                    }
                }
                
                Debug.Log($"  Edge errors (within {edgeThreshold} samples of boundary): {edgeErrors}");
                Debug.Log($"  Interior errors: {interiorErrors}");
                
                if (interiorErrors == 0)
                {
                    Debug.Log($"  ✓ All non-Mountain errors are at tile edges (likely precision/sampling artifacts)");
                }
                else
                {
                    Debug.Log($"  ⚠ Interior errors detected - may indicate coordinate mapping issue");
                }
            }
            
            Assert.That(biomeAccuracy, Is.EqualTo(100f), $"Biome accuracy must be 100%, got {biomeAccuracy:F4}%");
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
        
        MapSample GetSampleAtCoordinate(string dataPath, float worldX, float worldZ)
        {
            float halfWorld = WorldWidth / 2f;
            float tileWorldX = worldX + halfWorld;
            float tileWorldZ = worldZ + halfWorld;
            
            int tileX = (int)(tileWorldX / TileSize);
            int tileZ = (int)(tileWorldZ / TileSize);
            
            tileX = Math.Max(0, Math.Min(TileSideCount - 1, tileX));
            tileZ = Math.Max(0, Math.Min(TileSideCount - 1, tileZ));
            
            float localX = tileWorldX - (tileX * TileSize);
            float localZ = tileWorldZ - (tileZ * TileSize);
            
            int sampleX = (int)((localX / TileSize) * TileRowCount);
            int sampleZ = (int)((localZ / TileSize) * TileRowCount);
            
            sampleX = Math.Max(0, Math.Min(TileRowCount - 1, sampleX));
            sampleZ = Math.Max(0, Math.Min(TileRowCount - 1, sampleZ));
            
            var tilePath = Path.Combine(dataPath, "tiles", $"{tileX:D2}-{tileZ:D2}.bin.gz");
            var samples = LoadTile(tilePath);
            
            int index = sampleX * TileRowCount + sampleZ;
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

        BiomeType BiomeFromBiomeValue(ushort value)
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
