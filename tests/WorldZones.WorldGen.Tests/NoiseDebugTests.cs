using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class NoiseDebugTests
    {
        readonly ITestOutputHelper output;
        
        public NoiseDebugTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void Debug_RawNoiseValues()
        {
            var noise = new FastNoiseLite(42);
            noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            noise.SetFractalType(FastNoiseLite.FractalType.None);
            
            this.output.WriteLine("Raw FastNoiseLite Perlin noise values:");
            this.output.WriteLine("");
            
            for (int i = 0; i < 10; i++)
            {
                float x = i * 100f;
                float y = i * 50f;
                float value = noise.GetNoise(x, y);
                this.output.WriteLine($"  ({x,6}, {y,6}): {value:F6}");
            }
            
            Assert.True(true);
        }
        
        [Fact]
        public void Debug_GetBaseHeightStepByStep()
        {
            var generator = new WorldGenerator("TestWorld");
            float worldX = 500f;
            float worldZ = 0f;
            
            float height = generator.GetBaseHeight(worldX, worldZ);
            
            this.output.WriteLine($"GetBaseHeight({worldX}, {worldZ}):");
            this.output.WriteLine($"  Final height: {height:F6}");
            this.output.WriteLine($"  (Expected: non-zero with Perlin noise)");
            
            Assert.True(true);
        }
    }
}
