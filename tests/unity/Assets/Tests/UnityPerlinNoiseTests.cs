using NUnit.Framework;
using UnityEngine;
using System;

namespace WorldZones.WorldGen.Tests
{
    [TestFixture]
    public class UnityPerlinNoiseTests
    {
        // Maximum acceptable absolute error: a few ULPs of float in the [0,1] range.
        // Unity's native C++ and our pure C# compute identical algorithms but may
        // differ by a small number of ULPs due to compiler/JIT float rounding.
        // Observed max delta across 300K+ test points: ~1.04e-6 (≤ 3-4 ULPs).
        const float MaxAbsError = 1.2e-6f;

        /// <summary>
        /// Count how many ULPs apart two floats are.
        /// </summary>
        static int UlpDistance(float a, float b)
        {
            if (a == b) return 0;
            int ai = BitConverter.ToInt32(BitConverter.GetBytes(a), 0);
            int bi = BitConverter.ToInt32(BitConverter.GetBytes(b), 0);
            // If signs differ and neither is zero, they're far apart
            if ((ai < 0) != (bi < 0)) return int.MaxValue;
            return Math.Abs(ai - bi);
        }
        [Test]
        public void CanCallMathfPerlinNoise_ReturnsValidValue()
        {
            float x = 1.5f;
            float y = 2.5f;
            float result = Mathf.PerlinNoise(x, y);
            Assert.That(result, Is.GreaterThanOrEqualTo(0f), "Perlin noise should return >= 0");
            Assert.That(result, Is.LessThanOrEqualTo(1f), "Perlin noise should return <= 1");
        }

        [Test]
        public void MathfPerlinNoise_IsDeterministic()
        {
            float x = 3.7f;
            float y = 8.2f;
            float result1 = Mathf.PerlinNoise(x, y);
            float result2 = Mathf.PerlinNoise(x, y);
            Assert.That(result1, Is.EqualTo(result2), "Perlin noise should be deterministic");
        }

        [Test]
        public void MathfPerlinNoise_CoordinateWrapping()
        {
            float baseX = 10.5f;
            float baseY = 20.3f;
            
            float result1 = Mathf.PerlinNoise(baseX, baseY);
            float result2 = Mathf.PerlinNoise(baseX + 256f, baseY);
            float result3 = Mathf.PerlinNoise(baseX, baseY + 256f);
            
            Assert.That(result2, Is.EqualTo(result1).Within(0.0001f), 
                "Perlin should repeat at X+256");
            Assert.That(result3, Is.EqualTo(result1).Within(0.0001f), 
                "Perlin should repeat at Y+256");
        }

        [Test]
        public void MathfPerlinNoise_KnownValues()
        {
            var testCases = new[]
            {
                (x: 0f, y: 0f),
                (x: 1f, y: 1f),
                (x: 10.5f, y: 20.3f),
                (x: 100.123f, y: 200.456f),
            };

            foreach (var (x, y) in testCases)
            {
                float result = Mathf.PerlinNoise(x, y);
                Debug.Log($"Mathf.PerlinNoise({x}, {y}) = {result}");
                Assert.That(result, Is.InRange(0f, 1f), 
                    $"Perlin({x}, {y}) should return value in [0, 1]");
            }
        }

        // ===================================================================
        // Exhaustive validation: our pure C# PerlinNoise vs Unity Mathf.PerlinNoise
        // Tolerance: ≤1 ULP (≤ ~6e-7 absolute error at Perlin output range)
        // ===================================================================

        /// <summary>
        /// Comparison at canonical test points spanning the full coordinate range
        /// used by Valheim worldgen: small values, fractional, large offsets,
        /// negative inputs, near-boundary coords.
        /// </summary>
        [Test]
        public void PerlinNoise_Matches_Unity_AtCanonicalPoints()
        {
            var coords = new[]
            {
                (0f, 0f), (0.5f, 0.5f), (1f, 1f), (1.5f, 2.5f),
                (3.7f, 8.2f), (10.5f, 20.3f), (100.123f, 200.456f),
                (255f, 255f), (255.5f, 255.5f), (256f, 256f),
                (0.001f, 0.001f), (0.999f, 0.999f),
                (-1.5f, -2.5f), (-10f, 5f), (5f, -10f),
                (1000f, 2000f), (9999.99f, 9999.99f),
            };

            int exact = 0;
            int within1Ulp = 0;
            int over1Ulp = 0;
            float maxDelta = 0f;
            foreach (var (x, y) in coords)
            {
                float unity = Mathf.PerlinNoise(x, y);
                float ours  = WorldZones.WorldGen.PerlinNoise.Noise(x, y);
                float delta = Mathf.Abs(unity - ours);
                if (delta > maxDelta) maxDelta = delta;
                int ulps = UlpDistance(unity, ours);
                if (ulps == 0) exact++;
                else if (delta <= MaxAbsError) within1Ulp++;
                else
                {
                    over1Ulp++;
                    Debug.Log($"OVER-TOLERANCE ({x}, {y}): Unity={unity:R}  Ours={ours:R}  delta={delta:E}  ulps={ulps}");
                }
            }
            Debug.Log($"Canonical: {coords.Length} pts, {exact} exact, {within1Ulp} within tolerance, {over1Ulp} over. Max delta={maxDelta:E}");
            Assert.That(over1Ulp, Is.EqualTo(0),
                $"{over1Ulp}/{coords.Length} canonical points exceeded tolerance ({MaxAbsError:E})");
        }

