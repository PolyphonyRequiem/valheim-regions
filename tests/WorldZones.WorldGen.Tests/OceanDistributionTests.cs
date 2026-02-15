using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class OceanDistributionTests
    {
        readonly ITestOutputHelper output;
        
        public OceanDistributionTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void AnalyzeOceanByDistance_Seed1()
        {
            var generator = new WorldGenerator("1");
            
            this.output.WriteLine("Ocean Distribution by Distance Rings:");
            this.output.WriteLine("");
            
            var rings = new (float min, float max, string label)[]
            {
                (0, 2000, "0-2km"),
                (2000, 4000, "2-4km"),
                (4000, 6000, "4-6km"),
                (6000, 8000, "6-8km"),
                (8000, 9000, "8-9km"),
                (9000, 10000, "9-10km"),
                (10000, 10500, "10-10.5km (edge)"),
            };
            
            var random = new Random(42);
            foreach (var ring in rings)
            {
                int samples = 500;
                int oceanCount = 0;
                
                for (int i = 0; i < samples; i++)
                {
                    float angle = (float)(random.NextDouble() * 2 * Math.PI);
                    float distance = ring.min + (float)(random.NextDouble() * (ring.max - ring.min));
                    float x = (float)Math.Cos(angle) * distance;
                    float z = (float)Math.Sin(angle) * distance;
                    
                    var biome = generator.GetBiome(x, z);
                    if (biome == BiomeType.Ocean)
                        oceanCount++;
                }
                
                float percent = oceanCount * 100f / samples;
                this.output.WriteLine($"  {ring.label,-20} Ocean: {oceanCount,3}/{samples} ({percent:F1}%)");
            }
            
            Assert.True(true);
        }
    }
}
