using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class SpecificCoordinateTests
    {
        readonly ITestOutputHelper output;
        
        public SpecificCoordinateTests(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CheckCoordinate_HHcLC5acQt_Neg3476_979()
        {
            var generator = new WorldGenerator("HHcLC5acQt");
            
            float x = -3476f;
            float z = 979f;
            
            var biome = generator.GetBiome(x, z);
            var height = generator.GetBaseHeight(x, z);
            
            this.output.WriteLine($"Seed: 'HHcLC5acQt'");
            this.output.WriteLine($"Coordinate: ({x}, {z})");
            this.output.WriteLine($"");
            this.output.WriteLine($"Biome:  {biome}");
            this.output.WriteLine($"Height: {height:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"Is Ocean: {(biome == BiomeType.Ocean ? "YES" : "NO")}");
            this.output.WriteLine($"Height < 0.02 threshold: {(height <= 0.02f ? "YES (would be ocean)" : "NO")}");
            
            Assert.True(true);
        }
        
        [Fact]
        public void CheckCoordinate_HHcLC5acQt_Neg2692_563()
        {
            var generator = new WorldGenerator("HHcLC5acQt");
            
            float x = -2692f;
            float z = 563f;
            
            var biome = generator.GetBiome(x, z);
            var height = generator.GetBaseHeight(x, z);
            
            this.output.WriteLine($"Seed: 'HHcLC5acQt'");
            this.output.WriteLine($"Coordinate: ({x}, {z})");
            this.output.WriteLine($"");
            this.output.WriteLine($"Biome:  {biome}");
            this.output.WriteLine($"Height: {height:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"Is Ocean: {(biome == BiomeType.Ocean ? "YES" : "NO")}");
            this.output.WriteLine($"Height < 0.02 threshold: {(height <= 0.02f ? "YES (would be ocean)" : "NO")}");
            
            Assert.True(true);
        }
        
        [Fact]
        public void CheckCoordinate_HHcLC5acQt_Neg2099_116()
        {
            var generator = new WorldGenerator("HHcLC5acQt");
            
            float x = -2099f;
            float z = 116f;
            
            var biome = generator.GetBiome(x, z);
            var height = generator.GetBaseHeight(x, z);
            
            this.output.WriteLine($"Seed: 'HHcLC5acQt'");
            this.output.WriteLine($"Coordinate: ({x}, {z})");
            this.output.WriteLine($"");
            this.output.WriteLine($"Biome:  {biome}");
            this.output.WriteLine($"Height: {height:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"Is Ocean: {(biome == BiomeType.Ocean ? "YES" : "NO")}");
            this.output.WriteLine($"Is Mountain (>0.4): {(height > 0.4f ? "YES" : "NO")}");
            
            Assert.True(true);
        }
        
        [Fact]
        public void CheckSurroundingArea_Seed1_Neg3476_979()
        {
            var generator = new WorldGenerator("1");
            
            float centerX = -3476f;
            float centerZ = 979f;
            
            this.output.WriteLine($"Area around ({centerX}, {centerZ}) - Seed '1'");
            this.output.WriteLine($"Grid: 11x11 cells, 100m spacing (1km x 1km area)");
            this.output.WriteLine($"");
            
            for (int z = -5; z <= 5; z++)
            {
                var line = "";
                for (int x = -5; x <= 5; x++)
                {
                    float wx = centerX + (x * 100f);
                    float wz = centerZ + (z * 100f);
                    
                    var biome = generator.GetBiome(wx, wz);
                    char c = BiomeToChar(biome);
                    line += c;
                }
                this.output.WriteLine(line);
            }
            
            this.output.WriteLine($"");
            this.output.WriteLine($"Legend: . = Meadows, # = BlackForest, ~ = Ocean, ^ = Mountain");
            this.output.WriteLine($"        S = Swamp, P = Plains, M = Mistlands");
            
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
                _ => '?'
            };
        }
    }
}
