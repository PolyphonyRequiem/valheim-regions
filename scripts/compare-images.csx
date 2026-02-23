// Compare two biome map PNGs pixel by pixel
// Usage: dotnet script scripts/compare-images.csx -- <path1> <path2>
// Or: dotnet-script compare-images.csx <path1> <path2>

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var path1 = Args[0];
var path2 = Args[1];

Console.WriteLine($"Comparing:\n  {path1}\n  {path2}\n");

using var img1 = new Bitmap(path1);
using var img2 = new Bitmap(path2);

Console.WriteLine($"Image 1: {img1.Width}x{img1.Height}");
Console.WriteLine($"Image 2: {img2.Width}x{img2.Height}");

if (img1.Width != img2.Width || img1.Height != img2.Height) {
    Console.WriteLine("ERROR: Size mismatch!");
    return;
}

int w = img1.Width, h = img1.Height;
var rect = new Rectangle(0, 0, w, h);
var d1 = img1.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
var d2 = img2.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

int stride = d1.Stride;
byte[] b1 = new byte[stride * h];
byte[] b2 = new byte[stride * h];
Marshal.Copy(d1.Scan0, b1, 0, b1.Length);
Marshal.Copy(d2.Scan0, b2, 0, b2.Length);
img1.UnlockBits(d1);
img2.UnlockBits(d2);

long total = (long)w * h;
long diff = 0;
var samples = new System.Collections.Generic.List<string>();

for (int y = 0; y < h; y++) {
    int rowOff = y * stride;
    for (int x = 0; x < w; x++) {
        int off = rowOff + x * 3;
        if (b1[off] != b2[off] || b1[off+1] != b2[off+1] || b1[off+2] != b2[off+2]) {
            diff++;
            if (samples.Count < 20) {
                // BGR format in memory
                samples.Add($"  ({x},{y}): pure=({b1[off+2]},{b1[off+1]},{b1[off]}) orig=({b2[off+2]},{b2[off+1]},{b2[off]})");
            }
        }
    }
}

double pct = diff * 100.0 / total;
Console.WriteLine($"\n=== Pixel Comparison ===");
Console.WriteLine($"Total pixels:  {total:N0}");
Console.WriteLine($"Different:     {diff:N0} ({pct:F6}%)");
Console.WriteLine($"Identical:     {total - diff:N0} ({100 - pct:F6}%)");

if (samples.Count > 0) {
    Console.WriteLine($"\nFirst {samples.Count} differences:");
    foreach (var s in samples) Console.WriteLine(s);
}
