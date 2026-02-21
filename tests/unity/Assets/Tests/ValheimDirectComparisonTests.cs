using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using WorldZones.WorldGen;

/// <summary>
/// Directly compares our WorldGenerator implementation against Valheim's
/// assembly_valheim WorldGenerator using the same seed and offsets.
/// No ground truth files needed - pure apples-to-apples comparison.
/// </summary>
public class ValheimDirectComparisonTests
{
    private object valheimWorldGen;
    private Type valheimWgType;
    private MethodInfo valheimGetHeight;
    private MethodInfo valheimGetBiome;

    private WorldZones.WorldGen.WorldGenerator oursWorldGen;

    /// <summary>
    /// Initializes both Valheim's and our WorldGenerator with the same seed,
    /// extracting offsets from Valheim's instance so inputs are identical.
    /// </summary>
    private void InitializeBoth(string seed)
    {
        // --- Valheim's WorldGenerator (via reflection) ---
        Assembly valheimAssembly = Assembly.Load("assembly_valheim");
        Assembly utilsAssembly = Assembly.Load("assembly_utils");

        Type stringExtType = utilsAssembly.GetType("StringExtensionMethods");
        MethodInfo getHashMethod = stringExtType.GetMethod("GetStableHashCode",
            new Type[] { typeof(string) });
        int seedHash = (int)getHashMethod.Invoke(null, new object[] { seed });

        Type worldType = valheimAssembly.GetType("World");
        object world = Activator.CreateInstance(worldType);
        worldType.GetField("m_name").SetValue(world, "TestWorld");
        worldType.GetField("m_seedName").SetValue(world, seed);
        worldType.GetField("m_seed").SetValue(world, seedHash);
        worldType.GetField("m_worldGenVersion").SetValue(world, 2);
        worldType.GetField("m_menu").SetValue(world, false);

        valheimWgType = valheimAssembly.GetType("WorldGenerator");
        MethodInfo initMethod = valheimWgType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
        initMethod.Invoke(null, new[] { world });

        PropertyInfo instanceProp = valheimWgType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
        valheimWorldGen = instanceProp.GetValue(null);

        valheimGetHeight = valheimWgType.GetMethod("GetHeight", new Type[] { typeof(float), typeof(float) });
        valheimGetBiome = valheimWgType.GetMethod("GetBiome", new Type[] { typeof(float), typeof(float), typeof(float), typeof(bool) });

        Assert.IsNotNull(valheimGetHeight, "Valheim GetHeight(float,float) not found");
        Assert.IsNotNull(valheimGetBiome, "Valheim GetBiome(float,float,float,bool) not found");

        // Extract offsets from Valheim's instance (floats in Valheim's assembly)
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        float offset0 = (float)valheimWgType.GetField("m_offset0", flags).GetValue(valheimWorldGen);
        float offset1 = (float)valheimWgType.GetField("m_offset1", flags).GetValue(valheimWorldGen);
        float offset2 = (float)valheimWgType.GetField("m_offset2", flags).GetValue(valheimWorldGen);
        float offset3 = (float)valheimWgType.GetField("m_offset3", flags).GetValue(valheimWorldGen);
        float offset4 = (float)valheimWgType.GetField("m_offset4", flags).GetValue(valheimWorldGen);

        Debug.Log($"Seed: {seed}, Hash: {seedHash}");
        Debug.Log($"Offsets: {offset0}, {offset1}, {offset2}, {offset3}, {offset4}");

        // --- Our WorldGenerator with identical inputs ---
        oursWorldGen = new WorldZones.WorldGen.WorldGenerator(seed, offset0, offset1, offset2, offset3, offset4);

        Debug.Log("Both WorldGenerators initialized");
    }

    [Test]
    public void GetBiome_SpotCheck()
    {
        Debug.Log("=== Biome Spot Check: Ours vs Valheim ===");
        InitializeBoth("HHcLC5acQt");

        float[] coords = { -5000f, -2000f, 0f, 2000f, 5000f };
        int match = 0;
        int total = 0;

        foreach (float wx in coords)
        {
            foreach (float wz in coords)
            {
                float vHeight = (float)valheimGetHeight.Invoke(valheimWorldGen, new object[] { wx, wz });
                object vBiome = valheimGetBiome.Invoke(valheimWorldGen, new object[] { wx, wz, 0.05f, false });

                float oHeight = oursWorldGen.GetHeight(wx, wz);
                var oBiome = oursWorldGen.GetBiome(wx, wz, 0.05f, false);

                bool biomeMatch = Convert.ToInt32(vBiome) == (int)oBiome;
                if (biomeMatch) match++;
                total++;

                Debug.Log($"({wx:F0},{wz:F0}): Valheim={vBiome} h={vHeight:F4} | Ours={oBiome} h={oHeight:F4} {(biomeMatch ? "✓" : "✗")}");
            }
        }

        Debug.Log($"\nSpot check: {match}/{total} biomes match");
    }

