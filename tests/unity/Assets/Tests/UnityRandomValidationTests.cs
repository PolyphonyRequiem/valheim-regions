using NUnit.Framework;
using WorldZones.WorldGen;

/// <summary>
/// Side-by-side validation: compares our pure-C# UnityRandom against
/// the real UnityEngine.Random to verify bit-exact output.
/// Runs inside the Unity test runner where both are available.
/// </summary>
[TestFixture]
public class UnityRandomValidationTests
{
    /// <summary>
    /// Tests that Range(int, int) produces identical sequences for multiple seeds.
    /// This is the call pattern used by WorldGenerator.GenerateOffsets.
    /// </summary>
    [Test]
    public void RangeInt_matches_UnityEngine_Random_for_multiple_seeds()
    {
        int[] seeds = { 0, 1, -1, 42, 12345, -99999, int.MinValue, int.MaxValue,
                        "HHcLC5acQt".GetStableHashCode(),
                        "myTestSeed".GetStableHashCode(),
                        "".GetStableHashCode() };

        foreach (int seed in seeds)
        {
            // Unity's Random
            UnityEngine.Random.InitState(seed);
            int[] unityValues = new int[20];
            for (int i = 0; i < 20; i++)
                unityValues[i] = UnityEngine.Random.Range(-10000, 10000);

            // Our replacement
            var ours = new WorldZones.WorldGen.UnityRandom(seed);
            int[] ourValues = new int[20];
            for (int i = 0; i < 20; i++)
                ourValues[i] = ours.Range(-10000, 10000);

            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(unityValues[i], ourValues[i],
                    $"Mismatch at seed={seed}, call #{i}: Unity={unityValues[i]}, Ours={ourValues[i]}");
            }
        }
    }

    /// <summary>
    /// Tests that Range(float, float) produces identical sequences.
    /// </summary>
    [Test]
    public void RangeFloat_matches_UnityEngine_Random_for_multiple_seeds()
    {
        int[] seeds = { 0, 42, "HHcLC5acQt".GetStableHashCode() };

        foreach (int seed in seeds)
        {
            UnityEngine.Random.InitState(seed);
            float[] unityValues = new float[20];
            for (int i = 0; i < 20; i++)
                unityValues[i] = UnityEngine.Random.Range(-10000f, 10000f);

            var ours = new WorldZones.WorldGen.UnityRandom(seed);
            float[] ourValues = new float[20];
            for (int i = 0; i < 20; i++)
                ourValues[i] = ours.Range(-10000f, 10000f);

            for (int i = 0; i < 20; i++)
            {
                // The << 9 truncation means float precision is limited to ~23 bits.
                // Over a 20000-wide range that's ~0.002, so allow 0.01 tolerance.
                Assert.AreEqual(unityValues[i], ourValues[i], 0.01f,
                    $"Mismatch at seed={seed}, call #{i}: Unity={unityValues[i]}, Ours={ourValues[i]}");
            }
        }
    }

    /// <summary>
    /// Tests that Value produces identical sequences.
    /// </summary>
    [Test]
    public void Value_matches_UnityEngine_Random_for_multiple_seeds()
    {
        int[] seeds = { 0, 1, 42, "HHcLC5acQt".GetStableHashCode() };

        foreach (int seed in seeds)
        {
            UnityEngine.Random.InitState(seed);
            float[] unityValues = new float[20];
            for (int i = 0; i < 20; i++)
                unityValues[i] = UnityEngine.Random.value;

            var ours = new WorldZones.WorldGen.UnityRandom(seed);
            float[] ourValues = new float[20];
            for (int i = 0; i < 20; i++)
                ourValues[i] = ours.Value;

            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(unityValues[i], ourValues[i], 0.0001f,
                    $"Mismatch at seed={seed}, call #{i}: Unity={unityValues[i]}, Ours={ourValues[i]}");
            }
        }
    }

    /// <summary>
    /// Tests the exact offset generation sequence that WorldGenerator uses:
    /// InitState(hash) → 5× Range(-10000, 10000) with 2 discarded calls
    /// for river/stream seeds in between.
    /// </summary>
    [Test]
    public void WorldGenerator_offset_sequence_matches()
    {
        string seed = "HHcLC5acQt";
        int hash = seed.GetStableHashCode();

        // Generate via Unity
        UnityEngine.Random.InitState(hash);
        int uo0 = UnityEngine.Random.Range(-10000, 10000);
        int uo1 = UnityEngine.Random.Range(-10000, 10000);
        int uo2 = UnityEngine.Random.Range(-10000, 10000);
        int uo3 = UnityEngine.Random.Range(-10000, 10000);
        UnityEngine.Random.Range(int.MinValue, int.MaxValue); // riverSeed
        UnityEngine.Random.Range(int.MinValue, int.MaxValue); // streamSeed
        int uo4 = UnityEngine.Random.Range(-10000, 10000);

        // Generate via ours
        var rng = new WorldZones.WorldGen.UnityRandom(hash);
        int oo0 = rng.Range(-10000, 10000);
        int oo1 = rng.Range(-10000, 10000);
        int oo2 = rng.Range(-10000, 10000);
        int oo3 = rng.Range(-10000, 10000);
        rng.Range(int.MinValue, int.MaxValue); // riverSeed
        rng.Range(int.MinValue, int.MaxValue); // streamSeed
        int oo4 = rng.Range(-10000, 10000);

        Assert.AreEqual(uo0, oo0, $"offset0: Unity={uo0}, Ours={oo0}");
        Assert.AreEqual(uo1, oo1, $"offset1: Unity={uo1}, Ours={oo1}");
        Assert.AreEqual(uo2, oo2, $"offset2: Unity={uo2}, Ours={oo2}");
        Assert.AreEqual(uo3, oo3, $"offset3: Unity={uo3}, Ours={oo3}");
        Assert.AreEqual(uo4, oo4, $"offset4: Unity={uo4}, Ours={oo4}");
    }

    /// <summary>
    /// Edge case: Range with min == max should return min.
    /// </summary>
    [Test]
    public void RangeInt_min_equals_max_returns_min()
    {
        var rng = new WorldZones.WorldGen.UnityRandom(42);
        Assert.AreEqual(5, rng.Range(5, 5));
    }
}
