using System;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class DebugOriginBiome
    {
        readonly ITestOutputHelper output;
        
        public DebugOriginBiome(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void TraceOriginBiomeCalculation()
        {
            // Step through GetBiome logic at origin to see why we get Mountain
            var generator = new WorldGenerator("HHcLC5acQt");
            
            float worldX = 0f;
            float worldZ = 0f;
            
            // Get the actual values
            var biome = generator.GetBiome(worldX, worldZ);
            var baseHeight = generator.GetBaseHeight(worldX, worldZ);
            var height = generator.GetHeight(worldX, worldZ);
            
            this.output.WriteLine($"Origin (0, 0) calculation:");
            this.output.WriteLine($"  baseHeight = {baseHeight:F4}");
            this.output.WriteLine($"  height (from GetHeight) = {height:F4}");
            this.output.WriteLine($"  final biome = {biome}");
            this.output.WriteLine($"");
            
            // Trace through GetBiome logic
            this.output.WriteLine("GetBiome logic trace:");
            this.output.WriteLine($"  distance = 0");
            this.output.WriteLine($"");
            
            // Check ocean condition
            const float oceanLevel = 0.02f;
            if (baseHeight <= oceanLevel)
            {
                this.output.WriteLine($"  baseHeight ({baseHeight:F4}) <= oceanLevel ({oceanLevel})");
                this.output.WriteLine($"  -> Should return Ocean");
            }
            else
            {
                this.output.WriteLine($"  baseHeight ({baseHeight:F4}) > oceanLevel ({oceanLevel})");
                this.output.WriteLine($"  -> NOT Ocean, continue checking...");
            }
            
            // Check mountain condition
            const float mountainThreshold = 0.4f;
            if (baseHeight > mountainThreshold)
            {
                this.output.WriteLine($"  baseHeight ({baseHeight:F4}) > mountainThreshold ({mountainThreshold})");
                this.output.WriteLine($"  -> RETURNED Mountain! ✗");
            }
            else
            {
                this.output.WriteLine($"  baseHeight ({baseHeight:F4}) <= mountainThreshold ({mountainThreshold})");
                this.output.WriteLine($"  -> NOT Mountain, would continue checking other biomes...");
            }
            
            this.output.WriteLine($"");
            this.output.WriteLine("PROBLEM: baseHeight is too high!");
            this.output.WriteLine($"Expected: Ocean (baseHeight should be < 0.02)");
            this.output.WriteLine($"Actual: Mountain (baseHeight = {baseHeight:F4} > 0.4)");
        }
        
        [Fact]
        public void CheckGetHeightVsGetBaseHeight()
        {
            // Are GetHeight and GetBaseHeight different?
            var generator = new WorldGenerator("HHcLC5acQt");
            
            float worldX = 0f;
            float worldZ = 0f;
            
            var baseHeight = generator.GetBaseHeight(worldX, worldZ);
            var height = generator.GetHeight(worldX, worldZ);
            
            this.output.WriteLine($"At origin (0, 0):");
            this.output.WriteLine($"  GetBaseHeight() = {baseHeight:F4}");
            this.output.WriteLine($"  GetHeight() = {height:F4}");
            this.output.WriteLine($"");
            
            if (Math.Abs(baseHeight - height) < 0.0001)
                this.output.WriteLine("✓ They return the same value");
            else
                this.output.WriteLine($"✗ They differ by {Math.Abs(baseHeight - height):F4}");
        }
        
        [Fact]
        public void SampleHeightsAroundOrigin()
        {
            // Sample a grid around origin to see the height pattern
            var generator = new WorldGenerator("HHcLC5acQt");
            
            this.output.WriteLine("Height and biome values around origin:");
            this.output.WriteLine("");
            
            for (float z = -500; z <= 500; z += 250)
            {
                for (float x = -500; x <= 500; x += 250)
                {
                    var height = generator.GetBaseHeight(x, z);
                    this.output.WriteLine($"({x,6},{z,6}): height={height,7:F4}");
                }
            }
        }
        
        [Fact]
        public void CompareOurHeightWithTileHeight()
        {
            // Check if our height calculation matches tile data at origin
            var tilePath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data\tiles\08-08.bin.gz";
            
            if (!System.IO.File.Exists(tilePath))
            {
                this.output.WriteLine("Tile file not found");
                return;
            }
            
            // Load tile 08-08 which covers world [0, 1500]
            using var fileStream = System.IO.File.OpenRead(tilePath);
            using var gzipStream = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
            using var memoryStream = new System.IO.MemoryStream();
            gzipStream.CopyTo(memoryStream);
            var tileData = memoryStream.ToArray();
            
            // Origin (0,0) is at tile sample (0, 0)
            // Sample format: uint16 biomeId, float height, float forestFactor (10 bytes)
            ushort biomeId = BitConverter.ToUInt16(tileData, 0);
            float tileHeight = BitConverter.ToSingle(tileData, 2);
            
            var generator = new WorldGenerator("HHcLC5acQt");
            var ourHeight = generator.GetBaseHeight(0, 0);
            var ourBiome = generator.GetBiome(0, 0);
            
            // Decode biome ID
            string tileBiomeName = biomeId switch
            {
                1 => "Meadows",
                2 => "Swamp",
                4 => "Mountain",
                8 => "BlackForest",
                16 => "Plains",
                32 => "AshLands",
                256 => "Ocean",
                512 => "Mistlands",
                _ => $"Unknown({biomeId})"
            };
            
            this.output.WriteLine("Comparison at origin (0, 0):");
            this.output.WriteLine($"Tile data:");
            this.output.WriteLine($"  BiomeID: {biomeId} ({tileBiomeName})");
            this.output.WriteLine($"  Height: {tileHeight:F4}");
            this.output.WriteLine($"");
            this.output.WriteLine($"Our generator:");
            this.output.WriteLine($"  Biome: {ourBiome}");
            this.output.WriteLine($"  Height: {ourHeight:F4}");
            this.output.WriteLine($"");
            
            if (tileBiomeName == ourBiome.ToString())
                this.output.WriteLine("✓ Biome MATCHES!");
            else
                this.output.WriteLine($"✗ Biome MISMATCH: {ourBiome} vs {tileBiomeName}");
            
            this.output.WriteLine($"Height difference: {Math.Abs(ourHeight - tileHeight):F4}");
        }
    }
}
