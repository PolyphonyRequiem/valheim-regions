// Quick analysis script - run with: dotnet script AnalyzeHeightFormula.cs
// Or just read the logic and manually calculate

using System;

// We know:
// - Origin (0,0) tile data: actualHeight = -3.2m
// - Our GetBaseHeight(0,0) = 0.4767 (WRONG - should be negative)
// - Ocean threshold: normalizedHeight <= 0.02

// Question: What's the relationship between actual height (meters) and normalized height?

// From GetBiome, the height thresholds are:
// - Ocean: <= 0.02
// - Mountain: > 0.4
// - Everything else: 0.02 to 0.4

// Hypothesis: Maybe our octave blending is wrong?
// Let's trace through what GetBaseHeight SHOULD do

Console.WriteLine("=== ANALYZING GetBaseHeight FORMULA ===\n");

Console.WriteLine("Known facts:");
Console.WriteLine("1. Origin (0,0) actual height: -3.2m");
Console.WriteLine("2. Origin should be Ocean biome");
Console.WriteLine("3. Ocean needs normalized height <= 0.02");
Console.WriteLine("4. Our GetBaseHeight(0,0) returns: 0.4767");
Console.WriteLine("5. This triggers Mountain biome (> 0.4) - WRONG\n");

Console.WriteLine("Possible causes:");
Console.WriteLine("A. Perlin noise implementation is wrong");
Console.WriteLine("   - BUT our tests show it returns correct [0,1] range");
Console.WriteLine("   - Distribution looks reasonable");
Console.WriteLine("   - Based on proven Keijiro Takahashi implementation");
Console.WriteLine("   - UNLIKELY\n");

Console.WriteLine("B. Coordinate offsetting is wrong (lines 221-222)");
Console.WriteLine("   - x = worldX + 100000.0 + offset0");
Console.WriteLine("   - y = worldZ + 100000.0 + offset1");
Console.WriteLine("   - offset0 = 47200.8764, offset1 = 65826.1358");
Console.WriteLine("   - These offsets come from UnityRandom seed initialization");
Console.WriteLine("   - NEED TO VERIFY against Valheim source\n");

Console.WriteLine("C. Octave blending formula is wrong (lines 228-243)");
Console.WriteLine("   - Multi-octave noise with specific frequencies");
Console.WriteLine("   - Complex interactions between octaves");
Console.WriteLine("   - MOST LIKELY - need to compare with Valheim source\n");

Console.WriteLine("D. Missing normalization/scaling step");
Console.WriteLine("   - Maybe heights need different scaling?");
Console.WriteLine("   - Baseline adjustment (-0.07) might be wrong");
Console.WriteLine("   - POSSIBLE\n");

Console.WriteLine("RECOMMENDATION:");
Console.WriteLine("1. Get the actual Valheim WorldGenerator.GetBaseHeight decompiled code");
Console.WriteLine("2. Compare line-by-line with our implementation");
Console.WriteLine("3. Look for differences in:");
Console.WriteLine("   - Noise frequencies (0.002, 0.003, 0.005, 0.01)");
Console.WriteLine("   - Octave weights (1.0, 0.9, 0.5)");
Console.WriteLine("   - Baseline adjustment (-0.07)");
Console.WriteLine("   - Offset calculations (100000.0 + offsetN)");
Console.WriteLine("\nWithout Valheim source code, we're guessing in the dark.");
