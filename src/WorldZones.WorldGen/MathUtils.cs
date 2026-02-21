using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Utility methods for mathematical operations used in world generation.
    /// Provides game-math helpers matching Valheim's DUtils class.
    /// </summary>
    public static class MathUtils
    {
        /// <summary>
        /// Calculates the Euclidean distance from the origin to the point (x, y).
        /// </summary>
        /// <param name="x">X coordinate.</param>
        /// <param name="y">Y coordinate.</param>
        /// <returns>Distance from origin: sqrt(x² + y²).</returns>
        public static float Length(float x, float y)
        {
            return (float)Math.Sqrt(x * x + y * y);
        }
        
        /// <summary>
        /// Linear interpolation between two values.
        /// </summary>
        /// <param name="a">Start value.</param>
        /// <param name="b">End value.</param>
        /// <param name="t">Interpolation factor (typically 0-1, but not clamped).</param>
        /// <returns>Interpolated value: a + (b - a) * t.</returns>
        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
        
        /// <summary>
        /// Inverse linear interpolation - converts a value within a range to 0-1.
        /// Similar to InverseLerp but clamped to [0, 1].
        /// </summary>
        /// <param name="min">Minimum value of the range.</param>
        /// <param name="max">Maximum value of the range.</param>
        /// <param name="value">Value to normalize.</param>
        /// <returns>Normalized value clamped to [0, 1].</returns>
        public static float LerpStep(float min, float max, float value)
        {
            if (max <= min)
            {
                return 0f;
            }
            
            float t = (value - min) / (max - min);
            return Clamp01(t);
        }
        
        /// <summary>
        /// Smooth Hermite interpolation between 0 and 1.
        /// Uses smoothstep formula: 3t² - 2t³ for smooth ease-in/ease-out.
        /// </summary>
        /// <param name="min">Minimum value of the range.</param>
        /// <param name="max">Maximum value of the range.</param>
        /// <param name="value">Value to interpolate.</param>
        /// <returns>Smoothly interpolated value between 0 and 1.</returns>
        public static float SmoothStep(float min, float max, float value)
        {
            float t = LerpStep(min, max, value);
            return t * t * (3f - 2f * t);
        }
        
        /// <summary>
        /// Clamps a value to the range [0, 1].
        /// </summary>
        /// <param name="value">Value to clamp.</param>
        /// <returns>Value clamped between 0 and 1.</returns>
        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }
            
            if (value > 1f)
            {
                return 1f;
            }
            
            return value;
        }
        
        /// <summary>
        /// Clamps a value to the range [0, 1].
        /// </summary>
        /// <param name="value">Value to clamp.</param>
        /// <returns>Value clamped between 0 and 1.</returns>
        public static double Clamp01(double value)
        {
            if (value < 0.0)
            {
                return 0.0;
            }
            
            if (value > 1.0)
            {
                return 1.0;
            }
            
            return value;
        }
        
        /// <summary>
        /// Returns the absolute value of a number.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <returns>Absolute value.</returns>
        public static float Abs(float value)
        {
            return Math.Abs(value);
        }
        
        /// <summary>
        /// Double-precision linear interpolation.
        /// </summary>
        public static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
        
        /// <summary>
        /// Double-precision inverse lerp clamped to [0,1]. Matches DUtils.LerpStep.
        /// </summary>
        public static double LerpStep(double min, double max, double value)
        {
            if (max <= min)
            {
                return 0.0;
            }
            
            double t = (value - min) / (max - min);
            return Clamp01(t);
        }
        
        /// <summary>
        /// Remaps a value from one range to another. Matches DUtils.Remap.
        /// </summary>
        public static double Remap(double value, double inMin, double inMax, double outMin, double outMax)
        {
            return outMin + (value - inMin) / (inMax - inMin) * (outMax - outMin);
        }
        
        /// <summary>
        /// Overlay blend mode. Matches DUtils.BlendOverlay.
        /// </summary>
        public static double BlendOverlay(double a, double b)
        {
            if (a < 0.5)
                return 2.0 * a * b;
            return 1.0 - 2.0 * (1.0 - a) * (1.0 - b);
        }
        
        /// <summary>
        /// Fractal Brownian Motion using Unity's PerlinNoise. Matches DUtils.Fbm(Vector2, ...).
        /// </summary>
        public static double Fbm(UnityEngine.Vector2 pos, int octaves, float lacunarity, float gain)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * (UnityEngine.Mathf.PerlinNoise(pos.x * freq, pos.y * freq) * 2f - 1f);
                freq *= lacunarity;
                amp *= gain;
            }
            return sum;
        }
        
        /// <summary>
        /// Fractal Brownian Motion using Vector3 (uses x,z). Matches DUtils.Fbm(Vector3, ...).
        /// </summary>
        public static double Fbm(UnityEngine.Vector3 pos, int octaves, float lacunarity, float gain)
        {
            return Fbm(new UnityEngine.Vector2(pos.x, pos.z), octaves, lacunarity, gain);
        }
        
        /// <summary>
        /// Double-precision Euclidean distance from origin. Matches DUtils.Length(double, double).
        /// </summary>
        public static double Length(double x, double y)
        {
            return Math.Sqrt(x * x + y * y);
        }
    }
}
