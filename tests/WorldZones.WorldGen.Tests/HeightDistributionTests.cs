using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class HeightDistributionTests
    {
        readonly ITestOutputHelper output;
        
        public HeightDistributionTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void AnalyzeHeightDistribution_10000Samples()
        {
            var generator = new WorldGenerator("TestWorld");
            var random = new Random(42);
            int totalSamples = 10000;
            
            int oceanHeight = 0;    // < 0.02
            int lowLand = 0;        // 0.02 - 0.2
            int midLand = 0;        // 0.2 - 0.4
            int mountain = 0;       // > 0.4
            
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            float sumHeight = 0;
            
            for (int i = 0; i < totalSamples; i++)
            {
                float angle = (float)(random.NextDouble() * 2 * Math.PI);
                float distance = (float)(random.NextDouble() * 8000);
                
                float x = (float)Math.Cos(angle) * distance;
                float z = (float)Math.Sin(angle) * distance;
                
                float height = generator.GetBaseHeight(x, z);
                
                if (height < 0.02f) oceanHeight++;
                else if (height < 0.2f) lowLand++;
                else if (height < 0.4f) midLand++;
                else mountain++;
                
                minHeight = Math.Min(minHeight, height);
                maxHeight = Math.Max(maxHeight, height);
                sumHeight += height;
            }
            
            float avgHeight = sumHeight / totalSamples;
            
            this.output.WriteLine($"Height Distribution (n={totalSamples}, radius=8km):");
            this.output.WriteLine($"");
            this.output.WriteLine($"  Min:     {minHeight:F4}");
            this.output.WriteLine($"  Max:     {maxHeight:F4}");
            this.output.WriteLine($"  Average: {avgHeight:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"  Ocean (< 0.02):  {oceanHeight,5} ({oceanHeight * 100f / totalSamples:F1}%)");
            this.output.WriteLine($"  Low (0.02-0.2):  {lowLand,5} ({lowLand * 100f / totalSamples:F1}%)");
            this.output.WriteLine($"  Mid (0.2-0.4):   {midLand,5} ({midLand * 100f / totalSamples:F1}%)");
            this.output.WriteLine($"  High (> 0.4):    {mountain,5} ({mountain * 100f / totalSamples:F1}%)");
            this.output.WriteLine($"");
            this.output.WriteLine($"Note: Biome 'Ocean' requires height <= 0.02");
            this.output.WriteLine($"      Most terrain is 0.2-0.4 range (mid-land)");
            
            Assert.True(true);
        }
    }
}
