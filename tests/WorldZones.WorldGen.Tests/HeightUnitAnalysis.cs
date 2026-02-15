using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class HeightUnitAnalysis
    {
        readonly ITestOutputHelper output;
        
        public HeightUnitAnalysis(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void AnalyzeHeightRanges()
        {
            // Sample many points to understand the relationship between
            // our normalized heights and tile meter heights
            var tilePath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data\tiles\08-08.bin.gz";
            
            if (!File.Exists(tilePath))
            {
                this.output.WriteLine("Tile file not found");
                return;
            }
            
            using var fileStream = File.OpenRead(tilePath);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);
            var tileData = memoryStream.ToArray();
            
            var generator = new WorldGenerator("HHcLC5acQt");
            
            var samples = new List<(float ourNormalized, float tileMeters, float worldX, float worldZ)>();
            
            // Sample every 100th point in the tile
            for (int sampleX = 0; sampleX < 1024; sampleX += 10)
            {
                for (int sampleZ = 0; sampleZ < 1024; sampleZ += 10)
                {
                    int index = sampleX * 1024 + sampleZ;
                    int byteIndex = index * 10;
                    
                    if (byteIndex + 5 >= tileData.Length) continue;
                    
                    ushort biomeId = BitConverter.ToUInt16(tileData, byteIndex);
                    float tileHeight = BitConverter.ToSingle(tileData, byteIndex + 2);
                    
                    if (tileHeight == -400f) continue; // Skip void
                    
                    // Convert sample coords to world coords
                    float worldX = (sampleX / 1024f) * 1500f;
                    float worldZ = (sampleZ / 1024f) * 1500f;
                    
                    float ourHeight = generator.GetBaseHeight(worldX, worldZ);
                    
                    samples.Add((ourHeight, tileHeight, worldX, worldZ));
                }
            }
            
            this.output.WriteLine($"Analyzed {samples.Count} sample points");
            this.output.WriteLine("");
            
            // Find ranges
            var ourMin = samples.Min(s => s.ourNormalized);
            var ourMax = samples.Max(s => s.ourNormalized);
            var tileMin = samples.Min(s => s.tileMeters);
            var tileMax = samples.Max(s => s.tileMeters);
            
            this.output.WriteLine($"Our normalized height range: [{ourMin:F3}, {ourMax:F3}]");
            this.output.WriteLine($"Tile meter height range: [{tileMin:F1}, {tileMax:F1}]");
            this.output.WriteLine("");
            
            // Show some sample mappings
            this.output.WriteLine("Sample mappings (ours vs tile):");
            foreach (var sample in samples.Take(20))
            {
                this.output.WriteLine($"  ({sample.worldX,7:F1},{sample.worldZ,7:F1}): our={sample.ourNormalized,7:F4} tile={sample.tileMeters,8:F2}m");
            }
            
            // Check if there's a linear relationship
            // If our height scales to tile height, what's the factor?
            this.output.WriteLine("");
            this.output.WriteLine("Attempting to find scale factor:");
            this.output.WriteLine("If tileHeight = ourHeight * scale + offset, what are scale and offset?");
            
            // Simple linear regression
            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            int n = samples.Count;
            
            foreach (var s in samples)
            {
                sumX += s.ourNormalized;
                sumY += s.tileMeters;
                sumXY += s.ourNormalized * s.tileMeters;
                sumX2 += s.ourNormalized * s.ourNormalized;
            }
            
            double scale = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double offset = (sumY - scale * sumX) / n;
            
            this.output.WriteLine($"Best fit: tileHeight = {scale:F2} * ourHeight + {offset:F2}");
            
            // Test the conversion
            var testSample = samples[0];
            var predicted = testSample.ourNormalized * scale + offset;
            this.output.WriteLine("");
            this.output.WriteLine($"Test conversion at ({testSample.worldX:F1}, {testSample.worldZ:F1}):");
            this.output.WriteLine($"  Our height: {testSample.ourNormalized:F4}");
            this.output.WriteLine($"  Predicted tile height: {predicted:F2}m");
            this.output.WriteLine($"  Actual tile height: {testSample.tileMeters:F2}m");
            this.output.WriteLine($"  Error: {Math.Abs(predicted - testSample.tileMeters):F2}m");
        }
    }
}
