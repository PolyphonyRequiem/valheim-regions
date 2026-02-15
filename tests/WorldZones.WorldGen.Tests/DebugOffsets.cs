using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class DebugOffsets
    {
        private readonly ITestOutputHelper output;

        public DebugOffsets(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void ShowOffsetsForHHcLC5acQt()
        {
            var generator = new WorldGenerator("HHcLC5acQt");
            var type = generator.GetType();
            
            var offset0 = type.GetField("offset0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset1 = type.GetField("offset1", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset2 = type.GetField("offset2", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset3 = type.GetField("offset3", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var offset4 = type.GetField("offset4", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            output.WriteLine("WorldGenerator offsets for 'HHcLC5acQt':");
            output.WriteLine($"  offset0: {offset0.GetValue(generator)}");
            output.WriteLine($"  offset1: {offset1.GetValue(generator)}");
            output.WriteLine($"  offset2: {offset2.GetValue(generator)}");
            output.WriteLine($"  offset3: {offset3.GetValue(generator)}");
            output.WriteLine($"  offset4: {offset4.GetValue(generator)}");
            output.WriteLine("");
            output.WriteLine("Expected Unity offsets:");
            output.WriteLine("  offset0: 20292.78576374054");
            output.WriteLine("  offset1: 64034.789800643921");
            output.WriteLine("  offset2: 71026.712656021118");
            output.WriteLine("  offset3: 6663.8357937335968");
            output.WriteLine("  offset4: 38212.889432907104");
        }
    }
}
