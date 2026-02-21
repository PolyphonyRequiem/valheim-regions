using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEditor;
using WorldZones.Regions;
using WorldZones.WorldGen;
using Debug = UnityEngine.Debug;

/// <summary>
/// Unity Editor tool that exports land_components.png, shelf_components.png,
/// and archipelago_candidates.png for a given seed.
/// Run via command line:
///   Unity.exe -projectPath ... -executeMethod LandComponentExporter.Export -seed HHcLC5acQt [-output path.png]
/// Or via menu: Tools > Export Land Component Map
/// </summary>
public static class LandComponentExporter
{
    // Non-land zones
    static readonly Color32 ColorDeep    = new Color32(20, 20, 40, 255);
    static readonly Color32 ColorShallow = new Color32(60, 60, 100, 255);
    // Land zones rendered as mid-gray in the shelf view
    static readonly Color32 ColorLandInShelfView = new Color32(100, 100, 100, 255);
    // Non-archipelago land in the archipelago view
    static readonly Color32 ColorNeutralGray = new Color32(120, 120, 120, 255);

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

        // ── 7. Label shelf components ─────────────────────────────
        var shelfOptions = new ShelfLabelingOptions();   // K=3 default
        var swShelfLabel = Stopwatch.StartNew();
        var shelfComponents = ComponentLabeler.LabelShelf(grid, labelGrid, out int[,] shelfLabelGrid, shelfOptions);
        swShelfLabel.Stop();
        Debug.Log($"Shelf labeling (K={shelfOptions.MaxShallowDistanceFromLandZones}): {swShelfLabel.ElapsedMilliseconds} ms");
        Debug.Log($"Shelf components: {shelfComponents.Count}");

        for (int i = 0; i < Math.Min(shelfComponents.Count, 10); i++)
        {
            var sc = shelfComponents[i];
            Debug.Log($"  #{i}: {sc.Zones.Count} zones, {sc.ContainedLandComponentIds.Count} land components (id={sc.Id})");
        }
        if (shelfComponents.Count > 10)
            Debug.Log($"  ... and {shelfComponents.Count - 10} more");

        // ── 8. Render shelf PNG ───────────────────────────────────
        var swShelfRender = Stopwatch.StartNew();
        byte[] shelfRgb = new byte[size * size * 3];

        for (int gy = 0; gy < size; gy++)
        {
            for (int gx = 0; gx < size; gx++)
            {
                int zx = gx + grid.MinIndex;
                int zy = gy + grid.MinIndex;
                var depth = grid[zx, zy];

                Color32 c;
                if (depth == DepthClass.Land || depth == DepthClass.Shallow)
                {
                    int label = shelfLabelGrid[gy, gx];
                    c = ComponentColor(label);
                }
                else
                {
                    c = ColorDeep;
                }

                int py = size - 1 - gy;
                int offset = (py * size + gx) * 3;
                shelfRgb[offset]     = c.r;
                shelfRgb[offset + 1] = c.g;
                shelfRgb[offset + 2] = c.b;
            }
        }

        swShelfRender.Stop();
        Debug.Log($"Shelf render: {swShelfRender.ElapsedMilliseconds} ms");

        // ── 9. Save shelf PNG ─────────────────────────────────────
        string shelfPath = outputPath.Replace("_land_components", "_shelf_components")
                                     .Replace("land_components", "shelf_components");
        if (shelfPath == outputPath)
        {
            // Fallback: insert _shelf before extension
            string ext = Path.GetExtension(outputPath);
            shelfPath = outputPath.Substring(0, outputPath.Length - ext.Length) + "_shelf" + ext;
        }

        var swShelfSave = Stopwatch.StartNew();
        PngWriter.Write(shelfPath, size, size, shelfRgb);
        swShelfSave.Stop();
        var shelfFileInfo = new FileInfo(shelfPath);
        Debug.Log($"Shelf PNG save: {swShelfSave.ElapsedMilliseconds} ms ({shelfFileInfo.Length / 1024} KB)");

        // ── 10. Detect archipelago candidates ─────────────────────
        var swArchDetect = Stopwatch.StartNew();
        var archCandidates = ArchipelagoDetector.Detect(shelfComponents, components);
        swArchDetect.Stop();
        Debug.Log($"Archipelago detection: {swArchDetect.ElapsedMilliseconds} ms");
        Debug.Log($"Archipelago candidates: {archCandidates.Count}");

