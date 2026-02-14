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
        /// Returns the absolute value of a number.
        /// </summary>
        /// <param name="value">Input value.</param>
        /// <returns>Absolute value.</returns>
        public static float Abs(float value)
        {
            return Math.Abs(value);
        }
    }
}
