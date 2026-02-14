// Classic Perlin Noise Implementation
// 
// Based on Ken Perlin's reference implementation from "Improving Noise" (SIGGRAPH 2002)
// http://mrl.nyu.edu/~perlin/noise/
// http://mrl.nyu.edu/~perlin/paper445.pdf
//
// Adapted from Keijiro Takahashi's Unity implementation (MIT License)
// https://github.com/keijiro/PerlinNoise
// Copyright (c) 2013, 2015 Keijiro Takahashi
//
// This implementation matches Unity's Mathf.PerlinNoise behavior for compatibility
// with Valheim's world generation, which relies on Unity's native Perlin implementation.

using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Classic Perlin noise implementation matching Unity's Mathf.PerlinNoise behavior.
    /// Returns values in [0, 1] range (normalized from underlying [-1, 1] Perlin noise).
    /// </summary>
    public class PerlinNoise
    {
        readonly int[] perm;
        
        /// <summary>
        /// Creates a new Perlin noise generator with the specified seed.
        /// </summary>
        public PerlinNoise(int seed)
        {
            // Initialize permutation table with seed
            var random = new Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++)
            {
                p[i] = i;
            }
            
            // Fisher-Yates shuffle
            for (int i = 255; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int temp = p[i];
                p[i] = p[j];
                p[j] = temp;
            }
            
            // Duplicate permutation table
            this.perm = new int[512];
            for (int i = 0; i < 512; i++)
            {
                this.perm[i] = p[i & 255];
            }
        }
        
        /// <summary>
        /// Gets 2D Perlin noise value at the specified coordinates.
        /// Returns value in [0, 1] range (matches Unity's Mathf.PerlinNoise).
        /// </summary>
        public float GetNoise(float x, float y)
        {
            // Raw Perlin noise returns [-1, 1]
            float raw = RawNoise(x, y);
            
            // Normalize to [0, 1] to match Unity's behavior
            return (raw + 1f) * 0.5f;
        }
        
        /// <summary>
        /// Raw Perlin noise implementation returning [-1, 1].
        /// </summary>
        float RawNoise(float x, float y)
        {
            int X = FastFloor(x) & 0xff;
            int Y = FastFloor(y) & 0xff;
            x -= (float)Math.Floor(x);
            y -= (float)Math.Floor(y);
            float u = Fade(x);
            float v = Fade(y);
            int A = (this.perm[X] + Y) & 0xff;
            int B = (this.perm[X + 1] + Y) & 0xff;
            
            return Lerp(v,
                Lerp(u, Grad(this.perm[A], x, y), Grad(this.perm[B], x - 1, y)),
                Lerp(u, Grad(this.perm[A + 1], x, y - 1), Grad(this.perm[B + 1], x - 1, y - 1)));
        }
        
        static int FastFloor(float x)
        {
            return x >= 0 ? (int)x : (int)x - 1;
        }
        
        static float Fade(float t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }
        
        static float Lerp(float t, float a, float b)
        {
            return a + t * (b - a);
        }
        
        static float Grad(int hash, float x, float y)
        {
            return ((hash & 1) == 0 ? x : -x) + ((hash & 2) == 0 ? y : -y);
        }
    }
}