    [Test]
    public void GetBiome_GridSweep()
    {
        Debug.Log("=== Biome Grid Sweep: Ours vs Valheim ===");
        InitializeBoth("HHcLC5acQt");

        int step = 200;
        int range = 10000;
        int biomeMatch = 0;
        int total = 0;
        var errors = new Dictionary<string, int>();

        for (float wx = -range; wx <= range; wx += step)
        {
            for (float wz = -range; wz <= range; wz += step)
            {
                object vBiome = valheimGetBiome.Invoke(valheimWorldGen, new object[] { wx, wz, 0.05f, false });
                var oBiome = oursWorldGen.GetBiome(wx, wz, 0.05f, false);

                int vInt = Convert.ToInt32(vBiome);
                int oInt = (int)oBiome;

                total++;
                if (vInt == oInt)
                {
                    biomeMatch++;
                }
                else
                {
                    string key = $"{vBiome}->{oBiome}";
                    errors.TryGetValue(key, out int count);
                    errors[key] = count + 1;
                }
            }
        }

        float accuracy = (float)biomeMatch / total * 100f;
        Debug.Log($"\n=== Results ===");
        Debug.Log($"Total samples: {total}");
        Debug.Log($"Biome match: {biomeMatch}/{total} ({accuracy:F2}%)");

        if (errors.Count > 0)
        {
            Debug.Log($"\nMismatch breakdown ({errors.Count} types):");
            foreach (var kvp in errors)
                Debug.Log($"  {kvp.Key}: {kvp.Value}");
        }
    }

    [Test]
    public void GetHeight_GridSweep()
    {
        Debug.Log("=== Height Grid Sweep: Ours vs Valheim ===");
        InitializeBoth("HHcLC5acQt");

        int step = 500;
        int range = 10000;
        int total = 0;
        float maxError = 0f;
        double sumError = 0;
        int exactMatch = 0;
        string worstCoord = "";

        for (float wx = -range; wx <= range; wx += step)
        {
            for (float wz = -range; wz <= range; wz += step)
            {
                float vHeight = (float)valheimGetHeight.Invoke(valheimWorldGen, new object[] { wx, wz });
                float oHeight = oursWorldGen.GetHeight(wx, wz);

                float err = Math.Abs(vHeight - oHeight);
                sumError += err;
                total++;

                if (err < 0.0001f) exactMatch++;
                if (err > maxError)
                {
                    maxError = err;
                    worstCoord = $"({wx:F0},{wz:F0}): v={vHeight:F6} o={oHeight:F6}";
                }
            }
        }

        Debug.Log($"\n=== Height Results ===");
        Debug.Log($"Total samples: {total}");
        Debug.Log($"Exact matches (<0.0001): {exactMatch}/{total} ({(float)exactMatch / total * 100f:F2}%)");
        Debug.Log($"Mean absolute error: {sumError / total:F6}");
        Debug.Log($"Max error: {maxError:F6}");
        Debug.Log($"Worst: {worstCoord}");
    }

