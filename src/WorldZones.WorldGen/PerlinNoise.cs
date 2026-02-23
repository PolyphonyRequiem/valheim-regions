using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Pure C# reimplementation of Unity's <c>Mathf.PerlinNoise</c>.
    /// Uses Ken Perlin's "Improved Noise" (2002) with quintic fade,
    /// the standard 256-entry permutation table, and 2D gradient selection.
    /// <para>
    /// Algorithm reverse-engineered by crazicrafter1 / ricosolana for the
    /// Avledet (Valhalla) Valheim server project:
    /// <see href="https://github.com/ricosolana/Avledet/blob/0.221.10/library/src/VUtilsMath.cpp"/>
    /// </para>
    /// <para>
    /// Key behaviors matching Unity:
    /// <list type="bullet">
    ///   <item>Inputs are mirrored via <c>abs()</c> — negative coords equal positive.</item>
    ///   <item>Coordinates tile every 256 units.</item>
    ///   <item>Output is remapped from raw [-1,1] to [0,1] via <c>(raw + 0.69) / 1.483</c>.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Precision:</b> Results match <c>Mathf.PerlinNoise</c> to within 1 ULP
    /// (≤ 5.96e-8 for typical values). This residual difference is inherent to
    /// C++/MSVC/Mono float rounding behavior and has zero practical impact on
    /// worldgen output (biome thresholds, height values, river placement).
    /// </para>
    /// </summary>
    public static class PerlinNoise
    {
        // Ken Perlin's standard permutation table (256 entries), doubled to 512
        // to avoid index wrapping.
        private static readonly int[] p =
        {
            // First copy (0-255)
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7,
            225, 140, 36, 103, 30, 69, 142,
            8, 99, 37, 240, 21, 10, 23, 190, 6, 148, 247, 120, 234, 75, 0, 26,
            197, 62, 94, 252, 219, 203,
            117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125,
            136, 171, 168, 68, 175, 74, 165,
            71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60,
            211, 133, 230, 220, 105, 92,
            41, 55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80,
            73, 209, 76, 132, 187, 208,
            89, 18, 169, 200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109,
            198, 173, 186, 3, 64, 52, 217,
            226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58,
            17, 182, 189, 28, 42, 223, 183, 170, 213, 119, 248, 152, 2, 44, 154,
            163, 70, 221, 153, 101, 155,
            167, 43, 172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224,
            232, 178, 185, 112, 104, 218,
            246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162,
            241, 81, 51, 145, 235, 249, 14,
            239, 107, 49, 192, 214, 31, 181, 199, 106, 157, 184, 84, 204, 176,
            115, 121, 50, 45, 127, 4, 150,
            254, 138, 236, 205, 93, 222, 114, 67, 29, 24, 72, 243, 141, 128,
            195, 78, 66, 215, 61, 156, 180,

            // Second copy (256-511)
            151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7,
            225, 140, 36, 103, 30, 69, 142,
            8, 99, 37, 240, 21, 10, 23, 190, 6, 148, 247, 120, 234, 75, 0, 26,
            197, 62, 94, 252, 219, 203,
            117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125,
            136, 171, 168, 68, 175, 74, 165,
            71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60,
            211, 133, 230, 220, 105, 92,
            41, 55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80,
            73, 209, 76, 132, 187, 208,
            89, 18, 169, 200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109,
            198, 173, 186, 3, 64, 52, 217,
            226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
            207, 206, 59, 227, 47, 16, 58,
            17, 182, 189, 28, 42, 223, 183, 170, 213, 119, 248, 152, 2, 44, 154,
            163, 70, 221, 153, 101, 155,
            167, 43, 172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224,
            232, 178, 185, 112, 104, 218,
            246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162,
            241, 81, 51, 145, 235, 249, 14,
            239, 107, 49, 192, 214, 31, 181, 199, 106, 157, 184, 84, 204, 176,
            115, 121, 50, 45, 127, 4, 150,
            254, 138, 236, 205, 93, 222, 114, 67, 29, 24, 72, 243, 141, 128,
            195, 78, 66, 215, 61, 156, 180
        };

        /// <summary>
        /// Quintic fade curve: 6t^5 - 15t^4 + 10t^3.
        /// From Perlin's "Improving Noise" (2002).
        /// Uses double-precision constants (6.0, 15.0, 10.0) matching the native
        /// C++ implementation: t*t*t is computed in float, then promoted to double
        /// for the polynomial evaluation.
        /// </summary>
        private static double Fade(float t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        /// <summary>
        /// Standard linear interpolation (float arithmetic).
        /// Matches the C++ <c>mylerp(float, float, float)</c> function.
        /// </summary>
        private static float Lerp(float t, float a, float b)
        {
            return a + t * (b - a);
        }

        /// <summary>
        /// 2D gradient function. Uses low 4 bits of hash to select from
        /// 12 gradient directions (Perlin's standard approach).
        /// Returns float matching the native implementation.
        /// </summary>
        private static float Grad(int hash, float x, float y)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4
                ? y
                : h == 12 || h == 14 ? x : 0f;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        /// <summary>
        /// Computes 2D Perlin noise matching <c>UnityEngine.Mathf.PerlinNoise</c>.
        /// </summary>
        /// <param name="x">X coordinate (any float; tiles every 256 units).</param>
        /// <param name="y">Y coordinate (any float; tiles every 256 units).</param>
        /// <returns>Noise value in approximately [0, 1].</returns>
        public static float Noise(float x, float y)
        {
            // Unity mirrors negative inputs
            x = Math.Abs(x);
            y = Math.Abs(y);

            // Integer lattice coordinates, masked to [0,255]
            int X = (int)x & 0xFF;
            int Y = (int)y & 0xFF;

            // Fractional parts
            x -= (int)x;
            y -= (int)y;

            // Permutation lookups
            int A  = p[X] + Y;
            int B  = p[X + 1] + Y;
            int AA = p[p[A]];
            int AB = p[p[A + 1]];
            int BA = p[p[B]];
            int BB = p[p[B + 1]];

            // Fade curves (double precision, matching native C++ with double constants)
            double u = Fade(x);
            double v = Fade(y);

            // Gradient values at four corners (float precision)
            float gradAA = Grad(AA, x, y);
            float gradBA = Grad(BA, x - 1, y);
            float gradAB = Grad(AB, x, y - 1);
            float gradBB = Grad(BB, x - 1, y - 1);

            // Bilinear interpolation — cast u,v to float when passing to Lerp
            // (matches C++ implicit double→float parameter conversion)
            float res = Lerp((float)v,
                Lerp((float)u, gradAA, gradBA),
                Lerp((float)u, gradAB, gradBB));

            // Remap from [-1,1] to approximately [0,1]
            return (res + 0.69f) / 1.483f;
        }

        /// <summary>
        /// Perlin noise function matching Valheim's DUtils.PerlinNoise signature.
        /// Accepts doubles, casts to float internally (matching Valheim's behavior exactly).
        /// </summary>
        public static float Sample(double x, double y)
        {
            return Noise((float)x, (float)y);
        }
    }
}
