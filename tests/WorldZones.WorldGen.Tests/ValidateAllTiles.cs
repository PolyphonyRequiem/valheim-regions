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
    public class ValidateAllTiles
    {
        private readonly ITestOutputHelper output;

        public ValidateAllTiles(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void ValidateInteriorTiles_WorldSize24576()
        {
            // Interior 50% = middle tiles 04-11 in both X and Z (8x8 = 64 tiles = 25% of total)
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            var tilesDir = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\data\tiles";

            int worldSize = 24576; // 3.0 m/pixel exactly

            // Get all tile files
            var tileFiles = Directory.GetFiles(tilesDir, "*.bin.gz");
            this.output.WriteLine($"Found {tileFiles.Length} tile files");
            this.output.WriteLine("Filtering to interior tiles (04-11 in both X and Z)");
            this.output.WriteLine("");

            var results = new List<(string tile, int scanned, int compared, int matched, int mismatched, double matchPct)>();

            using var png = new Bitmap(pngPath);

            foreach (var tilePath in tileFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(tilePath));
                var parts = fileName.Split('-');
                if (parts.Length != 2) continue;

                if (!int.TryParse(parts[0], out int tileX) || !int.TryParse(parts[1], out int tileZ))
                    continue;

                // Only process interior tiles
                if (tileX < 4 || tileX > 11 || tileZ < 4 || tileZ > 11)
                    continue;

                // Calculate world bounds for this tile
                // Tiles are 1500m wide, tile 08-08 covers [0,1500] so tile 00-00 is at [-12000, -10500]
                double worldXMin = (tileX - 8) * 1500.0;
                double worldXMax = (tileX - 7) * 1500.0;
                double worldZMin = (tileZ - 8) * 1500.0;
                double worldZMax = (tileZ - 7) * 1500.0;

                // Load tile data
                byte[] tileData = LoadTileData(tilePath);

                int centerPx = 4095;
                int centerPy = 4095;
                double pixelToWorld = worldSize / 8192.0;

                // Calculate PNG pixel range
                int pngXMin = centerPx + (int)(worldXMin / pixelToWorld);
                int pngXMax = centerPx + (int)(worldXMax / pixelToWorld);
                int pngYMin = centerPy - (int)(worldZMax / pixelToWorld);
                int pngYMax = centerPy - (int)(worldZMin / pixelToWorld);

                int totalPngPixels = 0;
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

                        if (pngBiome == null || pngBiome == "Shallows") continue;

                        double worldX = (px - centerPx) * pixelToWorld;
                        double worldZ = -(py - centerPy) * pixelToWorld;

                        var tileBiome = GetTileBiomeAt(tileData, worldX - worldXMin, worldZ - worldZMin);

                        if (tileBiome == null) continue;

                        if (pngBiome == tileBiome)
                            matchedPixels++;
                        else
                            mismatchedPixels++;
                    }
                }

                int comparedPixels = matchedPixels + mismatchedPixels;
                double matchPct = comparedPixels > 0 ? (matchedPixels * 100.0 / comparedPixels) : 0;

                results.Add((fileName, totalPngPixels, comparedPixels, matchedPixels, mismatchedPixels, matchPct));
            }

            // Sort by tile name
            results = results.OrderBy(r => r.tile).ToList();

            this.output.WriteLine("Interior Tile Validation Results (worldSize=24576, scale=3.0 m/pixel):");
            this.output.WriteLine("Tile     | Scanned | Compared | Matched  | Mismatch | Match %");
            this.output.WriteLine("---------|---------|----------|----------|----------|----------");

            int totalScanned = 0;
            int totalCompared = 0;
            int totalMatched = 0;
            int totalMismatched = 0;

            foreach (var result in results)
            {
                this.output.WriteLine($"{result.tile,-8} | {result.scanned,7:N0} | {result.compared,8:N0} | {result.matched,8:N0} | {result.mismatched,8:N0} | {result.matchPct,7:F2}%");
                
                totalScanned += result.scanned;
                totalCompared += result.compared;
                totalMatched += result.matched;
                totalMismatched += result.mismatched;
            }

            double overallMatchPct = totalCompared > 0 ? (totalMatched * 100.0 / totalCompared) : 0;

            this.output.WriteLine("---------|---------|----------|----------|----------|----------");
            this.output.WriteLine($"{"TOTAL",-8} | {totalScanned,7:N0} | {totalCompared,8:N0} | {totalMatched,8:N0} | {totalMismatched,8:N0} | {overallMatchPct,7:F2}%");
            this.output.WriteLine("");
            this.output.WriteLine($"Overall Match Rate: {overallMatchPct:F4}%");
            this.output.WriteLine($"Interior Tiles Validated: {results.Count} (of 64 possible)");
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

        private string GetTileBiomeAt(byte[] tileData, double tileRelativeX, double tileRelativeZ)
        {
            double tileWidth = 1500.0;
            int samplesPerTile = 1024;

            if (tileRelativeX < 0 || tileRelativeX > tileWidth || tileRelativeZ < 0 || tileRelativeZ > tileWidth)
                return null;

            int sampleX = (int)((tileRelativeX / tileWidth) * samplesPerTile);
            int sampleZ = (int)((tileRelativeZ / tileWidth) * samplesPerTile);

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
