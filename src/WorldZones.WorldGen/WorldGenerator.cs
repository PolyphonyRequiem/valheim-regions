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
        private readonly string seed;
        private readonly int seedHash;
        private readonly Random random;
        
        // Noise offsets derived from seed (match Valheim's offset0-4)
        private readonly float offset0;
        private readonly float offset1;
        private readonly float offset2;
        private readonly float offset3;
        private readonly float offset4;
        
        // Biome placement parameters
        private readonly float minMountainDistance;
        
        // Constants matching Valheim's world generation
        private const float WorldRadius = 10000f;
        private const float WorldEdgeRadius = 10500f;
        
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
            
            // Initialize RNG with seed hash (replaces Unity's Random.InitState)
            this.random = new Random(this.seedHash);
            
            // Generate noise offsets (match Valheim's UnityEngine.Random.Range(-10000, 10000))
            this.offset0 = this.random.Next(-10000, 10000);
            this.offset1 = this.random.Next(-10000, 10000);
            this.offset2 = this.random.Next(-10000, 10000);
            this.offset3 = this.random.Next(-10000, 10000);
            this.offset4 = this.random.Next(-10000, 10000);
            
            // World generation version 2 parameters (latest Valheim version)
            this.minMountainDistance = 1500f;
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
            // Beyond world edge is always ocean
            float distanceFromOrigin = MathUtils.Length(worldX, worldZ);
            if (distanceFromOrigin > WorldEdgeRadius)
            {
                return BiomeType.Ocean;
            }
            
            // TODO: Port full biome placement algorithm from Valheim
            // For now, return Meadows at origin, Ocean beyond edge
            if (distanceFromOrigin < 500f)
            {
                return BiomeType.Meadows;
            }
            
            return BiomeType.Ocean;
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
            // TODO: Implement full height generation with FastNoiseLite
            // For now, return simple height based on distance
            float distanceFromOrigin = MathUtils.Length(worldX, worldZ);
            
            if (distanceFromOrigin > WorldEdgeRadius)
            {
                // Deep ocean beyond edge
                return -2.0f;
            }
            
            // Simple height variation for now
            return 0.0f;
        }
    }
}
