using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class RiverDebugTests
    {
        readonly ITestOutputHelper output;
        
        public RiverDebugTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void Debug_RiverCalculation_At3000m()
        {
            // Manually compute what GetBaseHeight does at 3000m
            var generator = new WorldGenerator("TestWorld");
            
            // This is just to get the offsets - hacky but works for debugging
            float worldX = 3000f;
            float worldZ = 0f;
            float distance = MathUtils.Length(worldX, worldZ);
            
            this.output.WriteLine($"Testing at position ({worldX}, {worldZ}):");
            this.output.WriteLine($"  Distance from origin: {distance}");
            this.output.WriteLine($"  SmoothStep(744, 1000, {distance}): {MathUtils.SmoothStep(744f, 1000f, distance)}");
            this.output.WriteLine("");
            this.output.WriteLine("If SmoothStep returns 1.0, and riverFactor becomes 1.0,");
            this.output.WriteLine("then height *= (1 - 1) = 0, which explains the zeros!");
            
            float height = generator.GetBaseHeight(worldX, worldZ);
            this.output.WriteLine($"");
            this.output.WriteLine($"Final height: {height}");
            
            Assert.True(true);
        }
    }
}
