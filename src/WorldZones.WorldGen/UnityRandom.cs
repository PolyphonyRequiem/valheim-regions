namespace WorldZones.WorldGen
{
    /// <summary>
    /// Pure C# reimplementation of Unity's <c>UnityEngine.Random</c> PRNG.
    /// Uses Marsaglia's Xorshift128 (shift triplet 11, 8, 19) with
    /// MT19937-style seeding (<c>s[i] = 1812433253 * s[i-1] + 1</c>).
    /// <para>
    /// Algorithm reverse-engineered and validated by macklinb / MoatShrimp:
    /// <see href="https://gist.github.com/macklinb/a00be6b616cbf20fa95e4227575fe50b"/>
    /// </para>
    /// <para>
    /// This allows generating the same random sequences as
    /// <c>UnityEngine.Random</c> without requiring the Unity runtime,
    /// enabling offline <c>dotnet test</c> validation.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>Range(int, int)</c> returns <c>[min, max)</c> — exclusive of max.</item>
    ///   <item><c>Range(float, float)</c> returns <c>[min, max]</c> — inclusive of both.</item>
    ///   <item><c>Value</c> returns <c>[0.0, 1.0]</c> — inclusive of both.</item>
    /// </list>
    /// Thread safety: NOT thread-safe. Each thread should use its own instance.
    /// </remarks>
    public sealed class UnityRandom
    {
        private uint s0, s1, s2, s3;

        private const uint MT19937 = 1812433253u;

        /// <summary>
        /// Initializes the PRNG with the given seed, replicating
        /// <c>UnityEngine.Random.InitState(seed)</c>.
        /// </summary>
        public UnityRandom(int seed)
        {
            InitState(seed);
        }

        /// <summary>
        /// Re-seeds the generator (equivalent to <c>UnityEngine.Random.InitState</c>).
        /// Seeding uses <c>s[i] = MT19937 * s[i-1] + 1</c> (simplified
        /// Mersenne Twister initialization, without the XOR-shift mixing).
        /// </summary>
        public void InitState(int seed)
        {
            s0 = (uint)seed;
            s1 = MT19937 * s0 + 1u;
            s2 = MT19937 * s1 + 1u;
            s3 = MT19937 * s2 + 1u;
        }

        /// <summary>
        /// Advances the Xorshift128 state and returns a raw 32-bit value.
        /// Shift triplet: (a=11, b=8, c=19) from Marsaglia's Table 3.
        /// </summary>
        private uint Next()
        {
            uint t = s0;
            t ^= t << 11;
            t ^= t >> 8;
            s0 = s1;
            s1 = s2;
            s2 = s3;
            s3 = (s3 ^ (s3 >> 19)) ^ t;
            return s3;
        }

        /// <summary>
        /// Returns a random float in [0.0, 1.0] (inclusive of both endpoints).
        /// Unity truncates to 23-bit mantissa precision via a left-shift of 9.
        /// </summary>
        public float Value => (float)(Next() << 9) / 4294967295.0f;

        /// <summary>
        /// Returns a random int in [<paramref name="min"/>, <paramref name="max"/>).
        /// Matches <c>UnityEngine.Random.Range(int, int)</c>.
        /// Uses <c>long</c> arithmetic internally to avoid overflow when
        /// <c>max - min</c> exceeds <c>int.MaxValue</c>.
        /// </summary>
        public int Range(int min, int max)
        {
            long minLong = min;
            long maxLong = max;
            long range = maxLong - minLong;
            if (range == 0L) return min;
            long r = Next();

            if (max < min)
                return (int)(minLong - r % (maxLong - minLong));
            else
                return (int)(minLong + r % (maxLong - minLong));
        }

        /// <summary>
        /// Returns a random float in [<paramref name="min"/>, <paramref name="max"/>].
        /// Matches <c>UnityEngine.Random.Range(float, float)</c>.
        /// Uses the same <c>&lt;&lt; 9</c> truncation as <see cref="Value"/>.
        /// </summary>
        public float Range(float min, float max)
        {
            return (min - max) * ((float)(Next() << 9) / 4294967295.0f) + max;
        }
    }
}
