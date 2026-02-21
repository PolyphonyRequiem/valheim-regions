using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Regions.Runner
{
    class Program
    {
        static void Main(string[] args)
        {
            string seed = args.Length > 0 ? args[0] : "HHcLC5acQt";
            Console.WriteLine($"Seed: {seed}");

            // ── 1. Create WorldGenerator ──────────────────────────────
            Console.Write("Creating WorldGenerator... ");
            var worldGen = new WorldGenerator(seed);
            Console.WriteLine("done.");

            // ── 2. Classify zones ─────────────────────────────────────
            Console.Write("Classifying zones... ");
            var grid = new ZoneGrid();
            ZoneClassifier.Classify(grid, worldGen);
            Console.WriteLine("done.");

            // ── 3. Count depth classes ────────────────────────────────
            int landCount = 0, shallowCount = 0, deepCount = 0;
            foreach (var coord in grid.AllCoords())
            {
                switch (grid[coord])
                {
                    case DepthClass.Land:    landCount++;    break;
                    case DepthClass.Shallow: shallowCount++; break;
                    case DepthClass.Deep:    deepCount++;    break;
                }
            }

            int total = grid.Size * grid.Size;
            Console.WriteLine();
            Console.WriteLine("=== Zone Classification ===");
            Console.WriteLine($"  Grid size : {grid.Size} x {grid.Size} = {total:N0} zones");
            Console.WriteLine($"  Land      : {landCount:N0}  ({100.0 * landCount / total:F1}%)");
            Console.WriteLine($"  Shallow   : {shallowCount:N0}  ({100.0 * shallowCount / total:F1}%)");
            Console.WriteLine($"  Deep      : {deepCount:N0}  ({100.0 * deepCount / total:F1}%)");

            // ── 4. Label land components ──────────────────────────────
            Console.Write("Labeling land components... ");
            var components = ComponentLabeler.LabelLand(grid, out int[,] labelGrid);
            Console.WriteLine("done.");

            Console.WriteLine();
            Console.WriteLine("=== Land Components ===");
            Console.WriteLine($"  Total components: {components.Count}");
            for (int i = 0; i < Math.Min(components.Count, 10); i++)
            {
                Console.WriteLine($"    #{i}: {components[i].Zones.Count:N0} zones (id={components[i].Id})");
            }
            if (components.Count > 10)
                Console.WriteLine($"    ... and {components.Count - 10} more");

            // ── 5. Export PNG ─────────────────────────────────────────
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "artifacts");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "land_components.png");

            Console.Write($"Exporting {outPath}... ");
            ExportPng(grid, labelGrid, components.Count, outPath);
            Console.WriteLine("done.");
        }

        static void ExportPng(ZoneGrid grid, int[,] labelGrid, int componentCount, string path)
        {
            int size = grid.Size;
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                Color nonLand = Color.FromArgb(255, 40, 40, 40); // dark gray

                for (int py = 0; py < size; py++)
                {
                    for (int px = 0; px < size; px++)
                    {
                        // labelGrid is [y, x] with y=0 at MinIndex
                        int label = labelGrid[py, px];
                        Color color;
                        if (label < 0)
                        {
                            color = nonLand;
                        }
                        else
                        {
                            color = ComponentColor(label);
                        }

                        // Flip Y so north is up: bitmap row 0 = max zone y
                        bitmap.SetPixel(px, size - 1 - py, color);
                    }
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>
        /// Deterministic color from component ID using a simple hash.
        /// </summary>
        static Color ComponentColor(int id)
        {
            // Golden-ratio hue spread for good visual separation
            double hue = (id * 0.618033988749895) % 1.0;
            return HslToRgb(hue, 0.7, 0.55);
        }

        static Color HslToRgb(double h, double s, double l)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h * 6.0) % 2.0 - 1.0));
            double m = l - c / 2.0;

            double r, g, b;
            int sector = (int)(h * 6.0) % 6;
            switch (sector)
            {
                case 0:  r = c; g = x; b = 0; break;
                case 1:  r = x; g = c; b = 0; break;
                case 2:  r = 0; g = c; b = x; break;
                case 3:  r = 0; g = x; b = c; break;
                case 4:  r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }

            return Color.FromArgb(255,
                (int)((r + m) * 255),
                (int)((g + m) * 255),
                (int)((b + m) * 255));
        }
    }
}
