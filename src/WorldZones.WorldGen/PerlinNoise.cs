namespace WorldZones.WorldGen
{
    /// <summary>
    /// Wrapper for Unity's Perlin noise that matches Valheim's DUtils.PerlinNoise signature.
    /// Accepts double parameters (for precision in coordinate calculations) and returns float.
    /// </summary>
    public static class PerlinNoise
    {
        /// <summary>
        /// Perlin noise function matching Valheim's DUtils.PerlinNoise signature.
        /// Accepts doubles, casts to float internally (matching Valheim's behavior exactly).
        /// </summary>
        public static float Sample(double x, double y)
        {
            return UnityEngine.Mathf.PerlinNoise((float)x, (float)y);
        }
    }
}
