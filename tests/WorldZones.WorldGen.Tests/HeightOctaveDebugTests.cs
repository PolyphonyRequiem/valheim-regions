using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class HeightOctaveDebugTests
    {
        readonly ITestOutputHelper output;
        
        public HeightOctaveDebugTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void Debug_FirstOctaveValues()
        {
            // Create a noise generator like WorldGenerator does
            var noise = new FastNoiseLite(42 + 100); // seed + 100 like noiseBase
            noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            noise.SetFractalType(FastNoiseLite.FractalType.None);
            
            float worldX = 3000f;
            float worldZ = 0f;
            
            // Simulate GetBaseHeight's coordinate transformation
            double offsetX = 53114.4; // Example offset from Random
            double offsetY = 18267.9;
            double x = worldX + 100000.0 + offsetX;
            double y = worldZ + 100000.0 + offsetY;
            
            // First octave calculations
            float n1Raw = noise.GetNoise((float)(x * 0.002 * 0.5), (float)(y * 0.002 * 0.5));
            float n2Raw = noise.GetNoise((float)(x * 0.003 * 0.5), (float)(y * 0.003 * 0.5));
            float n1 = (n1Raw + 1f) * 0.5f;
            float n2 = (n2Raw + 1f) * 0.5f;
            float firstOctave = n1 * n2 * 1.0f;
            
            this.output.WriteLine($"Position: ({worldX}, {worldZ})");
            this.output.WriteLine($"Transformed coords: ({x:F1}, {y:F1})");
            this.output.WriteLine($"");
            this.output.WriteLine($"n1 raw: {n1Raw:F6} → normalized: {n1:F6}");
            this.output.WriteLine($"n2 raw: {n2Raw:F6} → normalized: {n2:F6}");
            this.output.WriteLine($"First octave (n1 * n2): {firstOctave:F6}");
            this.output.WriteLine($"");
            this.output.WriteLine("If first octave is near zero, subsequent octaves");
            this.output.WriteLine("that multiply by height will also be zero!");
            
            Assert.True(true);
        }
    }
}
