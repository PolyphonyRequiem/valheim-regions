using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEditor;
using Debug = UnityEngine.Debug;

/// <summary>
/// Unity Editor tool that exports a biome map PNG for a given seed.
/// Run via command line:
///   Unity.exe -projectPath ... -executeMethod BiomeMapExporter.Export -seed HHcLC5acQt [-output path.png]
/// Or via menu: Tools > Export Biome Map
/// </summary>
public static class BiomeMapExporter
{
    // Biome colors (matching reference map from valheim-map.world)
    static readonly Color32 ColorOcean       = new Color32(0, 0, 153, 255);
    static readonly Color32 ColorShallows    = new Color32(102, 102, 255, 255);
    static readonly Color32 ColorMeadows     = new Color32(145, 167, 91, 255);
    static readonly Color32 ColorMountain    = new Color32(255, 255, 255, 255);
    static readonly Color32 ColorBlackForest = new Color32(52, 94, 59, 255);
    static readonly Color32 ColorPlains      = new Color32(199, 199, 49, 255);
    static readonly Color32 ColorSwamp       = new Color32(163, 113, 87, 255);
    static readonly Color32 ColorMistlands   = new Color32(82, 82, 82, 255);
    static readonly Color32 ColorAshLands    = new Color32(255, 0, 0, 255);
    static readonly Color32 ColorDeepNorth   = new Color32(200, 200, 255, 255);

    static readonly Color32 ColorEdge = new Color32(0, 0, 0, 255);

    const int Range = 10050;
    const int Step = 5;  // 1 pixel = 5 world units → 4021x4021 (fits in 4096)
    const float WorldRadius = 10500f;
    const float WorldRadiusSq = WorldRadius * WorldRadius;
    const float WaterLevel = 30f;  // Water surface is at Y=30 in world units

    /// <summary>
    /// Command-line entry point. Called via -executeMethod BiomeMapExporter.Export
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
            output = Path.Combine(Directory.GetCurrentDirectory(), $"{seed}_biome_map.png");

        ExportMap(seed, output);
        EditorApplication.Exit(0);
    }

    [MenuItem("Tools/Export Biome Map")]
    public static void ExportFromMenu()
    {
        string seed = EditorInputDialog.Show("Export Biome Map", "Enter world seed:", "HHcLC5acQt");
        if (string.IsNullOrEmpty(seed)) return;

        string output = EditorUtility.SaveFilePanel("Save Biome Map", "", $"{seed}_biome_map", "png");
        if (string.IsNullOrEmpty(output)) return;

        ExportMap(seed, output);
    }

    public static void ExportMap(string seed, string outputPath)
    {
        int size = (Range * 2 / Step) + 1; // 10051

        Debug.Log($"=== Biome Map Export ===");
        Debug.Log($"Seed: {seed}");
        Debug.Log($"Range: +/-{Range}, Step: {Step}");
        Debug.Log($"Image size: {size}x{size}");
        Debug.Log($"Output: {outputPath}");

        // Generate offsets from seed (matching Valheim's constructor)
        int seedHash = seed.GetStableHashCode();
        var rng = new WorldZones.WorldGen.UnityRandom(seedHash);
        float offset0 = rng.Range(-10000, 10000);
        float offset1 = rng.Range(-10000, 10000);
        float offset2 = rng.Range(-10000, 10000);
        float offset3 = rng.Range(-10000, 10000);
        rng.Range(int.MinValue, int.MaxValue); // riverSeed
        rng.Range(int.MinValue, int.MaxValue); // streamSeed
        float offset4 = rng.Range(-10000, 10000);

        var swInit = Stopwatch.StartNew();
        var wg = new WorldZones.WorldGen.WorldGenerator(seed, offset0, offset1, offset2, offset3, offset4);
        swInit.Stop();
        Debug.Log($"WorldGenerator init: {swInit.ElapsedMilliseconds} ms");

        // Render to raw RGB byte array (bypass Texture2D which is capped in batch mode)
        var swRender = Stopwatch.StartNew();
        byte[] rgbData = new byte[size * size * 3];

        for (int py = 0; py < size; py++)
        {
            // World Z: top of image = +range (north), bottom = -range (south)
            float wz = Range - py * Step;

            for (int px = 0; px < size; px++)
            {
                float wx = -Range + px * Step;

                Color32 c;
                float distSq = wx * wx + wz * wz;

                if (distSq > WorldRadiusSq)
                {
                    c = ColorEdge;
                }
                else
                {
                    var biome = wg.GetBiome(wx, wz);
                    if (biome == WorldZones.WorldGen.BiomeType.Ocean)
                        c = ColorOcean;
                    else if (wg.GetBiomeHeight(biome, wx, wz) < WaterLevel)
                        c = ColorShallows;
                    else
                        c = GetBiomeColor(biome);
                }

                int offset = (py * size + px) * 3;
                rgbData[offset]     = c.r;
                rgbData[offset + 1] = c.g;
                rgbData[offset + 2] = c.b;
            }

            if (py % 1000 == 0)
            {
                Debug.Log($"  Rendering: {py * 100 / size}%...");
            }
        }

        swRender.Stop();
        Debug.Log($"Render: {swRender.ElapsedMilliseconds} ms");

        // Save as PNG (manual encoder — no Texture2D dependency)
        var swSave = Stopwatch.StartNew();
        PngWriter.Write(outputPath, size, size, rgbData);
        swSave.Stop();
        var fileInfo = new FileInfo(outputPath);
        Debug.Log($"PNG save: {swSave.ElapsedMilliseconds} ms ({fileInfo.Length / 1024 / 1024} MB)");

        Debug.Log($"=== Summary ===");
        Debug.Log($"WorldGen init: {swInit.ElapsedMilliseconds} ms");
        Debug.Log($"Render:        {swRender.ElapsedMilliseconds} ms");
        Debug.Log($"PNG save:      {swSave.ElapsedMilliseconds} ms");
        long total = swInit.ElapsedMilliseconds + swRender.ElapsedMilliseconds + swSave.ElapsedMilliseconds;
        Debug.Log($"Total:         {total} ms");
        Debug.Log($"Output:        {outputPath}");
    }

    static Color32 GetBiomeColor(WorldZones.WorldGen.BiomeType biome)
    {
        switch (biome)
        {
            case WorldZones.WorldGen.BiomeType.Meadows:     return ColorMeadows;
            case WorldZones.WorldGen.BiomeType.Swamp:       return ColorSwamp;
            case WorldZones.WorldGen.BiomeType.Mountain:    return ColorMountain;
            case WorldZones.WorldGen.BiomeType.BlackForest: return ColorBlackForest;
            case WorldZones.WorldGen.BiomeType.Plains:      return ColorPlains;
            case WorldZones.WorldGen.BiomeType.Ocean:       return ColorOcean;
            case WorldZones.WorldGen.BiomeType.Mistlands:   return ColorMistlands;
            case WorldZones.WorldGen.BiomeType.AshLands:    return ColorAshLands;
            case WorldZones.WorldGen.BiomeType.DeepNorth:   return ColorDeepNorth;
            default:                    return new Color32(255, 0, 255, 255);
        }
    }
}

