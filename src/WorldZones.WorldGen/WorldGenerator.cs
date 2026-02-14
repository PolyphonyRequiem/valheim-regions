using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Generates Valheim world data deterministically from a seed string.
    /// Replicates Valheim's world generation algorithms for biome placement and heightmap generation.
    /// Thread-safe for read operations after construction.
    /// </summary>
    public class WorldGenerator
    {
        readonly string seed;
        readonly int seedHash;
        readonly double offset0;
        readonly double offset1;
        readonly double offset2;
        readonly double offset3;
        readonly double offset4;
        readonly float minMountainDistance = 2000f;
        readonly float maxMarshDistance = 6000f;
        readonly float minDarklandNoise = 0.4f;
        
        // Perlin noise generators for each offset channel
        readonly FastNoiseLite noise0;
        readonly FastNoiseLite noise1;
        readonly FastNoiseLite noise2;
        readonly FastNoiseLite noise4;
        readonly FastNoiseLite noiseBase;
        
        // Constants matching Valheim's world generation
        const float WorldRadius = 10000f;
        const float WorldEdgeRadius = 10500f;
        
        /// <summary>
        /// Gets the seed string used to generate this world.
        /// </summary>
        public string Seed => this.seed;
        
        /// <summary>
        /// Initializes a new WorldGenerator with the specified seed.
        /// </summary>
        /// <param name="seed">World seed string. Empty string is valid (produces seed hash of 0).</param>
        /// <exception cref="ArgumentNullException">Thrown if seed is null.</exception>
        public WorldGenerator(string seed)
        {
            if (seed == null)
            {
                throw new ArgumentNullException(nameof(seed));
            }
            
            this.seed = seed;
            
            // Use Valheim's GetStableHashCode from assembly_utils
            this.seedHash = string.IsNullOrEmpty(seed) ? 0 : seed.GetStableHashCode();
            
            // Generate deterministic offsets using System.Random (matches Valheim)
            Random random = new Random(this.seedHash);
            this.offset0 = random.NextDouble() * 100000.0;
            this.offset1 = random.NextDouble() * 100000.0;
            this.offset2 = random.NextDouble() * 100000.0;
            this.offset3 = random.NextDouble() * 100000.0;
            this.offset4 = random.NextDouble() * 100000.0;
            
            // Initialize noise generators (Perlin noise matching Valheim's usage)
            this.noise0 = CreateNoiseGenerator(this.seedHash + 0);
            this.noise1 = CreateNoiseGenerator(this.seedHash + 1);
            this.noise2 = CreateNoiseGenerator(this.seedHash + 2);
            this.noise4 = CreateNoiseGenerator(this.seedHash + 4);
            this.noiseBase = CreateNoiseGenerator(this.seedHash + 100);
        }
        
        FastNoiseLite CreateNoiseGenerator(int seed)
        {
            var noise = new FastNoiseLite(seed);
            noise.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
            noise.SetFractalType(FastNoiseLite.FractalType.None);
            return noise;
        }
        
        /// <summary>
        /// Gets the biome type at the specified world coordinates.
        /// Replicates Valheim's GetBiome algorithm.
        /// </summary>
        /// <param name="worldX">World X coordinate.</param>
        /// <param name="worldZ">World Z coordinate (Y in Unity).</param>
        /// <returns>The biome type at the specified coordinates.</returns>
        public BiomeType GetBiome(float worldX, float worldZ)
        {
            return GetBiome(worldX, worldZ, oceanLevel: 0.02f, waterAlwaysOcean: false);
        }
        
        public BiomeType GetBiome(float worldX, float worldZ, float oceanLevel = 0.02f, bool waterAlwaysOcean = false)
        {
            float distance = MathUtils.Length(worldX, worldZ);
            float baseHeight = GetBaseHeight(worldX, worldZ);
            float angleVariation = (float)(WorldAngle(worldX, worldZ) * 100.0);
            
            // Check waterAlwaysOcean condition
            if (waterAlwaysOcean && GetHeight(worldX, worldZ) <= oceanLevel)
            {
                return BiomeType.Ocean;
            }
            
            // Check for Ashlands (not implemented yet - requires IsAshlands)
            // if (IsAshlands(worldX, worldZ)) return BiomeType.Ashlands;
            
            // Check base ocean condition
            if (!waterAlwaysOcean && baseHeight <= oceanLevel)
            {
                return BiomeType.Ocean;
            }
            
            // Check for Deep North (not implemented yet - requires IsDeepnorth)
            // if (IsDeepnorth(worldX, worldZ))
            // {
            //     if (baseHeight > 0.4f) return BiomeType.Mountain;
            //     return BiomeType.DeepNorth;
            // }
            
            // Mountain biome (high elevation anywhere)
            if (baseHeight > 0.4f)
            {
                return BiomeType.Mountain;
            }
            
            // Swamp biome (noise-based placement with distance and height constraints)
            if (PerlinNoise(this.noise0, worldX, worldZ, this.offset0, 0.001f) > 0.6f 
                && distance > 2000f 
                && distance < this.maxMarshDistance 
                && baseHeight > 0.05f 
                && baseHeight < 0.25f)
            {
                return BiomeType.Swamp;
            }
            
            // Mistlands biome
            if (PerlinNoise(this.noise4, worldX, worldZ, this.offset4, 0.001f) > this.minDarklandNoise 
                && distance > (6000.0 + angleVariation) 
                && distance < 10000f)
            {
                return BiomeType.Mistlands;
            }
            
            // Plains biome
            if (PerlinNoise(this.noise1, worldX, worldZ, this.offset1, 0.001f) > 0.4f 
                && distance > (3000.0 + angleVariation) 
                && distance < 8000f)
            {
                return BiomeType.Plains;
            }
            
            // Black Forest biome
            if (PerlinNoise(this.noise2, worldX, worldZ, this.offset2, 0.001f) > 0.4f 
                && distance > (600.0 + angleVariation) 
                && distance < 6000f)
            {
                return BiomeType.BlackForest;
            }
            
            // Black Forest (far distance fallback)
            if (distance > (5000.0 + angleVariation))
            {
                return BiomeType.BlackForest;
            }
            
            // Default: Meadows (safe starting biome)
            return BiomeType.Meadows;
        }
        
        /// <summary>
        /// Computes a sinusoidal angle variation based on world position.
        /// Used to create radial variation in biome rings.
        /// </summary>
        static float WorldAngle(float wx, float wy)
        {
            return (float)Math.Sin((float)Math.Atan2(wx, wy) * 20.0);
        }
        
        /// <summary>
        /// Gets Perlin noise value at world coordinates with offset and scale.
        /// Matches Valheim's DUtils.PerlinNoise usage pattern.
        /// </summary>
        float PerlinNoise(FastNoiseLite noise, float wx, float wy, double offset, float scale)
        {
            double x = (offset + wx) * scale;
            double y = (offset + wy) * scale;
            
            // FastNoiseLite returns [-1, 1], normalize to [0, 1]
            return (noise.GetNoise((float)x, (float)y) + 1f) * 0.5f;
        }
        
        /// <summary>
        /// Gets the base height at the specified world coordinates.
        /// This is the foundational terrain height before biome-specific modifications.
        /// </summary>
        /// <param name="worldX">World X coordinate.</param>
        /// <param name="worldZ">World Z coordinate (Y in Unity).</param>
        /// <returns>Base height value, typically in range [-2.0, 2.0].</returns>
        public float GetBaseHeight(float worldX, float worldZ)
        {
            float distance = MathUtils.Length(worldX, worldZ);
            
            double x = worldX + 100000.0 + this.offset0;
            double y = worldZ + 100000.0 + this.offset1;
            
            // Multi-octave noise for base terrain shape
            float height = 0f;
            
            // First octave: broad features
            float n1 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0, 0.001f);
            float n2 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0, 0.0015f);
            height += n1 * n2 * 1.0f;
            
            // Second octave: medium features
            float n3 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0, 0.002f);
            float n4 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0, 0.003f);
            height += n3 * n4 * height * 0.9f;
            
            // Third octave: fine details
            float n5 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0, 0.005f);
            float n6 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0, 0.01f);
            height += n5 * n6 * 0.5f * height;
            
            // Baseline adjustment
            height -= 0.07f;
            
            // River calculation (distance-based depression)
            float river1 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0.123, 0.0005f);
            float river2 = PerlinNoise(this.noiseBase, (float)x, (float)y, 0.321, 0.0005f);
            float riverDelta = Math.Abs(river1 - river2);
            float riverFactor = 1f - MathUtils.LerpStep(0.02f, 0.12f, riverDelta);
            riverFactor *= MathUtils.SmoothStep(744f, 1000f, distance);
            height *= (1f - riverFactor);
            
            // Edge fade to deep ocean
            if (distance > 10000f)
            {
                float edgeFade = MathUtils.LerpStep(10000f, 10500f, distance);
                height = MathUtils.Lerp(height, -0.2f, edgeFade);
                
                // Deep ocean trench at very edge
                if (distance > 10490f)
                {
                    float trenchFade = MathUtils.LerpStep(10490f, 10500f, distance);
                    height = MathUtils.Lerp(height, -2f, trenchFade);
                }
            }
            
            return height;
        }
        
        /// <summary>
        /// Gets final terrain height (base height + biome-specific modifications).
        /// Stub for now - full implementation requires per-biome logic.
        /// </summary>
        public float GetHeight(float worldX, float worldZ)
        {
            // Simplified: just return base height for now
            return GetBaseHeight(worldX, worldZ);
        }
    }
}