    [Test]
    public void FullWorld_Validation()
    {
        Debug.Log("=== Full World Validation Test ===");
        string seed = "HHcLC5acQt";

        // --- Initialize Valheim's WorldGenerator and time it ---
        var swV = System.Diagnostics.Stopwatch.StartNew();

        Assembly valheimAssembly = Assembly.Load("assembly_valheim");
        Assembly utilsAssembly = Assembly.Load("assembly_utils");

        Type stringExtType = utilsAssembly.GetType("StringExtensionMethods");
        MethodInfo getHashMethod = stringExtType.GetMethod("GetStableHashCode",
            new Type[] { typeof(string) });
        int seedHash = (int)getHashMethod.Invoke(null, new object[] { seed });

        Type worldType = valheimAssembly.GetType("World");
        object world = Activator.CreateInstance(worldType);
        worldType.GetField("m_name").SetValue(world, "TestWorld");
        worldType.GetField("m_seedName").SetValue(world, seed);
        worldType.GetField("m_seed").SetValue(world, seedHash);
        worldType.GetField("m_worldGenVersion").SetValue(world, 2);
        worldType.GetField("m_menu").SetValue(world, false);

        var vWgType = valheimAssembly.GetType("WorldGenerator");
        MethodInfo initMethod = vWgType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
        initMethod.Invoke(null, new[] { world });

        PropertyInfo instanceProp = vWgType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
        object vWg = instanceProp.GetValue(null);

        swV.Stop();
        long valheimInitMs = swV.ElapsedMilliseconds;
        Debug.Log($"Valheim WorldGenerator init: {valheimInitMs} ms");

        // Extract offsets
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        float offset0 = (float)vWgType.GetField("m_offset0", flags).GetValue(vWg);
        float offset1 = (float)vWgType.GetField("m_offset1", flags).GetValue(vWg);
        float offset2 = (float)vWgType.GetField("m_offset2", flags).GetValue(vWg);
        float offset3 = (float)vWgType.GetField("m_offset3", flags).GetValue(vWg);
        float offset4 = (float)vWgType.GetField("m_offset4", flags).GetValue(vWg);

        // --- Initialize our WorldGenerator and time it ---
        var swO = System.Diagnostics.Stopwatch.StartNew();
        var ourWg = new WorldZones.WorldGen.WorldGenerator(seed, offset0, offset1, offset2, offset3, offset4);
        swO.Stop();
        long oursInitMs = swO.ElapsedMilliseconds;
        Debug.Log($"Our WorldGenerator init: {oursInitMs} ms");

        // --- Full world comparison ---
        MethodInfo vGetHeight = vWgType.GetMethod("GetHeight", new Type[] { typeof(float), typeof(float) });
        MethodInfo vGetBiome = vWgType.GetMethod("GetBiome", new Type[] { typeof(float), typeof(float), typeof(float), typeof(bool) });

        int range = 10050;
        int step = 1;
        long totalPoints = 0;
        long biomeMismatches = 0;
        long heightMismatches = 0; // using exact bit equality
        float maxHeightError = 0f;
        string worstHeightCoord = "";
        string worstBiomeCoord = "";

        // Biome mismatch breakdown
        var biomeErrors = new Dictionary<string, int>();

        // Pre-allocate reusable arg arrays to reduce GC pressure
        object[] heightArgs = new object[2];
        object[] biomeArgs = new object[] { 0f, 0f, 0.02f, false };

        var swCompare = System.Diagnostics.Stopwatch.StartNew();

        for (float wx = -range; wx <= range; wx += step)
        {
            for (float wz = -range; wz <= range; wz += step)
            {
                totalPoints++;

                // Height comparison
                heightArgs[0] = wx;
                heightArgs[1] = wz;
                float vH = (float)vGetHeight.Invoke(vWg, heightArgs);
                float oH = ourWg.GetHeight(wx, wz);

                if (vH != oH)
                {
                    heightMismatches++;
                    float err = Math.Abs(vH - oH);
                    if (err > maxHeightError)
                    {
                        maxHeightError = err;
                        worstHeightCoord = $"({wx},{wz}): V={vH:R} O={oH:R}";
                    }
                }

                // Biome comparison
                biomeArgs[0] = wx;
                biomeArgs[1] = wz;
                object vBiome = vGetBiome.Invoke(vWg, biomeArgs);
                var oBiome = ourWg.GetBiome(wx, wz);

                int vBInt = Convert.ToInt32(vBiome);
                int oBInt = (int)oBiome;
                if (vBInt != oBInt)
                {
                    biomeMismatches++;
                    string key = $"{vBiome}->{oBiome}";
                    biomeErrors.TryGetValue(key, out int count);
                    biomeErrors[key] = count + 1;
                    if (worstBiomeCoord == "")
                        worstBiomeCoord = $"({wx},{wz}): V={vBiome} O={oBiome}";
                }
            }

            // Progress every 1000 rows
            if (((int)wx + range) % 5000 == 0)
            {
                Debug.Log($"Progress: x={wx}, {totalPoints} points checked...");
            }
        }

        swCompare.Stop();
        long compareMs = swCompare.ElapsedMilliseconds;

        // --- Results ---
        Debug.Log($"\n========================================");
        Debug.Log($"FULL WORLD VALIDATION RESULTS");
        Debug.Log($"========================================");
        Debug.Log($"Seed: {seed}");
        Debug.Log($"Range: +/-{range}, Step: {step}");
        Debug.Log($"Total points: {totalPoints:N0}");
        Debug.Log($"");
        Debug.Log($"--- Timing ---");
        Debug.Log($"Valheim WorldGen init: {valheimInitMs} ms");
        Debug.Log($"Our WorldGen init:     {oursInitMs} ms");
        Debug.Log($"Comparison loop:       {compareMs} ms ({compareMs / 1000.0:F1}s)");
        Debug.Log($"");
        Debug.Log($"--- Biome ---");
        Debug.Log($"Matches:    {totalPoints - biomeMismatches:N0} / {totalPoints:N0}");
        Debug.Log($"Mismatches: {biomeMismatches:N0}");
        if (biomeMismatches > 0)
        {
            Debug.Log($"First mismatch: {worstBiomeCoord}");
            foreach (var kvp in biomeErrors)
                Debug.Log($"  {kvp.Key}: {kvp.Value:N0}");
        }
        Debug.Log($"");
        Debug.Log($"--- Height ---");
        Debug.Log($"Bit-exact matches: {totalPoints - heightMismatches:N0} / {totalPoints:N0}");
        Debug.Log($"Mismatches:        {heightMismatches:N0}");
        Debug.Log($"Max error:         {maxHeightError:E}");
        if (heightMismatches > 0)
            Debug.Log($"Worst: {worstHeightCoord}");
        Debug.Log($"========================================");

        Assert.AreEqual(0, biomeMismatches, $"Biome mismatches found: {biomeMismatches}");
        Assert.AreEqual(0, heightMismatches, $"Height mismatches found: {heightMismatches}");
    }
}