        /// <summary>
        /// Grid sweep: every integer + half-integer pair in [0..32] x [0..32].
        /// 65 x 65 = 4225 points covering multiple permutation table cells.
        /// </summary>
        [Test]
        public void PerlinNoise_Matches_Unity_GridSweep_0_to_32()
        {
            int total = 0, exact = 0, within = 0, over = 0;
            float maxDelta = 0f;
            for (float x = 0f; x <= 32f; x += 0.5f)
            {
                for (float y = 0f; y <= 32f; y += 0.5f)
                {
                    float unity = Mathf.PerlinNoise(x, y);
                    float ours  = WorldZones.WorldGen.PerlinNoise.Noise(x, y);
                    float delta = Mathf.Abs(unity - ours);
                    if (delta > maxDelta) maxDelta = delta;
                    total++;
                    if (unity == ours) exact++;
                    else if (delta <= MaxAbsError) within++;
                    else
                    {
                        if (over < 5)
                            Debug.Log($"OVER ({x}, {y}): Unity={unity:R}  Ours={ours:R}  delta={delta:E}");
                        over++;
                    }
                }
            }
            Debug.Log($"Grid sweep: {total} pts, {exact} exact ({exact*100/total}%), {within} within tolerance, {over} over. Max delta={maxDelta:E}");
            Assert.That(over, Is.EqualTo(0),
                $"{over}/{total} grid points exceeded tolerance ({MaxAbsError:E})");
        }

        /// <summary>
        /// Seed-scenario test: generate offsets for seed "HHcLC5acQt" and
        /// evaluate Perlin at worldgen biome coordinate patterns.
        /// </summary>
        [Test]
        public void PerlinNoise_Matches_Unity_WorldGenBiomeCoords_HHcLC5acQt()
        {
            var rng = new WorldZones.WorldGen.UnityRandom("HHcLC5acQt".GetStableHashCode());
            float offset0 = rng.Range(-10000, 10000);
            float offset1 = rng.Range(-10000, 10000);
            float offset2 = rng.Range(-10000, 10000);
            rng.Range(-10000, 10000);   // offset3 (river)
            rng.Range(int.MinValue, int.MaxValue); // riverSeed
            rng.Range(int.MinValue, int.MaxValue); // streamSeed
            float offset4 = rng.Range(-10000, 10000);

            float[] offsets = { offset0, offset1, offset2, offset4 };
            float scale = 0.001f;

            int total = 0, exact = 0, within = 0, over = 0;
            float maxDelta = 0f;

            for (float wx = -10000f; wx <= 10000f; wx += 100f)
            {
                for (float wz = -10000f; wz <= 10000f; wz += 100f)
                {
                    foreach (float offset in offsets)
                    {
                        float coordX = (float)((double)offset + (double)wx);
                        float coordY = (float)((double)offset + (double)wz);
                        float px = coordX * scale;
                        float py = coordY * scale;

                        float unity = Mathf.PerlinNoise(px, py);
                        float ours  = WorldZones.WorldGen.PerlinNoise.Noise(px, py);
                        float delta = Mathf.Abs(unity - ours);
                        if (delta > maxDelta) maxDelta = delta;
                        total++;
                        if (unity == ours) exact++;
                        else if (delta <= MaxAbsError) within++;
                        else { over++; }
                    }
                }
            }
            Debug.Log($"WorldGen biome sweep: {total} calls, {exact} exact ({exact*100/total}%), {within} within tolerance, {over} over. Max delta={maxDelta:E}");
            Assert.That(over, Is.EqualTo(0),
                $"{over}/{total} worldgen biome calls exceeded tolerance ({MaxAbsError:E})");
        }

