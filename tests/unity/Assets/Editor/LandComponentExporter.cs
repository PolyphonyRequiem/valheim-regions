using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEditor;
using WorldZones.Regions;
using WorldZones.WorldGen;
using Debug = UnityEngine.Debug;

/// <summary>
/// Unity Editor tool that exports a land-component map PNG for a given seed.
/// Run via command line:
///   Unity.exe -projectPath ... -executeMethod LandComponentExporter.Export -seed HHcLC5acQt [-output path.png]
/// Or via menu: Tools > Export Land Component Map
/// </summary>
public static class LandComponentExporter
{
    // Non-land zones
    static readonly Color32 ColorDeep    = new Color32(20, 20, 40, 255);
    static readonly Color32 ColorShallow = new Color32(60, 60, 100, 255);

    /// <summary>
    /// Command-line entry point. Called via -executeMethod LandComponentExporter.Export
    /// Accepts args: -seed &lt;seed&gt; -output &lt;path.png&gt;
    /// </summary>
    public static void Export()
    {
        string seed = "HHcLC5acQt";
        string output = null;

        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-seed" && i + 1 < args.Length)
                seed = args[i + 1];
            if (args[i] == "-output" && i + 1 < args.Length)
                output = args[i + 1];
        }

        if (output == null)
            output = Path.Combine(Directory.GetCurrentDirectory(), $"{seed}_land_components.png");

        ExportMap(seed, output);
        EditorApplication.Exit(0);
    }

    [MenuItem("Tools/Export Land Component Map")]
    public static void ExportFromMenu()
    {
        string seed = EditorInputDialog.Show("Export Land Components", "Enter world seed:", "HHcLC5acQt");
        if (string.IsNullOrEmpty(seed)) return;

        string output = EditorUtility.SaveFilePanel("Save Land Component Map", "", $"{seed}_land_components", "png");
        if (string.IsNullOrEmpty(output)) return;

        ExportMap(seed, output);
    }

    public static void ExportMap(string seed, string outputPath)
    {
        Debug.Log("=== Land Component Map Export ===");
        Debug.Log($"Seed: {seed}");

        // ── 1. Create WorldGenerator ──────────────────────────────
        var swInit = Stopwatch.StartNew();
        var worldGen = new WorldZones.WorldGen.WorldGenerator(seed);
        swInit.Stop();
        Debug.Log($"WorldGenerator init: {swInit.ElapsedMilliseconds} ms");

        // ── 2. Classify zones ─────────────────────────────────────
        var swClassify = Stopwatch.StartNew();
        var grid = new ZoneGrid();
        WorldZones.Regions.ZoneClassifier.Classify(grid, worldGen);
        swClassify.Stop();
        Debug.Log($"Zone classification: {swClassify.ElapsedMilliseconds} ms");

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
        Debug.Log($"Grid: {grid.Size}x{grid.Size} = {total} zones");
        Debug.Log($"  Land:    {landCount} ({100.0 * landCount / total:F1}%)");
        Debug.Log($"  Shallow: {shallowCount} ({100.0 * shallowCount / total:F1}%)");
        Debug.Log($"  Deep:    {deepCount} ({100.0 * deepCount / total:F1}%)");

        // ── 4. Label land components ──────────────────────────────
        var swLabel = Stopwatch.StartNew();
        var components = ComponentLabeler.LabelLand(grid, out int[,] labelGrid);
        swLabel.Stop();
        Debug.Log($"Component labeling: {swLabel.ElapsedMilliseconds} ms");
        Debug.Log($"Land components: {components.Count}");

        for (int i = 0; i < Math.Min(components.Count, 10); i++)
            Debug.Log($"  #{i}: {components[i].Zones.Count} zones (id={components[i].Id})");
        if (components.Count > 10)
            Debug.Log($"  ... and {components.Count - 10} more");

        // ── 5. Render PNG ─────────────────────────────────────────
        var swRender = Stopwatch.StartNew();
        int size = grid.Size;
        byte[] rgbData = new byte[size * size * 3];

        for (int gy = 0; gy < size; gy++)
        {
            for (int gx = 0; gx < size; gx++)
            {
                int zx = gx + grid.MinIndex;
                int zy = gy + grid.MinIndex;
                var depth = grid[zx, zy];

                Color32 c;
                if (depth == DepthClass.Land)
                {
                    int label = labelGrid[gy, gx];
                    c = ComponentColor(label);
                }
                else if (depth == DepthClass.Shallow)
                {
                    c = ColorShallow;
                }
                else
                {
                    c = ColorDeep;
                }

                // Flip Y so north is up: pixel row 0 = max zone y
                int py = size - 1 - gy;
                int offset = (py * size + gx) * 3;
                rgbData[offset]     = c.r;
                rgbData[offset + 1] = c.g;
                rgbData[offset + 2] = c.b;
            }
        }

        swRender.Stop();
        Debug.Log($"Render: {swRender.ElapsedMilliseconds} ms");

        // ── 6. Save PNG ───────────────────────────────────────────
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var swSave = Stopwatch.StartNew();
        PngWriter.Write(outputPath, size, size, rgbData);
        swSave.Stop();
        var fileInfo = new FileInfo(outputPath);
        Debug.Log($"PNG save: {swSave.ElapsedMilliseconds} ms ({fileInfo.Length / 1024} KB)");

        Debug.Log("=== Summary ===");
        Debug.Log($"WorldGen init:  {swInit.ElapsedMilliseconds} ms");
        Debug.Log($"Classification: {swClassify.ElapsedMilliseconds} ms");
        Debug.Log($"Labeling:       {swLabel.ElapsedMilliseconds} ms");
        Debug.Log($"Render:         {swRender.ElapsedMilliseconds} ms");
        Debug.Log($"PNG save:       {swSave.ElapsedMilliseconds} ms");
        long totalMs = swInit.ElapsedMilliseconds + swClassify.ElapsedMilliseconds
                     + swLabel.ElapsedMilliseconds + swRender.ElapsedMilliseconds
                     + swSave.ElapsedMilliseconds;
        Debug.Log($"Total:          {totalMs} ms");
        Debug.Log($"Output:         {outputPath}");
    }

    /// <summary>
    /// Deterministic color from component ID using golden-ratio hue spread.
    /// </summary>
    static Color32 ComponentColor(int id)
    {
        double hue = (id * 0.618033988749895) % 1.0;
        return HslToRgb(hue, 0.7, 0.55);
    }

    static Color32 HslToRgb(double h, double s, double l)
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

        return new Color32(
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255),
            255);
    }
}
