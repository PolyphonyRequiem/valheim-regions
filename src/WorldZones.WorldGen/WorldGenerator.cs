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
        
        // Perlin noise generators for each offset channel (matches Unity's Mathf.PerlinNoise)
        readonly PerlinNoise noise0;
        readonly PerlinNoise noise1;
        readonly PerlinNoise noise2;
        readonly PerlinNoise noise4;
        readonly PerlinNoise noiseBase;
        
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
            
            // Generate deterministic offsets using Unity's Xorshift128 (matches Valheim's actual implementation)
            var unityRandom = new UnityRandom();
            unityRandom.InitState(this.seedHash);
            this.offset0 = unityRandom.NextDouble() * 100000.0;
            this.offset1 = unityRandom.NextDouble() * 100000.0;
            this.offset2 = unityRandom.NextDouble() * 100000.0;
            this.offset3 = unityRandom.NextDouble() * 100000.0;
            this.offset4 = unityRandom.NextDouble() * 100000.0;
            
            // Initialize noise generators (Perlin noise matching Unity's Mathf.PerlinNoise)
            this.noise0 = new PerlinNoise(this.seedHash + 0);
            this.noise1 = new PerlinNoise(this.seedHash + 1);
            this.noise2 = new PerlinNoise(this.seedHash + 2);
            this.noise4 = new PerlinNoise(this.seedHash + 4);
            this.noiseBase = new PerlinNoise(this.seedHash + 100);
        }
        
        /// <summary>
        /// Constructor for testing with explicit offsets (to test different Random implementations).
        /// </summary>
        public WorldGenerator(string seed, double offset0, double offset1, double offset2, double offset3, double offset4, double offsetBase)
        {
            if (seed == null)
            {
                throw new ArgumentNullException(nameof(seed));
            }
            
            this.seed = seed;
            this.seedHash = string.IsNullOrEmpty(seed) ? 0 : seed.GetStableHashCode();
            
            this.offset0 = offset0;
            this.offset1 = offset1;
            this.offset2 = offset2;
            this.offset3 = offset3;
            this.offset4 = offset4;
            
            // Initialize noise generators
            this.noise0 = new PerlinNoise(this.seedHash + 0);
            this.noise1 = new PerlinNoise(this.seedHash + 1);
            this.noise2 = new PerlinNoise(this.seedHash + 2);
            this.noise4 = new PerlinNoise(this.seedHash + 4);
            this.noiseBase = new PerlinNoise(this.seedHash + 100);
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
            
            // Helper to get noise [0, 1] - PerlinNoise already returns this range
            float GetNoise(PerlinNoise noise, float nx, float ny) => noise.GetNoise(nx, ny);
            
            // Check waterAlwaysOcean condition
            if (waterAlwaysOcean && GetHeight(worldX, worldZ) <= oceanLevel)
            {
                return BiomeType.Ocean;
            }
            
            // Check for Ashlands (not implemented yet - requires IsAshlands)
            // if (IsAshlands(worldX, worldZ)) return BiomeType.AshLands;
            
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
            double swampX = (this.offset0 + worldX) * 0.001;
            double swampZ = (this.offset0 + worldZ) * 0.001;
            if (GetNoise(this.noise0, (float)swampX, (float)swampZ) > 0.6f 
                && distance > 2000f 
                && distance < this.maxMarshDistance 
                && baseHeight > 0.05f 
                && baseHeight < 0.25f)
            {
                return BiomeType.Swamp;
            }
            
            // Mistlands biome
            double mistX = (this.offset4 + worldX) * 0.001;
            double mistZ = (this.offset4 + worldZ) * 0.001;
            if (GetNoise(this.noise4, (float)mistX, (float)mistZ) > this.minDarklandNoise 
                && distance > (6000.0 + angleVariation) 
                && distance < 10000f)
            {
                return BiomeType.Mistlands;
            }
            
            // Plains biome
            double plainsX = (this.offset1 + worldX) * 0.001;
            double plainsZ = (this.offset1 + worldZ) * 0.001;
            if (GetNoise(this.noise1, (float)plainsX, (float)plainsZ) > 0.4f 
                && distance > (3000.0 + angleVariation) 
                && distance < 8000f)
            {
                return BiomeType.Plains;
            }
            
            // Black Forest biome
            double forestX = (this.offset2 + worldX) * 0.001;
            double forestZ = (this.offset2 + worldZ) * 0.001;
            if (GetNoise(this.noise2, (float)forestX, (float)forestZ) > 0.4f 
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
            
            // Helper - PerlinNoise already returns [0, 1]
            float Noise(float nx, float ny) => this.noiseBase.GetNoise(nx, ny);
            
            // Multi-octave noise for base terrain shape
            float height = 0f;
            
            // First octave: broad features
            float n1 = Noise((float)(x * 0.002 * 0.5), (float)(y * 0.002 * 0.5));
            float n2 = Noise((float)(x * 0.003 * 0.5), (float)(y * 0.003 * 0.5));
            height += n1 * n2 * 1.0f;
            
            // Second octave: medium features (amplifies existing height)
            float n3 = Noise((float)(x * 0.002), (float)(y * 0.002));
            float n4 = Noise((float)(x * 0.003), (float)(y * 0.003));
            height += n3 * n4 * height * 0.9f;
            
            // Third octave: fine details
            float n5 = Noise((float)(x * 0.005), (float)(y * 0.005));
            float n6 = Noise((float)(x * 0.01), (float)(y * 0.01));
            height += n5 * n6 * 0.5f * height;
            
            // Baseline adjustment
            height -= 0.07f;
            
            // River calculation - carves river valleys where two noise channels align
            float river1 = Noise((float)(x * 0.002 * 0.25 + 0.123), (float)(y * 0.002 * 0.25 + 0.15123));
            float river2 = Noise((float)(x * 0.002 * 0.25 + 0.321), (float)(y * 0.002 * 0.25 + 0.231));
            float riverDelta = Math.Abs(river1 - river2);
            float riverIntensity = 1f - MathUtils.LerpStep(0.02f, 0.12f, riverDelta);
            float riverDistanceFade = MathUtils.SmoothStep(744f, 1000f, distance);
            float riverFactor = riverIntensity * riverDistanceFade;
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