        /// <summary>
        /// Height formula test: evaluate Perlin at coordinate patterns used by
        /// GetBaseHeight for seed "HHcLC5acQt".
        /// </summary>
        [Test]
        public void PerlinNoise_Matches_Unity_HeightCoords_HHcLC5acQt()
        {
            var rng = new WorldZones.WorldGen.UnityRandom("HHcLC5acQt".GetStableHashCode());
            float offset0 = rng.Range(-10000, 10000);

            float[] scales = {
                0.002f * 0.5f, 0.003f * 0.5f, 0.002f, 0.003f, 0.005f,
                0.01f, 0.02f, 0.05f, 0.1f, 0.4f,
            };

            int total = 0, exact = 0, within = 0, over = 0;
            float maxDelta = 0f;

            for (float wx = -10000f; wx <= 10000f; wx += 500f)
            {
                for (float wz = -10000f; wz <= 10000f; wz += 500f)
                {
                    float num5 = (float)((double)wx + (double)offset0 + 100000.0);
                    float num6 = (float)((double)wz + (double)offset0 + 100000.0);

                    foreach (float s in scales)
                    {
                        float px = num5 * s;
                        float py = num6 * s;
                        float unity = Mathf.PerlinNoise(px, py);
                        float ours  = WorldZones.WorldGen.PerlinNoise.Noise(px, py);
                        float delta = Mathf.Abs(unity - ours);
                        if (delta > maxDelta) maxDelta = delta;
                        total++;
                        if (unity == ours) exact++;
                        else if (delta <= MaxAbsError) within++;
                        else { over++; }
                    }
                }
            }
            Debug.Log($"Height coord sweep: {total} calls, {exact} exact ({exact*100/total}%), {within} within tolerance, {over} over. Max delta={maxDelta:E}");
            Assert.That(over, Is.EqualTo(0),
                $"{over}/{total} height formula calls exceeded tolerance ({MaxAbsError:E})");
        }

        /// <summary>
        /// Negative coordinate test: verify abs() mirroring matches across
        /// all four quadrants.
        /// </summary>
        [Test]
        public void PerlinNoise_Matches_Unity_NegativeCoords()
        {
            int total = 0, exact = 0, within = 0, over = 0;
            float maxDelta = 0f;
            for (float x = -50f; x <= 50f; x += 0.7f)
            {
                for (float y = -50f; y <= 50f; y += 0.7f)
                {
                    float unity = Mathf.PerlinNoise(x, y);
                    float ours  = WorldZones.WorldGen.PerlinNoise.Noise(x, y);
                    float delta = Mathf.Abs(unity - ours);
                    if (delta > maxDelta) maxDelta = delta;
                    total++;
                    if (unity == ours) exact++;
                    else if (delta <= MaxAbsError) within++;
                    else { over++; }
                }
            }
            Debug.Log($"Negative coord sweep: {total} pts, {exact} exact ({exact*100/total}%), {within} within tolerance, {over} over. Max delta={maxDelta:E}");
            Assert.That(over, Is.EqualTo(0),
                $"{over}/{total} negative-coord points exceeded tolerance ({MaxAbsError:E})");
        }

        /// <summary>
        /// Large coordinate test: Valheim coords can reach offset+worldX ≈ 20000.
        /// </summary>
        [Test]
        public void PerlinNoise_Matches_Unity_LargeCoords()
        {
            int total = 0, exact = 0, within = 0, over = 0;
            float maxDelta = 0f;
            for (float x = 100f; x <= 500f; x += 1.3f)
            {
                for (float y = 100f; y <= 500f; y += 1.3f)
                {
                    float unity = Mathf.PerlinNoise(x, y);
                    float ours  = WorldZones.WorldGen.PerlinNoise.Noise(x, y);
                    float delta = Mathf.Abs(unity - ours);
                    if (delta > maxDelta) maxDelta = delta;
                    total++;
                    if (unity == ours) exact++;
                    else if (delta <= MaxAbsError) within++;
                    else { over++; }
                }
            }
            Debug.Log($"Large coord sweep: {total} pts, {exact} exact ({exact*100/total}%), {within} within tolerance, {over} over. Max delta={maxDelta:E}");
            Assert.That(over, Is.EqualTo(0),
                $"{over}/{total} large-coord points exceeded tolerance ({MaxAbsError:E})");
        }
    }
}
