using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// C# implementation of UnityEngine.Random with Xorshift128 algorithm.
    /// Provides near 1-to-1 parity with Unity's Random for deterministic world generation.
    /// Based on community reverse-engineering: https://gist.github.com/macklinb/a00be6b616cbf20fa95e4227575fe50b
    /// </summary>
    public class UnityRandom
    {
        uint x, y, z, w;
        
        /// <summary>
        /// Initializes the random number generator with the given seed.
        /// Matches UnityEngine.Random.InitState behavior.
        /// </summary>
        public void InitState(int seed)
        {
            this.x = (uint)seed;
            this.y = 362436069;
            this.z = 521288629;
            this.w = 88675123;
            
            // Shuffle the state 10 times to spread the seed
            for (int i = 0; i < 10; i++)
            {
                NextUint();
            }
        }
        
        /// <summary>
        /// Returns the next random uint (internal Xorshift128 step).
        /// </summary>
        uint NextUint()
        {
            uint t = this.x ^ (this.x << 11);
            this.x = this.y;
            this.y = this.z;
            this.z = this.w;
            this.w = this.w ^ (this.w >> 19) ^ t ^ (t >> 8);
            return this.w;
        }
        
        /// <summary>
        /// Returns a random float in the range [0, 1).
        /// Matches UnityEngine.Random.value behavior.
        /// </summary>
        public float Value()
        {
            return (NextUint() & 0x00FFFFFF) / (float)0x01000000;
        }
        
        /// <summary>
        /// Returns a random double in the range [0, 1).
        /// </summary>
        public double NextDouble()
        {
            return Value();
        }
        
        /// <summary>
        /// Returns a random integer in the range [min, max).
        /// Matches UnityEngine.Random.Range(int, int) behavior.
        /// </summary>
        public int Range(int min, int max)
        {
            return min + (int)(Value() * (max - min));
        }
    }
}
