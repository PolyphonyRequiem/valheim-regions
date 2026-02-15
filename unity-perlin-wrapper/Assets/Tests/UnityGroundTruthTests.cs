using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.WorldGen.Tests
{
    public class UnityGroundTruthTests
    {
        // Ground truth data: coordinates sampled from actual Valheim world "HHcLC5acQt"
        // Format: (worldX, worldZ, expectedBiome)
        private static readonly (float x, float z, BiomeType biome)[] GroundTruthSamples = new[]
        {
            // Ocean samples (should have very high accuracy)
            (-11709f, 11853f, BiomeType.BlackForest),
            (-11661f, 11853f, BiomeType.BlackForest),
            (-11613f, 11853f, BiomeType.BlackForest),
            (-3476f, 979f, BiomeType.BlackForest),
            (-2692f, 563f, BiomeType.Meadows),
            (-2099f, 116f, BiomeType.Meadows),
            
            // Add more samples here as we validate
        };

        [Test]
        public void ValidateUnityImplementation_AgainstGroundTruth()
        {
            var generator = new WorldGeneratorUnity("HHcLC5acQt");
            
            int matches = 0;
            int total = 0;
            var mismatches = new List<string>();
            
            foreach (var sample in GroundTruthSamples)
            {
                var ourBiome = generator.GetBiome(sample.x, sample.z);
                total++;
                
                if (ourBiome == sample.biome)
                {
                    matches++;
                }
                else
                {
                    mismatches.Add($"({sample.x:F0}, {sample.z:F0}): Expected={sample.biome}, Got={ourBiome}");
                }
            }
            
            float accuracy = (float)matches / total * 100f;
            
            Debug.Log($"Ground Truth Validation:");
            Debug.Log($"  Matches: {matches}/{total} ({accuracy:F1}%)");
            
            if (mismatches.Count > 0)
            {
                Debug.Log($"  Mismatches:");
                foreach (var mm in mismatches.Take(10))
                {
                    Debug.Log($"    {mm}");
                }
            }
            
            // For now, just log results - we'll add more samples iteratively
            Assert.GreaterOrEqual(accuracy, 50f, "Should have at least 50% accuracy with Unity's real Perlin");
        }
        
        [Test]
        public void ShowUnityOffsets_ForHHcLC5acQt()
        {
            var generator = new WorldGeneratorUnity("HHcLC5acQt");
            var type = generator.GetType();
            
            var offset0 = type.GetField("offset0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset1 = type.GetField("offset1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset2 = type.GetField("offset2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset3 = type.GetField("offset3", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset4 = type.GetField("offset4", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            Debug.Log("Unity offsets for 'HHcLC5acQt':");
            Debug.Log($"  offset0: {offset0.GetValue(generator)}");
            Debug.Log($"  offset1: {offset1.GetValue(generator)}");
            Debug.Log($"  offset2: {offset2.GetValue(generator)}");
            Debug.Log($"  offset3: {offset3.GetValue(generator)}");
            Debug.Log($"  offset4: {offset4.GetValue(generator)}");
        }
    }
}
