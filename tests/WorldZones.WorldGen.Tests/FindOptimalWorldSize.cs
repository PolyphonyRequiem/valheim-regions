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
    public class FindOptimalWorldSize
    {
        private readonly ITestOutputHelper output;

        public FindOptimalWorldSize(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void TestWorldSize_24576_ExactScale3()
        {
            // If scale is exactly 3.0 m/pixel: 8192 * 3.0 = 24576
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            var tilePath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data\tiles\08-08.bin.gz";

            // Load tile data once
            byte[] tileData = LoadTileData(tilePath);
            double worldXMin = 0;
            double worldXMax = 1500;
            double worldZMin = 0;
            double worldZMax = 1500;

            int worldSize = 24576;
            using var png = new Bitmap(pngPath);

            int centerPx = 4095;
            int centerPy = 4095;
            double pixelToWorld = worldSize / 8192.0;

            // Calculate PNG pixel range
            int pngXMin = centerPx + (int)(worldXMin / pixelToWorld);
            int pngXMax = centerPx + (int)(worldXMax / pixelToWorld);
            int pngYMin = centerPy - (int)(worldZMax / pixelToWorld);
            int pngYMax = centerPy - (int)(worldZMin / pixelToWorld);

            int totalPngPixels = 0;
            int skippedGradient = 0;
            int skippedShallows = 0;
            int matchedPixels = 0;
            int mismatchedPixels = 0;

            for (int px = pngXMin; px <= pngXMax; px++)
            {
                for (int py = pngYMin; py <= pngYMax; py++)
                {
                    if (px < 0 || px >= png.Width || py < 0 || py >= png.Height) continue;

                    totalPngPixels++;

                    var color = png.GetPixel(px, py);
                    var pngBiome = GetExactBiomeMatch(color.R, color.G, color.B);

                    if (pngBiome == null)
                    {
                        skippedGradient++;
                        continue;
                    }

                    if (pngBiome == "Shallows")
                    {
                        skippedShallows++;
                        continue;
                    }

                    double worldX = (px - centerPx) * pixelToWorld;
                    double worldZ = -(py - centerPy) * pixelToWorld;

                    var tileBiome = GetTileBiomeAt(tileData, worldX, worldZ);

                    if (tileBiome == null) continue;

                    if (pngBiome == tileBiome)
                        matchedPixels++;
                    else
                        mismatchedPixels++;
                }
            }

            int comparedPixels = matchedPixels + mismatchedPixels;
            double matchPct = comparedPixels > 0 ? (matchedPixels * 100.0 / comparedPixels) : 0;

            this.output.WriteLine($"WorldSize: {worldSize} meters");
            this.output.WriteLine($"Pixel to World Scale: {pixelToWorld:F4} m/pixel");
            this.output.WriteLine("");
            this.output.WriteLine($"PNG Pixels Scanned: {totalPngPixels:N0}");
            this.output.WriteLine($"  - Skipped (gradients): {skippedGradient:N0}");
            this.output.WriteLine($"  - Skipped (shallows): {skippedShallows:N0}");
            this.output.WriteLine($"  - Valid biome pixels: {comparedPixels:N0}");
            this.output.WriteLine("");
            this.output.WriteLine($"Comparison Results:");
            this.output.WriteLine($"  - Matched: {matchedPixels:N0}");
            this.output.WriteLine($"  - Mismatched: {mismatchedPixels:N0}");
            this.output.WriteLine($"  - Match Rate: {matchPct:F4}%");
            
            // Calculate tile sample count
            int tileWidth = 1024;
            int tileSampleCount = tileWidth * tileWidth;
            this.output.WriteLine("");
            this.output.WriteLine($"Tile Data Points: {tileSampleCount:N0} (1024x1024)");
        }

        private byte[] LoadTileData(string path)
        {
            using var fileStream = File.OpenRead(path);
            using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
            using var memoryStream = new MemoryStream();
            gzipStream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        private string GetExactBiomeMatch(int r, int g, int b)
        {
            int tolerance = 3;
            if (Math.Abs(r - 0) <= tolerance && Math.Abs(g - 0) <= tolerance && Math.Abs(b - 153) <= tolerance) return "Ocean";
            if (Math.Abs(r - 102) <= tolerance && Math.Abs(g - 102) <= tolerance && Math.Abs(b - 255) <= tolerance) return "Shallows";
            if (Math.Abs(r - 145) <= tolerance && Math.Abs(g - 167) <= tolerance && Math.Abs(b - 91) <= tolerance) return "Meadows";
            if (Math.Abs(r - 52) <= tolerance && Math.Abs(g - 94) <= tolerance && Math.Abs(b - 59) <= tolerance) return "BlackForest";
            if (Math.Abs(r - 82) <= tolerance && Math.Abs(g - 82) <= tolerance && Math.Abs(b - 82) <= tolerance) return "Mistlands";
            if (Math.Abs(r - 255) <= tolerance && Math.Abs(g - 255) <= tolerance && Math.Abs(b - 255) <= tolerance) return "Mountain";
            if (Math.Abs(r - 199) <= tolerance && Math.Abs(g - 199) <= tolerance && Math.Abs(b - 49) <= tolerance) return "Plains";
            if (Math.Abs(r - 163) <= tolerance && Math.Abs(g - 113) <= tolerance && Math.Abs(b - 87) <= tolerance) return "Swamp";
            if (Math.Abs(r - 255) <= tolerance && Math.Abs(g - 0) <= tolerance && Math.Abs(b - 0) <= tolerance) return "AshLands";
            return null;
        }

        private string GetTileBiomeAt(byte[] tileData, double worldX, double worldZ)
        {
            double tileWidth = 1500.0;
            int samplesPerTile = 1024;

            if (worldX < 0 || worldX > tileWidth || worldZ < 0 || worldZ > tileWidth)
                return null;

            int sampleX = (int)((worldX / tileWidth) * samplesPerTile);
            int sampleZ = (int)((worldZ / tileWidth) * samplesPerTile);

            if (sampleX < 0 || sampleX >= samplesPerTile || sampleZ < 0 || sampleZ >= samplesPerTile)
                return null;

            int index = sampleX * samplesPerTile + sampleZ;
            int byteIndex = index * 10;

            if (byteIndex + 5 >= tileData.Length)
                return null;

            ushort biomeId = BitConverter.ToUInt16(tileData, byteIndex);
            float height = BitConverter.ToSingle(tileData, byteIndex + 2);

            if (height == -400f) return null;

            switch (biomeId)
            {
                case 1: return "Meadows";
                case 2: return "Swamp";
                case 4: return "Mountain";
                case 8: return "BlackForest";
                case 16: return "Plains";
                case 32: return "AshLands";
                case 64: return "DeepNorth";
                case 256: return "Ocean";
                case 512: return "Mistlands";
                default: return $"Unknown({biomeId})";
            }
        }
    }
}
