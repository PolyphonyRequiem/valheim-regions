using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.Validation
{
    public class CompareWithValheimWorldGen
    {
        readonly ITestOutputHelper output;
        
        public CompareWithValheimWorldGen(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CompareRandomOffsets_SystemVsUnity()
        {
            var seed = "HHcLC5acQt";
            var hashCode = seed.GetStableHashCode();
            
            this.output.WriteLine($"Seed: '{seed}'");
            this.output.WriteLine($"Hash: {hashCode}");
            this.output.WriteLine($"");
            
            // System.Random (what we're using)
            var sysRandom = new Random(hashCode);
            this.output.WriteLine("System.Random offsets:");
            for (int i = 0; i < 6; i++)
            {
                var value = sysRandom.NextDouble() * 100000.0;
                this.output.WriteLine($"  offset{i} = {value:F3}");
            }
            this.output.WriteLine($"");
            
            // Unity Random (what Valheim uses)
            this.output.WriteLine("UnityEngine.Random offsets:");
            UnityEngine.Random.InitState(hashCode);
            for (int i = 0; i < 6; i++)
            {
                var value = UnityEngine.Random.value * 100000.0;
                this.output.WriteLine($"  offset{i} = {value:F3}");
            }
            this.output.WriteLine($"");
            
            Assert.True(true);
        }
        
        [Fact]
        public void CompareCoordinates_OursVsValheim()
        {
            var seed = "HHcLC5acQt";
            var hashCode = seed.GetStableHashCode();
            
            // Our implementation
            var ourGen = new WorldZones.WorldGen.WorldGenerator(seed);
            
            // Valheim's implementation
            var valheimGen = new WorldGenerator();
            UnityEngine.Random.InitState(hashCode);
            valheimGen.Initialize(UnityEngine.Random.state);
            
            var testCoords = new[] {
                (0f, 0f, "Ocean"),
                (-686f, 1744f, "Ocean"),
                (2564f, -1189f, "Mountain?"),
                (-127f, 262f, "Meadows")
            };
            
            this.output.WriteLine($"Coordinate comparison (seed: '{seed}'):");
            this.output.WriteLine($"");
            
            foreach (var (x, z, expected) in testCoords)
            {
                var ourBiome = ourGen.GetBiome(x, z);
                var ourHeight = ourGen.GetBaseHeight(x, z);
                
                var valheimBiome = valheimGen.GetBiome(x, z);
                var valheimHeight = valheimGen.GetBaseHeight(x, z, out _, out _);
                
                var biomeMatch = ourBiome.ToString() == valheimBiome.ToString() ? "YES" : "NO";
                var heightMatch = Math.Abs(ourHeight - valheimHeight) < 0.01f ? "YES" : "NO";
                
                this.output.WriteLine($"({x,6:F0}, {z,6:F0}) [expected: {expected}]");
                this.output.WriteLine($"  Ours:    {ourBiome,-12} h={ourHeight:F4}");
                this.output.WriteLine($"  Valheim: {valheimBiome,-12} h={valheimHeight:F4}");
                this.output.WriteLine($"  Match: {biomeMatch} biome, {heightMatch} height");
                this.output.WriteLine($"");
            }
            
            Assert.True(true);
        }
    }
}
