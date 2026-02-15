using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class CompareTileVsPngDistribution
    {
        readonly ITestOutputHelper output;
        
        public CompareTileVsPngDistribution(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CompareCenterTile_8_8()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            var tilePath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data\tiles\08-08.bin.gz";
            
            if (!File.Exists(pngPath) || !File.Exists(tilePath)) return;
            
            // Tile (8,8) covers world coordinates [0, 1500] for both X and Z
            int worldXMin = 0;
            int worldXMax = 1500;
            int worldZMin = 0;
            int worldZMax = 1500;
            
            this.output.WriteLine($"Comparing tile (8,8) covering world region:");
            this.output.WriteLine($"  X: [{worldXMin}, {worldXMax}]");
            this.output.WriteLine($"  Z: [{worldZMin}, {worldZMax}]");
            this.output.WriteLine("");
            
            // Sample tile data
            var tileBiomes = SampleTileData(tilePath);
            
            // Try different PNG world size guesses
            var worldSizeGuesses = new[] { 21000.0, 24000.0, 24609.0 };
            
            foreach (var worldSize in worldSizeGuesses)
            {
                this.output.WriteLine($"Testing PNG with worldSize = {worldSize}");
                var pngBiomes = SamplePngData(pngPath, worldXMin, worldXMax, worldZMin, worldZMax, worldSize);
                
                int tileTotal = tileBiomes.Values.Sum();
                int pngTotal = pngBiomes.Values.Sum();
                
                this.output.WriteLine($"  Tile samples: {tileTotal:N0} (after excluding void/shallows)");
                this.output.WriteLine("  Tile biome distribution:");
                foreach (var kvp in tileBiomes.OrderByDescending(k => k.Value))
                {
                    double pct = (kvp.Value * 100.0) / tileTotal;
                    this.output.WriteLine($"    {kvp.Key,-15}: {pct,6:F2}%");
                }
                
                this.output.WriteLine($"  PNG samples: {pngTotal:N0} (after excluding gradients/shallows)");
                this.output.WriteLine("  PNG biome distribution:");
                foreach (var kvp in pngBiomes.OrderByDescending(k => k.Value))
                {
                    double pct = (kvp.Value * 100.0) / pngTotal;
                    this.output.WriteLine($"    {kvp.Key,-15}: {pct,6:F2}%");
                }
                
                // Calculate similarity
                double similarity = CalculateSimilarity(tileBiomes, pngBiomes);
                this.output.WriteLine($"  Distribution similarity: {similarity:F2}%");
                this.output.WriteLine("");
            }
        }
        
        Dictionary<string, int> SampleTileData(string tilePath)
        {
            var biomeCounts = new Dictionary<string, int>();
            
            using var fileStream = File.OpenRead(tilePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            
            // Decompress entire file into memory
            gzipStream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            
            using var reader = new BinaryReader(memoryStream);
            
            const int TileRowCount = 1024;
            
            for (int sampleZ = 0; sampleZ < TileRowCount; sampleZ++)
            {
                for (int sampleX = 0; sampleX < TileRowCount; sampleX++)
                {
                    int index = sampleX * TileRowCount + sampleZ;
                    long offset = index * 10;
                    
                    memoryStream.Position = offset;
                    
                    ushort biomeValue = reader.ReadUInt16();
                    float height = reader.ReadSingle();
                    
                    // Skip void and shallows
                    if (height <= -400) continue; // Void
                    
                    string biomeName = BiomeValueToName(biomeValue, height);
                    if (biomeName == "Shallows") continue; // Skip shallows
                    
                    if (!biomeCounts.ContainsKey(biomeName))
                        biomeCounts[biomeName] = 0;
                    biomeCounts[biomeName]++;
                }
            }
            
            return biomeCounts;
        }
        
        Dictionary<string, int> SamplePngData(string pngPath, int worldXMin, int worldXMax, int worldZMin, int worldZMax, double pngWorldSize)
        {
            var biomeCounts = new Dictionary<string, int>();
            
            using var png = new Bitmap(pngPath);
            
            int centerPx = 4095;
            int centerPy = 4095;
            double pixelToWorld = pngWorldSize / 8192.0;
            
            // Sample every 10 world units for speed
            for (int worldX = worldXMin; worldX < worldXMax; worldX += 10)
            {
                for (int worldZ = worldZMin; worldZ < worldZMax; worldZ += 10)
                {
                    int px = centerPx + (int)(worldX / pixelToWorld);
                    int py = centerPy - (int)(worldZ / pixelToWorld); // Z flipped
                    
                    if (px < 0 || px >= png.Width || py < 0 || py >= png.Height) continue;
                    
                    var color = png.GetPixel(px, py);
                    var biomeName = GetExactBiomeMatch(color.R, color.G, color.B);
                    
                    // Skip gradients (null) and Shallows
                    if (biomeName == null || biomeName == "Shallows") continue;
                    
                    if (!biomeCounts.ContainsKey(biomeName))
                        biomeCounts[biomeName] = 0;
                    biomeCounts[biomeName]++;
                }
            }
            
            return biomeCounts;
        }
        
        double CalculateSimilarity(Dictionary<string, int> dist1, Dictionary<string, int> dist2)
        {
            var allBiomes = dist1.Keys.Union(dist2.Keys).ToList();
            
            int total1 = dist1.Values.Sum();
            int total2 = dist2.Values.Sum();
            
            if (total1 == 0 || total2 == 0) return 0;
            
            double sumDiff = 0;
            foreach (var biome in allBiomes)
            {
                double pct1 = dist1.ContainsKey(biome) ? (dist1[biome] * 100.0 / total1) : 0;
                double pct2 = dist2.ContainsKey(biome) ? (dist2[biome] * 100.0 / total2) : 0;
                sumDiff += Math.Abs(pct1 - pct2);
            }
            
            return 100.0 - (sumDiff / 2.0); // Convert to similarity percentage
        }
        
        string BiomeValueToName(ushort biome, float height)
        {
            if (height <= -400) return "Void";
            if (biome == 256 && height > 0.4f) return "Shallows";
            if (biome == 256) return "Ocean";
            if (biome == 1) return "Meadows";
            if (biome == 2) return "Swamp";
            if (biome == 4) return "Mountain";
            if (biome == 8) return "BlackForest";
            if (biome == 16) return "Plains";
            if (biome == 32) return "AshLands";
            if (biome == 64) return "DeepNorth";
            if (biome == 512) return "Mistlands";
            return $"Unknown({biome})";
        }
        
        string GetExactBiomeMatch(int r, int g, int b)
        {
            int tolerance = 3;
            
            if (IsColor(r, g, b, 0, 0, 153, tolerance)) return "Ocean";
            if (IsColor(r, g, b, 102, 102, 255, tolerance)) return "Shallows";
            if (IsColor(r, g, b, 52, 94, 59, tolerance)) return "BlackForest";
            if (IsColor(r, g, b, 145, 167, 91, tolerance)) return "Meadows";
            if (IsColor(r, g, b, 82, 82, 82, tolerance)) return "Mistlands";
            if (IsColor(r, g, b, 255, 255, 255, tolerance)) return "Mountain";
            if (IsColor(r, g, b, 199, 199, 49, tolerance)) return "Plains";
            if (IsColor(r, g, b, 163, 113, 87, tolerance)) return "Swamp";
            if (IsColor(r, g, b, 255, 0, 0, tolerance)) return "AshLands";
            
            return null; // Gradient
        }
        
        bool IsColor(int cr, int cg, int cb, int r, int g, int b, int tolerance)
        {
            return Math.Abs(cr - r) <= tolerance && 
                   Math.Abs(cg - g) <= tolerance && 
                   Math.Abs(cb - b) <= tolerance;
        }
    }
}