        for (int i = 0; i < Math.Min(archCandidates.Count, 10); i++)
        {
            var ac = archCandidates[i];
            Debug.Log($"  #{i}: shelf={ac.ShelfComponentId}, {ac.LandComponentIds.Count} islands, {ac.TotalLandZoneCount} land zones, dominant={ac.DominantLandShare:P1}");
        }
        if (archCandidates.Count > 10)
            Debug.Log($"  ... and {archCandidates.Count - 10} more");

        // ── 11. Render archipelago PNG ────────────────────────────
        // Archipelago member islands: colored by their parent shelf (shared color per archipelago)
        // Non-archipelago land: neutral gray
        // Shallow: dark blue-gray; Deep: dark navy
        var swArchRender = Stopwatch.StartNew();
        byte[] archRgb = new byte[size * size * 3];

        // Build lookup: land component ID → archipelago candidate (if any)
        var landToArch = new Dictionary<int, ArchipelagoCandidate>();
        foreach (var ac in archCandidates)
            foreach (var landId in ac.LandComponentIds)
                landToArch[landId] = ac;

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
                    int landLabel = labelGrid[gy, gx];
                    if (landToArch.TryGetValue(landLabel, out var ac))
                    {
                        // Color by archipelago's shelf ID so all islands in same
                        // archipelago share a color
                        c = ComponentColor(ac.ShelfComponentId);
                    }
                    else
                    {
                        c = ColorNeutralGray;
                    }
                }
                else if (depth == DepthClass.Shallow)
                {
                    c = ColorShallow;
                }
                else
                {
                    c = ColorDeep;
                }

                int py = size - 1 - gy;
                int offset = (py * size + gx) * 3;
                archRgb[offset]     = c.r;
                archRgb[offset + 1] = c.g;
                archRgb[offset + 2] = c.b;
            }
        }

        swArchRender.Stop();
        Debug.Log($"Archipelago render: {swArchRender.ElapsedMilliseconds} ms");

        // ── 12. Save archipelago PNG ──────────────────────────────
        string archPath = outputPath.Replace("land_components", "archipelago_candidates");
        if (archPath == outputPath)
        {
            string ext = Path.GetExtension(outputPath);
            archPath = outputPath.Substring(0, outputPath.Length - ext.Length) + "_archipelago" + ext;
        }

        var swArchSave = Stopwatch.StartNew();
        PngWriter.Write(archPath, size, size, archRgb);
        swArchSave.Stop();
        var archFileInfo = new FileInfo(archPath);
        Debug.Log($"Archipelago PNG save: {swArchSave.ElapsedMilliseconds} ms ({archFileInfo.Length / 1024} KB)");

        Debug.Log("=== Summary ===");
        Debug.Log($"WorldGen init:     {swInit.ElapsedMilliseconds} ms");
        Debug.Log($"Classification:    {swClassify.ElapsedMilliseconds} ms");
        Debug.Log($"Land labeling:     {swLabel.ElapsedMilliseconds} ms");
        Debug.Log($"Shelf labeling:    {swShelfLabel.ElapsedMilliseconds} ms");
        Debug.Log($"Archip. detection: {swArchDetect.ElapsedMilliseconds} ms");
        Debug.Log($"Render:            {swRender.ElapsedMilliseconds + swShelfRender.ElapsedMilliseconds + swArchRender.ElapsedMilliseconds} ms");
        Debug.Log($"PNG save:          {swSave.ElapsedMilliseconds + swShelfSave.ElapsedMilliseconds + swArchSave.ElapsedMilliseconds} ms");
        long totalMs = swInit.ElapsedMilliseconds + swClassify.ElapsedMilliseconds
                     + swLabel.ElapsedMilliseconds + swShelfLabel.ElapsedMilliseconds
                     + swArchDetect.ElapsedMilliseconds
                     + swRender.ElapsedMilliseconds + swShelfRender.ElapsedMilliseconds + swArchRender.ElapsedMilliseconds
                     + swSave.ElapsedMilliseconds + swShelfSave.ElapsedMilliseconds + swArchSave.ElapsedMilliseconds;
        Debug.Log($"Total:             {totalMs} ms");
        Debug.Log($"Land output:       {outputPath}");
        Debug.Log($"Shelf output:      {shelfPath}");
        Debug.Log($"Archipelago output:{archPath}");
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