/// <summary>
/// Simple input dialog for the editor menu.
/// </summary>
public class EditorInputDialog : EditorWindow
{
    string inputText;
    string message;
    string result;
    bool confirmed;
    static EditorInputDialog instance;

    public static string Show(string title, string message, string defaultValue)
    {
        // For batch mode, just return the default
        if (Application.isBatchMode)
            return defaultValue;

        instance = CreateInstance<EditorInputDialog>();
        instance.titleContent = new GUIContent(title);
        instance.message = message;
        instance.inputText = defaultValue;
        instance.minSize = new Vector2(300, 100);
        instance.maxSize = new Vector2(300, 100);
        instance.ShowModalUtility();
        return instance.confirmed ? instance.result : null;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField(message);
        inputText = EditorGUILayout.TextField(inputText);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("OK"))
        {
            result = inputText;
            confirmed = true;
            Close();
        }
        if (GUILayout.Button("Cancel"))
        {
            Close();
        }
        EditorGUILayout.EndHorizontal();
    }
}

/// <summary>
/// Minimal PNG writer for RGB24 data — no Texture2D dependency.
/// </summary>
static class PngWriter
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var bw = new BinaryWriter(fs))
        {
            // PNG signature
            bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            // IHDR
            var ihdr = new byte[13];
            WriteInt32BE(ihdr, 0, width);
            WriteInt32BE(ihdr, 4, height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 2;  // color type: RGB
            ihdr[10] = 0; // compression
            ihdr[11] = 0; // filter
            ihdr[12] = 0; // interlace
            WriteChunk(bw, "IHDR", ihdr);

            // IDAT — deflate raw scanlines with filter byte 0 (None) per row
            int rowBytes = width * 3 + 1; // +1 for filter byte
            byte[] rawData = new byte[height * rowBytes];
            for (int y = 0; y < height; y++)
            {
                rawData[y * rowBytes] = 0; // filter: None
                System.Buffer.BlockCopy(rgb, y * width * 3, rawData, y * rowBytes + 1, width * 3);
            }

            byte[] compressed;
            using (var ms = new MemoryStream())
            {
                // zlib header
                ms.WriteByte(0x78);
                ms.WriteByte(0x01);
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
                {
                    ds.Write(rawData, 0, rawData.Length);
                }
                // zlib Adler-32 checksum
                uint adler = Adler32(rawData);
                ms.WriteByte((byte)(adler >> 24));
                ms.WriteByte((byte)(adler >> 16));
                ms.WriteByte((byte)(adler >> 8));
                ms.WriteByte((byte)adler);
                compressed = ms.ToArray();
            }
            WriteChunk(bw, "IDAT", compressed);

            // IEND
            WriteChunk(bw, "IEND", new byte[0]);
        }
    }

    static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        byte[] lenBytes = new byte[4];
        WriteInt32BE(lenBytes, 0, data.Length);
        bw.Write(lenBytes);
        bw.Write(typeBytes);
        bw.Write(data);

        // CRC32 over type + data
        byte[] crcInput = new byte[4 + data.Length];
        System.Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
        System.Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
        uint crc = Crc32(crcInput);
        byte[] crcBytes = new byte[4];
        WriteInt32BE(crcBytes, 0, (int)crc);
        bw.Write(crcBytes);
    }

    static void WriteInt32BE(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    static uint[] crcTable;
    static uint Crc32(byte[] data)
    {
        if (crcTable == null)
        {
            crcTable = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                crcTable[n] = c;
            }
        }
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
            crc = crcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
