using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    /// <summary>
    /// Captures detailed world generation data for known Valheim seeds to validate accuracy.
    /// </summary>
    public class SeedValidationTests
    {
        readonly ITestOutputHelper output;
        
        public SeedValidationTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CaptureWorldData_KnownSeed_HHcLC5acQt()
        {
            // This is a popular/well-known Valheim seed
            CaptureWorldData("HHcLC5acQt");
        }
        
        [Fact]
        public void CaptureWorldData_SimpleSeed_Test()
        {
            CaptureWorldData("test");
        }
        
        [Fact]
        public void CaptureWorldData_SimpleSeed_1()
        {
            CaptureWorldData("1");
        }
        
        void CaptureWorldData(string seed)
        {
            var generator = new WorldGenerator(seed);
            var sb = new StringBuilder();
            
            sb.AppendLine($"=== WORLD DATA CAPTURE ===");
            sb.AppendLine($"Seed: '{seed}'");
            sb.AppendLine($"Seed Hash: {seed.GetStableHashCode()}");
            sb.AppendLine();
            
            // Section 1: Sample specific important locations
            sb.AppendLine("=== KEY LOCATIONS ===");
            var keyLocations = new (float x, float z, string name)[]
            {
                (0, 0, "Origin/Spawn"),
                (500, 0, "500m North"),
                (1000, 0, "1km North"),
                (2000, 0, "2km North"),
                (3000, 0, "3km North"),
                (5000, 0, "5km North"),
                (8000, 0, "8km North"),
                (0, 500, "500m East"),
                (0, 1000, "1km East"),
                (3000, 3000, "3km NE"),
                (5000, 5000, "5km NE"),
            };
            
            foreach (var loc in keyLocations)
            {
                var biome = generator.GetBiome(loc.x, loc.z);
                var height = generator.GetBaseHeight(loc.x, loc.z);
                sb.AppendLine($"  {loc.name,-20} ({loc.x,6}, {loc.z,6}): {biome,-15} h={height:F3}");
            }
            sb.AppendLine();
            
            // Section 2: Radial sampling at specific distances
            sb.AppendLine("=== RADIAL BIOME DISTRIBUTION ===");
            var distances = new[] { 500f, 1000f, 2000f, 3000f, 5000f, 7000f };
            
            foreach (var distance in distances)
            {
                var biomeCounts = new Dictionary<BiomeType, int>();
                int samples = 36; // Every 10 degrees
                
                for (int i = 0; i < samples; i++)
                {
                    float angle = i * 10f * (float)Math.PI / 180f;
                    float x = (float)Math.Cos(angle) * distance;
                    float z = (float)Math.Sin(angle) * distance;
                    
                    var biome = generator.GetBiome(x, z);
                    if (!biomeCounts.ContainsKey(biome))
                        biomeCounts[biome] = 0;
                    biomeCounts[biome]++;
                }
                
                sb.AppendLine($"  At {distance}m radius ({samples} samples):");
                foreach (var kvp in biomeCounts)
                {
                    float percent = kvp.Value * 100f / samples;
                    sb.AppendLine($"    {kvp.Key,-15} {kvp.Value,2} ({percent:F1}%)");
                }
                sb.AppendLine();
            }
            
            // Section 3: Grid map (detailed view of spawn area)
            sb.AppendLine("=== SPAWN AREA MAP (20x20 grid, 200m cells, 4km total) ===");
            sb.AppendLine("Legend: . = Meadows, # = BlackForest, ~ = Ocean, ^ = Mountain");
            sb.AppendLine("        S = Swamp, P = Plains, M = Mistlands, A = AshLands, D = DeepNorth");
            sb.AppendLine();
            
            int gridSize = 20;
            float cellSize = 200f;
            float halfExtent = gridSize * cellSize / 2f;
            
            for (int y = 0; y < gridSize; y++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    float wx = (x * cellSize) - halfExtent;
                    float wz = (y * cellSize) - halfExtent;
                    
                    var biome = generator.GetBiome(wx, wz);
                    sb.Append(BiomeToChar(biome));
                }
                sb.AppendLine();
            }
            sb.AppendLine();
            
            // Section 4: Height statistics by distance rings
            sb.AppendLine("=== HEIGHT STATISTICS BY DISTANCE ===");
            var rings = new (float min, float max, string label)[]
            {
                (0, 500, "0-500m"),
                (500, 1000, "500m-1km"),
                (1000, 2000, "1-2km"),
                (2000, 3000, "2-3km"),
                (3000, 5000, "3-5km"),
                (5000, 8000, "5-8km"),
            };
            
            var random = new Random(42);
            foreach (var ring in rings)
            {
                int samples = 200;
                float minHeight = float.MaxValue;
                float maxHeight = float.MinValue;
                float sumHeight = 0;
                
                for (int i = 0; i < samples; i++)
                {
                    float angle = (float)(random.NextDouble() * 2 * Math.PI);
                    float distance = ring.min + (float)(random.NextDouble() * (ring.max - ring.min));
                    float x = (float)Math.Cos(angle) * distance;
                    float z = (float)Math.Sin(angle) * distance;
                    
                    float height = generator.GetBaseHeight(x, z);
                    minHeight = Math.Min(minHeight, height);
                    maxHeight = Math.Max(maxHeight, height);
                    sumHeight += height;
                }
                
                float avgHeight = sumHeight / samples;
                sb.AppendLine($"  {ring.label,-10} min={minHeight:F3}, max={maxHeight:F3}, avg={avgHeight:F3}");
            }
            sb.AppendLine();
            
            // Section 5: Overall biome distribution (large sample)
            sb.AppendLine("=== OVERALL BIOME DISTRIBUTION (n=5000, radius=8km) ===");
            var allBiomes = new Dictionary<BiomeType, int>();
            random = new Random(42);
            int totalSamples = 5000;
            
            for (int i = 0; i < totalSamples; i++)
            {
                float angle = (float)(random.NextDouble() * 2 * Math.PI);
                float distance = (float)(random.NextDouble() * 8000);
                float x = (float)Math.Cos(angle) * distance;
                float z = (float)Math.Sin(angle) * distance;
                
                var biome = generator.GetBiome(x, z);
                if (!allBiomes.ContainsKey(biome))
                    allBiomes[biome] = 0;
                allBiomes[biome]++;
            }
            
            foreach (var kvp in allBiomes)
            {
                float percent = kvp.Value * 100f / totalSamples;
                sb.AppendLine($"  {kvp.Key,-15} {kvp.Value,5} ({percent:F1}%)");
            }
            
            this.output.WriteLine(sb.ToString());
            Assert.True(true);
        }
        
        static char BiomeToChar(BiomeType biome)
        {
            return biome switch
            {
                BiomeType.Meadows => '.',
                BiomeType.BlackForest => '#',
                BiomeType.Ocean => '~',
                BiomeType.Mountain => '^',
                BiomeType.Swamp => 'S',
                BiomeType.Plains => 'P',
                BiomeType.Mistlands => 'M',
                BiomeType.AshLands => 'A',
                BiomeType.DeepNorth => 'D',
                _ => '?'
            };
        }
    }
}
