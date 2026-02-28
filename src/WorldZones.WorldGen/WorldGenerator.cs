using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Generates Valheim world data deterministically from a seed string.
    /// Replicates Valheim's world generation algorithms for biome placement and heightmap generation.
    /// Uses pure C# UnityRandom (no Unity runtime needed for RNG).
    /// Thread-safe for read operations after construction.
    /// </summary>
    public class WorldGenerator
    {
        readonly string seed;
        readonly int seedHash;
        readonly float offset0;
        readonly float offset1;
        readonly float offset2;
        readonly float offset3;
        readonly float offset4;
        readonly int riverSeed;
        readonly int streamSeed;
        readonly float minMountainDistance = 1000f;
        readonly float maxMarshDistance = 6000f;
        readonly float minDarklandNoise = 0.4f;
        
        // River generation data
        private System.Collections.Generic.List<Vector2> lakes;
        private System.Collections.Generic.List<River> rivers = new System.Collections.Generic.List<River>();
        private System.Collections.Generic.List<River> streams = new System.Collections.Generic.List<River>();
        private System.Collections.Generic.Dictionary<Vector2i, RiverPoint[]> riverPoints = new System.Collections.Generic.Dictionary<Vector2i, RiverPoint[]>();
        
        // Cellular noise generator (matching Valheim's m_noiseGen)
        private static FastNoise noiseGen;
        
        // Constants matching Valheim's world generation
        const float WorldRadius = 10000f;
        const float WorldEdgeRadius = 10500f;
        
        // DLC biome constants
        static readonly float ashlandsMinDistance = 12000f;
        static readonly float ashlandsYOffset = -4000f;
        
        /// <summary>
        /// Gets the seed string used to generate this world.
        /// </summary>
        public string Seed => this.seed;
        
        /// <summary>
        /// Initializes a new WorldGenerator from a seed string.
        /// Generates random offsets using pure C# UnityRandom.
        /// Replicates Valheim's constructor behavior: InitState(seedHash), then
        /// five Random.Range(-10000, 10000) calls for offsets 0-4.
        /// </summary>
        /// <param name="seed">World seed string. Empty string is valid (produces seed hash of 0).</param>
        /// <exception cref="ArgumentNullException">Thrown if seed is null.</exception>
        public WorldGenerator(string seed)
            : this(seed, GenerateOffsets(seed ?? throw new ArgumentNullException(nameof(seed))))
        {
        }

        /// <summary>Private chaining constructor that unpacks the offset tuple.</summary>
        private WorldGenerator(string seed, (float o0, float o1, float o2, float o3, float o4) offsets)
            : this(seed, offsets.o0, offsets.o1, offsets.o2, offsets.o3, offsets.o4)
        {
        }

        /// <summary>
        /// Generates the five random offsets from a seed string, exactly matching
        /// the sequence Valheim uses in its WorldGenerator constructor.
        /// Uses pure C# UnityRandom — no Unity runtime required.
        /// </summary>
        private static (float, float, float, float, float) GenerateOffsets(string seed)
        {
            int hash = string.IsNullOrEmpty(seed) ? 0 : seed.GetStableHashCode();
            var rng = new UnityRandom(hash);
            float o0 = rng.Range(-10000, 10000);
            float o1 = rng.Range(-10000, 10000);
            float o2 = rng.Range(-10000, 10000);
            float o3 = rng.Range(-10000, 10000);
            // River/stream seeds are consumed at positions 5-6 by the 6-param ctor
            rng.Range(int.MinValue, int.MaxValue); // riverSeed slot
            rng.Range(int.MinValue, int.MaxValue); // streamSeed slot
            float o4 = rng.Range(-10000, 10000);
            return (o0, o1, o2, o3, o4);
        }

        /// <summary>
        /// Initializes a new WorldGenerator with the specified seed and pre-computed random offsets.
        /// Use <see cref="WorldGenerator(string)"/> unless you have pre-extracted offsets.
        /// </summary>
        /// <param name="seed">World seed string. Empty string is valid (produces seed hash of 0).</param>
        /// <param name="offset0">Random offset 0 (biome noise).</param>
        /// <param name="offset1">Random offset 1 (biome noise).</param>
        /// <param name="offset2">Random offset 2 (biome noise).</param>
        /// <param name="offset3">Random offset 3 (river generation).</param>
        /// <param name="offset4">Random offset 4 (mistlands noise).</param>
        /// <exception cref="ArgumentNullException">Thrown if seed is null.</exception>
        public WorldGenerator(string seed, float offset0, float offset1, float offset2, float offset3, float offset4)
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
            
            // Initialize FastNoise for cellular noise (matching Valheim constructor lines 178-186)
            if (noiseGen == null)
            {
                noiseGen = new FastNoise(this.seedHash);
                noiseGen.SetNoiseType(FastNoise.NoiseType.Cellular);
                noiseGen.SetCellularDistanceFunction(FastNoise.CellularDistanceFunction.Euclidean);
                noiseGen.SetCellularReturnType(FastNoise.CellularReturnType.Distance);
                noiseGen.SetFractalOctaves(2);
            }
            noiseGen.SetSeed(0);
            
            // Initialize river seeds
            // Valheim consumes 4 Random values for offsets before pulling river seeds,
            // then one more after river seeds for offset4 (lines 187-193).
            // Since we take offsets as params, we must advance UnityRandom state to match.
            var rng = new UnityRandom(this.seedHash);
            rng.Range(-10000, 10000); // offset0
            rng.Range(-10000, 10000); // offset1
            rng.Range(-10000, 10000); // offset2
            rng.Range(-10000, 10000); // offset3
            this.riverSeed = rng.Range(int.MinValue, int.MaxValue);
            this.streamSeed = rng.Range(int.MinValue, int.MaxValue);
            // offset4 consumed here in Valheim but we already have it
            
            // Pregenerate rivers and streams
            Pregenerate();
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
            
            // Check Ashlands (DLC biome)
            if (IsAshlands(worldX, worldZ))
            {
                return BiomeType.AshLands;
            }
            
            // Check base ocean condition
            if (!waterAlwaysOcean && baseHeight <= oceanLevel)
            {
                return BiomeType.Ocean;
            }
            
            // Check Deep North (DLC biome)
            if (IsDeepnorth(worldX, worldZ))
            {
                if (baseHeight > 0.4f)
                {
                    return BiomeType.Mountain;
                }
                return BiomeType.DeepNorth;
            }
            
            // Mountain biome (high elevation anywhere)
            if (baseHeight > 0.4f)
            {
                return BiomeType.Mountain;
            }
            
            // Swamp biome (noise-based placement with distance and height constraints)
            if (PerlinNoise.Sample((double)(float)((double)this.offset0 + (double)worldX) * 0.0010000000474974513, (double)(float)((double)this.offset0 + (double)worldZ) * 0.0010000000474974513) > 0.6f
                && distance > 2000f 
                && distance < this.maxMarshDistance 
                && baseHeight > 0.05f 
                && baseHeight < 0.25f)
            {
                return BiomeType.Swamp;
            }
            
            // Mistlands biome
            if (PerlinNoise.Sample((double)(float)((double)this.offset4 + (double)worldX) * 0.0010000000474974513, (double)(float)((double)this.offset4 + (double)worldZ) * 0.0010000000474974513) > this.minDarklandNoise
                && distance > (6000.0 + angleVariation) 
                && distance < 10000f)
            {
                return BiomeType.Mistlands;
            }
            
            // Plains biome
            if (PerlinNoise.Sample((double)(float)((double)this.offset1 + (double)worldX) * 0.0010000000474974513, (double)(float)((double)this.offset1 + (double)worldZ) * 0.0010000000474974513) > 0.4f
                && distance > (3000.0 + angleVariation) 
                && distance < 8000f)
            {
                return BiomeType.Plains;
            }
            
            // Black Forest biome
            if (PerlinNoise.Sample((double)(float)((double)this.offset2 + (double)worldX) * 0.0010000000474974513, (double)(float)((double)this.offset2 + (double)worldZ) * 0.0010000000474974513) > 0.4f
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
            return (float)Math.Sin((float)((double)(float)Math.Atan2(wx, wy) * 20.0));
        }
        
        /// <summary>
        /// Checks if coordinates are in the Ashlands biome region.
        /// </summary>
        static bool IsAshlands(float x, float y)
        {
            double angleVariation = WorldAngle(x, y) * 100.0;
            return MathUtils.Length(x, y + ashlandsYOffset) > (ashlandsMinDistance + angleVariation);
        }
        
        /// <summary>
        /// Checks if coordinates are in the Deep North biome region.
        /// </summary>
        static bool IsDeepnorth(float x, float y)
        {
            float angleVariation = (float)(WorldAngle(x, y) * 100.0);
            return MathUtils.Length(x, y + 4000f) > (12000f + angleVariation);
        }
        
        /// <summary>
        /// Gets the base height at the specified world coordinates.
        /// Uses multi-octave Perlin noise for terrain generation.
        /// Matches Valheim's implementation (WorldGenerator.cs line 794-827)
        /// </summary>
        public float GetBaseHeight(float worldX, float worldZ)
        {
            // Calculate distance from world center (line 794)
            float distance = MathUtils.Length(worldX, worldZ);
            
            // Apply coordinate offset like Valheim (line 795-798)
            double num5 = worldX;
            double num6 = worldZ;
            num5 += 100000.0 + (double)this.offset0;
            num6 += 100000.0 + (double)this.offset1;
            
            float num7 = 0f;
            
            // Octave 1: broad features (line 800)
            num7 = (float)((double)num7 + (double)PerlinNoise.Sample(num5 * 0.0020000000949949026 * 0.5, num6 * 0.0020000000949949026 * 0.5) * (double)PerlinNoise.Sample(num5 * 0.003000000026077032 * 0.5, num6 * 0.003000000026077032 * 0.5) * 1.0);
            
            // Octave 2: medium features - amplifies existing height (line 801)
            num7 = (float)((double)num7 + (double)PerlinNoise.Sample(num5 * 0.0020000000949949026 * 1.0, num6 * 0.0020000000949949026 * 1.0) * (double)PerlinNoise.Sample(num5 * 0.003000000026077032 * 1.0, num6 * 0.003000000026077032 * 1.0) * (double)num7 * 0.8999999761581421);
            
            // Octave 3: fine details (line 802)
            num7 = (float)((double)num7 + (double)PerlinNoise.Sample(num5 * 0.004999999888241291 * 1.0, num6 * 0.004999999888241291 * 1.0) * (double)PerlinNoise.Sample(num5 * 0.009999999776482582 * 1.0, num6 * 0.009999999776482582 * 1.0) * 0.5 * (double)num7);
            
            // Baseline adjustment (line 803)
            num7 = (float)((double)num7 - 0.07000000029802322);
            
            // River calculation (line 804-809)
            float num8 = PerlinNoise.Sample(num5 * 0.0020000000949949026 * 0.25 + 0.12300000339746475, num6 * 0.0020000000949949026 * 0.25 + 0.15123000741004944);
            float num9 = PerlinNoise.Sample(num5 * 0.0020000000949949026 * 0.25 + 0.32100000977516174, num6 * 0.0020000000949949026 * 0.25 + 0.23100000619888306);
            float v = Math.Abs((float)((double)num8 - (double)num9));
            float num10 = (float)(1.0 - (double)MathUtils.LerpStep(0.02f, 0.12f, v));
            num10 = (float)((double)num10 * (double)MathUtils.SmoothStep(744f, 1000f, distance));
            num7 = (float)((double)num7 * (1.0 - (double)num10));
            
            // World edge handling (line 810-820)
            if (distance > 10000f)
            {
                float t = MathUtils.LerpStep(10000f, 10500f, distance);
                num7 = MathUtils.Lerp(num7, -0.2f, t);
                
                float num11 = 10490f;
                if (distance > num11)
                {
                    float t2 = MathUtils.LerpStep(num11, 10500f, distance);
                    num7 = MathUtils.Lerp(num7, -2f, t2);
                }
                
                return num7;
            }
            
            // Mountain suppression near center (line 822-826)
            if (distance < this.minMountainDistance && num7 > 0.28f)
            {
                float t3 = (float)MathUtils.Clamp01(((double)num7 - 0.2800000011920929) / 0.09999999403953552);
                num7 = MathUtils.Lerp(MathUtils.Lerp(0.28f, 0.38f, t3), num7, MathUtils.LerpStep((float)((double)this.minMountainDistance - 400.0), (float)this.minMountainDistance, distance));
            }
            
            return num7;
        }
        
        /// <summary>
        /// Gets the final height at the specified world coordinates.
        /// Matches Valheim's GetHeight: determines biome, then computes biome-specific height.
        /// </summary>
        public float GetHeight(float worldX, float worldZ)
        {
            BiomeType biome = GetBiome(worldX, worldZ);
            return GetBiomeHeight(biome, worldX, worldZ);
        }

        // Calculate terrain tilt (used by mountain biome)
        private float BaseHeightTilt(float wx, float wy)
        {
            float baseHeight = GetBaseHeight((float)((double)wx - 1.0), wy);
            float baseHeight2 = GetBaseHeight((float)((double)wx + 1.0), wy);
            float baseHeight3 = GetBaseHeight(wx, (float)((double)wy - 1.0));
            float baseHeight4 = GetBaseHeight(wx, (float)((double)wy + 1.0));
            return (float)((double)Math.Abs((float)((double)baseHeight2 - (double)baseHeight)) + (double)Math.Abs((float)((double)baseHeight3 - (double)baseHeight4)));
        }

        private float GetOceanHeight(float wx, float wy)
        {
            return GetBaseHeight(wx, wy);
        }

        private float GetMeadowsHeight(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num = wx;
            double num2 = wy;
            float num3 = (float)((double)PerlinNoise.Sample(num * 0.009999999776482582, num2 * 0.009999999776482582) * (double)PerlinNoise.Sample(num * 0.019999999552965164, num2 * 0.019999999552965164));
            num3 = (float)((double)num3 + (double)PerlinNoise.Sample(num * 0.05000000074505806, num2 * 0.05000000074505806) * (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * (double)num3 * 0.5);
            float num4 = baseHeight;
            num4 = (float)((double)num4 + (double)num3 * 0.10000000149011612);
            float num5 = 0.15f;
            float num6 = (float)((double)num4 - (double)num5);
            float num7 = (float)MathUtils.Clamp01((double)baseHeight / 0.4000000059604645);
            if (num6 > 0f)
            {
                num4 = (float)((double)num4 - (double)num6 * ((1.0 - (double)num7) * 0.75));
            }
            num4 = AddRivers(wx2, wy2, num4);
            num4 = (float)((double)num4 + (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * 0.009999999776482582);
            return (float)((double)num4 + (double)PerlinNoise.Sample(num * 0.4000000059604645, num2 * 0.4000000059604645) * 0.003000000026077032);
        }

        private float GetForestHeight(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num = wx;
            double num2 = wy;
            float num3 = (float)((double)PerlinNoise.Sample(num * 0.009999999776482582, num2 * 0.009999999776482582) * (double)PerlinNoise.Sample(num * 0.019999999552965164, num2 * 0.019999999552965164));
            num3 = (float)((double)num3 + (double)PerlinNoise.Sample(num * 0.05000000074505806, num2 * 0.05000000074505806) * (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * (double)num3 * 0.5);
            baseHeight = (float)((double)baseHeight + (double)num3 * 0.10000000149011612);
            baseHeight = AddRivers(wx2, wy2, baseHeight);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * 0.009999999776482582);
            return (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.4000000059604645, num2 * 0.4000000059604645) * 0.003000000026077032);
        }

        private float GetPlainsHeight(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num = wx;
            double num2 = wy;
            float num3 = (float)((double)PerlinNoise.Sample(num * 0.009999999776482582, num2 * 0.009999999776482582) * (double)PerlinNoise.Sample(num * 0.019999999552965164, num2 * 0.019999999552965164));
            num3 = (float)((double)num3 + (double)PerlinNoise.Sample(num * 0.05000000074505806, num2 * 0.05000000074505806) * (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * (double)num3 * 0.5);
            float num4 = baseHeight;
            num4 = (float)((double)num4 + (double)num3 * 0.10000000149011612);
            float num5 = 0.15f;
            float num6 = num4 - num5;
            float num7 = (float)MathUtils.Clamp01((double)baseHeight / 0.4000000059604645);
            if (num6 > 0f)
            {
                num4 = (float)((double)num4 - (double)num6 * (1.0 - (double)num7) * 0.75);
            }
            num4 = AddRivers(wx2, wy2, num4);
            num4 = (float)((double)num4 + (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * 0.009999999776482582);
            return (float)((double)num4 + (double)PerlinNoise.Sample(num * 0.4000000059604645, num2 * 0.4000000059604645) * 0.003000000026077032);
        }

        private float GetSnowMountainHeight(float wx, float wy, bool menu)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            float num = BaseHeightTilt(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num2 = wx;
            double num3 = wy;
            float num4 = (float)((double)baseHeight - 0.4000000059604645);
            baseHeight = (float)((double)baseHeight + (double)num4);
            float num5 = (float)((double)PerlinNoise.Sample(num2 * 0.009999999776482582, num3 * 0.009999999776482582) * (double)PerlinNoise.Sample(num2 * 0.019999999552965164, num3 * 0.019999999552965164));
            num5 = (float)((double)num5 + (double)PerlinNoise.Sample(num2 * 0.05000000074505806, num3 * 0.05000000074505806) * (double)PerlinNoise.Sample(num2 * 0.10000000149011612, num3 * 0.10000000149011612) * (double)num5 * 0.5);
            baseHeight = (float)((double)baseHeight + (double)num5 * 0.20000000298023224);
            baseHeight = AddRivers(wx2, wy2, baseHeight);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num2 * 0.10000000149011612, num3 * 0.10000000149011612) * 0.009999999776482582);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num2 * 0.4000000059604645, num3 * 0.4000000059604645) * 0.003000000026077032);
            return (float)((double)baseHeight + (double)PerlinNoise.Sample(num2 * 0.20000000298023224, num3 * 0.20000000298023224) * 2.0 * (double)num);
        }

        private float GetMarshHeight(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float num = 0.137f;
            wx = (float)((double)wx + 100000.0);
            wy = (float)((double)wy + 100000.0);
            double num2 = wx;
            double num3 = wy;
            float num4 = (float)((double)PerlinNoise.Sample(num2 * 0.03999999910593033, num3 * 0.03999999910593033) * (double)PerlinNoise.Sample(num2 * 0.07999999821186066, num3 * 0.07999999821186066));
            num = (float)((double)num + (double)num4 * 0.029999999329447746);
            num = AddRivers(wx2, wy2, num);
            num = (float)((double)num + (double)PerlinNoise.Sample(num2 * 0.10000000149011612, num3 * 0.10000000149011612) * 0.009999999776482582);
            return (float)((double)num + (double)PerlinNoise.Sample(num2 * 0.4000000059604645, num3 * 0.4000000059604645) * 0.003000000026077032);
        }

        public float GetBiomeHeight(BiomeType biome, float wx, float wy, bool preGeneration = false)
        {
            float num = (!preGeneration) ? (float)((double)GetHeightMultiplier() * CreateAshlandsGap(wx, wy) * CreateDeepNorthGap(wx, wy)) : GetHeightMultiplier();
            
            if (MathUtils.Length(wx, wy) > 10500f)
            {
                return -2f * GetHeightMultiplier();
            }
            
            switch (biome)
            {
                case BiomeType.Swamp:
                    return (float)((double)GetMarshHeight(wx, wy) * (double)num);
                case BiomeType.DeepNorth:
                    return (float)((double)GetDeepNorthHeight(wx, wy) * (double)num);
                case BiomeType.Mountain:
                    return (float)((double)GetSnowMountainHeight(wx, wy, false) * (double)num);
                case BiomeType.BlackForest:
                    return (float)((double)GetForestHeight(wx, wy) * (double)num);
                case BiomeType.Ocean:
                    return (float)((double)GetOceanHeight(wx, wy) * (double)num);
                case BiomeType.AshLands:
                    if (preGeneration)
                    {
                        return (float)((double)GetAshlandsHeightPregenerate(wx, wy) * (double)num);
                    }
                    return (float)((double)GetAshlandsHeight(wx, wy) * (double)num);
                case BiomeType.Plains:
                    return (float)((double)GetPlainsHeight(wx, wy) * (double)num);
                case BiomeType.Meadows:
                    return (float)((double)GetMeadowsHeight(wx, wy) * (double)num);
                case BiomeType.Mistlands:
                    if (preGeneration)
                    {
                        return (float)((double)GetForestHeight(wx, wy) * (double)num);
                    }
                    return (float)((double)GetMistlandsHeight(wx, wy) * (double)num);
                default:
                    return 0f;
            }
        }
        
        public static float GetHeightMultiplier()
        {
            return 200f;
        }
        
        private double CreateAshlandsGap(float wx, float wy)
        {
            double num = (double)WorldAngle(wx, wy) * 100.0;
            double value = (double)MathUtils.Length(wx, wy + ashlandsYOffset) - ((double)ashlandsMinDistance + num);
            value = MathUtils.Clamp01(Math.Abs(value) / 400.0);
            return MathUtils.MathfLikeSmoothStep(0.0, 1.0, (float)value);
        }
        
        private double CreateDeepNorthGap(float wx, float wy)
        {
            double num = (double)WorldAngle(wx, wy) * 100.0;
            double value = (double)MathUtils.Length(wx, wy + 4000f) - (12000.0 + num);
            value = MathUtils.Clamp01(Math.Abs(value) / 400.0);
            return MathUtils.MathfLikeSmoothStep(0.0, 1.0, (float)value);
        }
        
        
        private float GetDeepNorthHeight(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num = wx;
            double num2 = wy;
            float num3 = Math.Max(0f, (float)((double)baseHeight - 0.4000000059604645));
            baseHeight = (float)((double)baseHeight + (double)num3);
            float num4 = (float)((double)PerlinNoise.Sample(num * 0.009999999776482582, num2 * 0.009999999776482582) * (double)PerlinNoise.Sample(num * 0.019999999552965164, num2 * 0.019999999552965164));
            num4 = (float)((double)num4 + (double)PerlinNoise.Sample(num * 0.05000000074505806, num2 * 0.05000000074505806) * (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * (double)num4 * 0.5);
            baseHeight = (float)((double)baseHeight + (double)num4 * 0.20000000298023224);
            baseHeight = (float)((double)baseHeight * 1.2000000476837158);
            baseHeight = AddRivers(wx2, wy2, baseHeight);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(wx * 0.1f, wy * 0.1f) * 0.009999999776482582);
            return (float)((double)baseHeight + (double)PerlinNoise.Sample(wx * 0.4f, wy * 0.4f) * 0.003000000026077032);
        }
        
        private float GetAshlandsHeight(float wx, float wy)
        {
            double num = wx;
            double num2 = wy;
            double a = GetBaseHeight((float)num, (float)num2);
            double num3 = (double)WorldAngle((float)num, (float)num2) * 100.0;
            double value = MathUtils.Length(num, num2 + (double)ashlandsYOffset - (double)ashlandsYOffset * 0.3) - ((double)ashlandsMinDistance + num3);
            value = Math.Abs(value) / 1000.0;
            value = 1.0 - MathUtils.Clamp01(value);
            value = MathUtils.MathfLikeSmoothStep(0.1, 1.0, value);
            double num4 = Math.Abs(num);
            num4 = 1.0 - MathUtils.Clamp01(num4 / 7500.0);
            value *= num4;
            double num5 = MathUtils.Length(num, num2) - 10150.0;
            num5 = 1.0 - MathUtils.Clamp01(num5 / 600.0);
            num += (double)(100000f + this.offset3);
            num2 += (double)(100000f + this.offset3);
            double num6 = 0.0;
            double num7 = 1.0;
            double num8 = 0.33000001311302185;
            int num9 = 5;
            for (int i = 0; i < num9; i++)
            {
                num6 += num7 * MathUtils.MathfLikeSmoothStep(0.0, 1.0, noiseGen.GetCellular((float)(num * num8), (float)(num2 * num8)));
                num8 *= 2.0;
                num7 *= 0.5;
            }
            num6 = MathUtils.Remap(num6, -1.0, 1.0, 0.0, 1.0);
            double num10 = MathUtils.Lerp(value, MathUtils.BlendOverlay(value, num6), 0.5);
            double num11 = PerlinNoise.Sample(num * 0.009999999776482582, num2 * 0.009999999776482582) * PerlinNoise.Sample(num * 0.019999999552965164, num2 * 0.019999999552965164);
            num11 += (double)(PerlinNoise.Sample(num * 0.05000000074505806, num2 * 0.05000000074505806) * PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612)) * num11 * 0.5;
            double num12 = MathUtils.Lerp(a, 0.15000000596046448, 0.75);
            num12 += num10 * 0.5;
            num12 = MathUtils.Lerp(-1.0, num12, MathUtils.MathfLikeSmoothStep(0.0, 1.0, num5));
            double num13 = 0.15;
            double num14 = 0.0;
            double num15 = 1.0;
            double num16 = 8.0;
            int num17 = 3;
            for (int j = 0; j < num17; j++)
            {
                num14 += num15 * noiseGen.GetCellular((float)(num * num16), (float)(num2 * num16));
                num16 *= 2.0;
                num15 *= 0.5;
            }
            num14 = MathUtils.Remap(num14, -1.0, 1.0, 0.0, 1.0);
            num14 = MathUtils.Clamp01(Math.Pow(num14, 4.0) * 2.0);
            double simplexFractal = noiseGen.GetSimplexFractal((float)(num * 0.075), (float)(num2 * 0.075));
            simplexFractal = MathUtils.Remap(simplexFractal, -1.0, 1.0, 0.0, 1.0);
            simplexFractal = Math.Pow(simplexFractal, 1.399999976158142);
            num12 *= simplexFractal;
            double num18 = MathUtils.Fbm(new Vector2((float)(num * 0.009999999776482582), (float)(num2 * 0.009999999776482582)), 3, 2.0f, 0.5f);
            num18 *= MathUtils.Clamp01(MathUtils.Remap(value, 0.0, 0.5, 0.5, 1.0));
            num18 = MathUtils.LerpStep(0.699999988079071, 1.0, num18);
            num18 = Math.Pow(num18, 2.0);
            double num19 = MathUtils.BlendOverlay(num18, num14);
            num19 *= MathUtils.Clamp01((num12 - num13 - 0.02) / 0.01);
            double x = PerlinNoise.Sample(num * 0.05 + 5124.0, num2 * 0.05 + 5000.0);
            x = Math.Pow(x, 2.0);
            x = MathUtils.Remap(x, 0.0, 1.0, 0.009999999776482582, 0.054999999701976776);
            double b = MathUtils.Clamp((float)(num12 - x), (float)(num13 + 0.009999999776482582), 5000f);
            num12 = MathUtils.Lerp(num12, b, num19);
            return (float)num12;
        }
        
        private float GetAshlandsHeightPregenerate(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num = wx;
            double num2 = wy;
            float num3 = (float)((double)PerlinNoise.Sample(num * 0.009999999776482582, num2 * 0.009999999776482582) * (double)PerlinNoise.Sample(num * 0.019999999552965164, num2 * 0.019999999552965164));
            num3 = (float)((double)num3 + (double)PerlinNoise.Sample(num * 0.05000000074505806, num2 * 0.05000000074505806) * (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * (double)num3 * 0.5);
            baseHeight = (float)((double)baseHeight + (double)num3 * 0.10000000149011612);
            baseHeight = (float)((double)baseHeight + 0.10000000149011612);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * 0.009999999776482582);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.4000000059604645, num2 * 0.4000000059604645) * 0.003000000026077032);
            return AddRivers(wx2, wy2, baseHeight);
        }
        
        private float GetMistlandsHeight(float wx, float wy)
        {
            float wx2 = wx;
            float wy2 = wy;
            float baseHeight = GetBaseHeight(wx, wy);
            wx = (float)((double)wx + 100000.0 + (double)this.offset3);
            wy = (float)((double)wy + 100000.0 + (double)this.offset3);
            double num = wx;
            double num2 = wy;
            float num3 = PerlinNoise.Sample(num * 0.019999999552965164 * 0.699999988079071, num2 * 0.019999999552965164 * 0.699999988079071) * PerlinNoise.Sample(num * 0.03999999910593033 * 0.699999988079071, num2 * 0.03999999910593033 * 0.699999988079071);
            num3 = (float)((double)num3 + (double)PerlinNoise.Sample(num * 0.029999999329447746 * 0.699999988079071, num2 * 0.029999999329447746 * 0.699999988079071) * (double)PerlinNoise.Sample(num * 0.05000000074505806 * 0.699999988079071, num2 * 0.05000000074505806 * 0.699999988079071) * (double)num3 * 0.5);
            num3 = ((num3 > 0f) ? ((float)Math.Pow(num3, 1.5)) : num3);
            baseHeight = (float)((double)baseHeight + (double)num3 * 0.4000000059604645);
            baseHeight = AddRivers(wx2, wy2, baseHeight);
            float num4 = (float)MathUtils.Clamp01((double)num3 * 7.0);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.10000000149011612, num2 * 0.10000000149011612) * 0.029999999329447746 * (double)num4);
            baseHeight = (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.4000000059604645, num2 * 0.4000000059604645) * 0.009999999776482582 * (double)num4);
            float num5 = (float)(1.0 - (double)num4 * 1.2000000476837158);
            num5 = (float)((double)num5 - (1.0 - (double)MathUtils.LerpStep(0.1f, 0.3f, num4)));
            float a = (float)((double)baseHeight + (double)PerlinNoise.Sample(num * 0.4000000059604645, num2 * 0.4000000059604645) * 0.0020000000949949026);
            float num6 = baseHeight;
            num6 = (float)((double)num6 * 400.0);
            num6 = (float)Math.Ceiling(num6);
            num6 = (float)((double)num6 / 400.0);
            baseHeight = MathUtils.Lerp(a, num6, num4);
            return baseHeight;
        }
        
        private float GetEdgeHeight(float wx, float wy)
        {
            float num = MathUtils.Length(wx, wy);
            float num2 = 10490f;
            if (num > num2)
            {
                float num3 = MathUtils.LerpStep(num2, 10500f, num);
                return (float)(-2.0 * (double)num3);
            }
            float t = MathUtils.LerpStep(10000f, 10100f, num);
            float baseHeight = GetBaseHeight(wx, wy);
            baseHeight = MathUtils.Lerp(baseHeight, 0f, t);
            return AddRivers(wx, wy, baseHeight);
        }

        // ========== River Generation System ==========
        
        private void Pregenerate()
        {
            FindLakes();
            rivers = PlaceRivers();
            streams = PlaceStreams();
        }

        private void FindLakes()
        {
            var list = new System.Collections.Generic.List<Vector2>();
            for (float num = -10000f; num <= 10000f; num = (float)((double)num + 128.0))
            {
                for (float num2 = -10000f; num2 <= 10000f; num2 = (float)((double)num2 + 128.0))
                {
                    if (!(new Vector2(num2, num).magnitude > 10000f) && GetBaseHeight(num2, num) < 0.05f)
                    {
                        list.Add(new Vector2(num2, num));
                    }
                }
            }
            lakes = MergePoints(list, 800f);
        }

        private System.Collections.Generic.List<Vector2> MergePoints(System.Collections.Generic.List<Vector2> points, float range)
        {
            var list = new System.Collections.Generic.List<Vector2>();
            while (points.Count > 0)
            {
                var vector = points[0];
                points.RemoveAt(0);
                while (points.Count > 0)
                {
                    int num = FindClosest(points, vector, range);
                    if (num == -1)
                    {
                        break;
                    }
                    vector = (vector + points[num]) * 0.5f;
                    points[num] = points[points.Count - 1];
                    points.RemoveAt(points.Count - 1);
                }
                list.Add(vector);
            }
            return list;
        }

        private int FindClosest(System.Collections.Generic.List<Vector2> points, Vector2 p, float maxDistance)
        {
            int result = -1;
            float num = 99999f;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] != p)
                {
                    float num2 = Vector2.Distance(p, points[i]);
                    if (num2 < maxDistance && num2 < num)
                    {
                        result = i;
                        num = num2;
                    }
                }
            }
            return result;
        }

        private System.Collections.Generic.List<River> PlaceRivers()
        {
            var rng = new UnityRandom(riverSeed);
            var list = new System.Collections.Generic.List<River>();
            var list2 = new System.Collections.Generic.List<Vector2>(lakes);
            
            while (list2.Count > 1)
            {
                var vector = list2[0];
                int num = FindRandomRiverEnd(rng, list, lakes, vector, 2000f, 0.4f, 128f);
                if (num == -1 && !HaveRiver(list, vector))
                {
                    num = FindRandomRiverEnd(rng, list, lakes, vector, 5000f, 0.4f, 128f);
                }
                if (num != -1)
                {
                    var river = new River();
                    river.p0 = vector;
                    river.p1 = lakes[num];
                    river.center = (river.p0 + river.p1) * 0.5f;
                    river.widthMax = rng.Range(60f, 100f);
                    river.widthMin = rng.Range(60f, river.widthMax);
                    float num2 = Vector2.Distance(river.p0, river.p1);
                    river.curveWidth = (float)((double)num2 / 15.0);
                    river.curveWavelength = (float)((double)num2 / 20.0);
                    list.Add(river);
                }
                else
                {
                    list2.RemoveAt(0);
                }
            }
            
            RenderRivers(rng, list);
            return list;
        }

        private System.Collections.Generic.List<River> PlaceStreams()
        {
            var rng = new UnityRandom(streamSeed);
            var list = new System.Collections.Generic.List<River>();
            
            for (int i = 0; i < 3000; i++)
            {
                if (FindStreamStartPoint(rng, 100, 26f, 31f, out var p, out var _) && 
                    FindStreamEndPoint(rng, 100, 36f, 44f, p, 80f, 200f, out var end))
                {
                    var center = (p + end) * 0.5f;
                    float pregenerationHeight = GetPregenerationHeight(center.x, center.y);
                    if (!(pregenerationHeight < 26f) && !(pregenerationHeight > 44f))
                    {
                        var river = new River();
                        river.p0 = p;
                        river.p1 = end;
                        river.center = center;
                        river.widthMax = 20f;
                        river.widthMin = 20f;
                        float num2 = Vector2.Distance(river.p0, river.p1);
                        river.curveWidth = (float)((double)num2 / 15.0);
                        river.curveWavelength = (float)((double)num2 / 20.0);                        list.Add(river);
                    }
                }
            }
            
            RenderRivers(rng, list);
            return list;
        }

        private bool FindStreamStartPoint(UnityRandom rng, int iterations, float minHeight, float maxHeight, out Vector2 p, out float starth)
        {
            for (int i = 0; i < iterations; i++)
            {
                float num = rng.Range(-10000f, 10000f);
                float num2 = rng.Range(-10000f, 10000f);
                float pregenerationHeight = GetPregenerationHeight(num, num2);
                if (pregenerationHeight > minHeight && pregenerationHeight < maxHeight)
                {
                    p = new Vector2(num, num2);
                    starth = pregenerationHeight;
                    return true;
                }
            }
            p = Vector2.zero;
            starth = 0f;
            return false;
        }

        private bool FindStreamEndPoint(UnityRandom rng, int iterations, float minHeight, float maxHeight, Vector2 start, float minLength, float maxLength, out Vector2 end)
        {
            float num = (float)(((double)maxLength - (double)minLength) / (double)iterations);
            float num2 = maxLength;
            for (int i = 0; i < iterations; i++)
            {
                num2 = (float)((double)num2 - (double)num);
                float f = rng.Range(0f, (float)Math.PI * 2f);
                var vector = start + new Vector2((float)Math.Sin(f), (float)Math.Cos(f)) * num2;
                float pregenerationHeight = GetPregenerationHeight(vector.x, vector.y);
                if (pregenerationHeight > minHeight && pregenerationHeight < maxHeight)
                {
                    end = vector;
                    return true;
                }
            }
            end = Vector2.zero;
            return false;
        }

        private float GetPregenerationHeight(float wx, float wy)
        {
            BiomeType biome = GetBiome(wx, wy);
            return GetBiomeHeight(biome, wx, wy, preGeneration: true);
        }

        private int FindRandomRiverEnd(UnityRandom rng, System.Collections.Generic.List<River> rivers, System.Collections.Generic.List<Vector2> points, Vector2 p, float maxDistance, float heightLimit, float checkStep)
        {
            var list = new System.Collections.Generic.List<int>();
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] != p && 
                    Vector2.Distance(p, points[i]) < maxDistance && 
                    !HaveRiver(rivers, p, points[i]) && 
                    IsRiverAllowed(p, points[i], checkStep, heightLimit))
                {
                    list.Add(i);
                }
            }
            if (list.Count == 0)
            {
                return -1;
            }
            return list[rng.Range(0, list.Count)];
        }

        private bool HaveRiver(System.Collections.Generic.List<River> rivers, Vector2 p0)
        {
            foreach (var river in rivers)
            {
                if (river.p0 == p0 || river.p1 == p0)
                {
                    return true;
                }
            }
            return false;
        }

        private bool HaveRiver(System.Collections.Generic.List<River> rivers, Vector2 p0, Vector2 p1)
        {
            foreach (var river in rivers)
            {
                if ((river.p0 == p0 && river.p1 == p1) || (river.p0 == p1 && river.p1 == p0))
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsRiverAllowed(Vector2 p0, Vector2 p1, float step, float heightLimit)
        {
            float num = Vector2.Distance(p0, p1);
            var normalized = (p1 - p0).normalized;
            bool flag = true;
            for (float num2 = step; num2 <= (float)((double)num - (double)step); num2 = (float)((double)num2 + (double)step))
            {
                var vector = p0 + normalized * num2;
                float baseHeight = GetBaseHeight(vector.x, vector.y);
                if (baseHeight > heightLimit)
                {
                    return false;
                }
                if (baseHeight > 0.05f)
                {
                    flag = false;
                }
            }
            if (flag)
            {
                return false;
            }
            return true;
        }

        private void RenderRivers(UnityRandom rng, System.Collections.Generic.List<River> rivers)
        {
            var dictionary = new System.Collections.Generic.Dictionary<Vector2i, System.Collections.Generic.List<RiverPoint>>();
            
            foreach (var river in rivers)
            {
                float num = (float)((double)river.widthMin / 8.0);
                var normalized = (river.p1 - river.p0).normalized;
                var vector = new Vector2(-normalized.y, normalized.x);
                float num2 = Vector2.Distance(river.p0, river.p1);
                
                for (float num3 = 0f; num3 <= num2; num3 = (float)((double)num3 + (double)num))
                {
                    float num4 = (float)((double)num3 / (double)river.curveWavelength);
                    float num5 = (float)(Math.Sin(num4) * Math.Sin((double)num4 * 0.634119987487793) * Math.Sin((double)num4 * 0.3341200053691864) * (double)river.curveWidth);
                    float r = rng.Range(river.widthMin, river.widthMax);
                    var p = river.p0 + normalized * num3 + vector * num5;
                    AddRiverPoint(dictionary, p, r, river);
                }
            }
            
            foreach (var item in dictionary)
            {
                if (riverPoints.TryGetValue(item.Key, out var value))
                {
                    var list = new System.Collections.Generic.List<RiverPoint>(value);
                    list.AddRange(item.Value);
                    riverPoints[item.Key] = list.ToArray();
                }
                else
                {
                    var value2 = item.Value.ToArray();
                    riverPoints.Add(item.Key, value2);
                }
            }
        }

        private void AddRiverPoint(System.Collections.Generic.Dictionary<Vector2i, System.Collections.Generic.List<RiverPoint>> riverPoints, Vector2 p, float r, River river)
        {
            var riverGrid = GetRiverGrid(p.x, p.y);
            int num = (int)Math.Ceiling((float)((double)r / 64.0));
            
            for (int i = riverGrid.y - num; i <= riverGrid.y + num; i++)
            {
                for (int j = riverGrid.x - num; j <= riverGrid.x + num; j++)
                {
                    var grid = new Vector2i(j, i);
                    if (InsideRiverGrid(grid, p, r))
                    {
                        AddRiverPoint(riverPoints, grid, p, r, river);
                    }
                }
            }
        }

        private void AddRiverPoint(System.Collections.Generic.Dictionary<Vector2i, System.Collections.Generic.List<RiverPoint>> riverPoints, Vector2i grid, Vector2 p, float r, River river)
        {
            if (riverPoints.TryGetValue(grid, out var value))
            {
                value.Add(new RiverPoint(p, r));
            }
            else
            {
                riverPoints.Add(grid, new System.Collections.Generic.List<RiverPoint> { new RiverPoint(p, r) });
            }
        }

        private bool InsideRiverGrid(Vector2i grid, Vector2 p, float r)
        {
            var vector = new Vector2((float)((double)grid.x * 64.0), (float)((double)grid.y * 64.0));
            var vector2 = p - vector;
            if (Math.Abs(vector2.x) < (float)((double)r + 32.0))
            {
                return Math.Abs(vector2.y) < (float)((double)r + 32.0);
            }
            return false;
        }

        private Vector2i GetRiverGrid(float wx, float wy)
        {
            int x = (int)Math.Floor((float)(((double)wx + 32.0) / 64.0));
            int y = (int)Math.Floor((float)(((double)wy + 32.0) / 64.0));
            return new Vector2i(x, y);
        }

        private void GetRiverWeight(float wx, float wy, out float weight, out float width)
        {
            var riverGrid = GetRiverGrid(wx, wy);
            
            if (riverPoints.TryGetValue(riverGrid, out var value))
            {
                GetWeight(value, wx, wy, out weight, out width);
            }
            else
            {
                weight = 0f;
                width = 0f;
            }
        }

        private void GetWeight(RiverPoint[] points, float wx, float wy, out float weight, out float width)
        {
            var vector = new Vector2(wx, wy);
            weight = 0f;
            width = 0f;
            float num = 0f;
            float num2 = 0f;
            
            for (int i = 0; i < points.Length; i++)
            {
                var riverPoint = points[i];
                float num3 = Vector2.SqrMagnitude(riverPoint.p - vector);
                if (num3 < riverPoint.w2)
                {
                    float num4 = (float)Math.Sqrt(num3);
                    float num5 = (float)(1.0 - (double)num4 / (double)riverPoint.w);
                    if (num5 > weight)
                    {
                        weight = num5;
                    }
                    num = (float)((double)num + (double)riverPoint.w * (double)num5);
                    num2 = (float)((double)num2 + (double)num5);
                }
            }
            
            if (num2 > 0f)
            {
                width = (float)((double)num / (double)num2);
            }
        }

        // AddRivers implementation - uses river system to lower terrain near rivers
        private float AddRivers(float wx, float wy, float h)
        {
            GetRiverWeight(wx, wy, out var weight, out var width);
            if (weight <= 0f)
            {
                return h;
            }
            
            float t = MathUtils.LerpStep(20f, 60f, width);
            float num = MathUtils.Lerp(0.14f, 0.12f, t);
            float num2 = MathUtils.Lerp(0.139f, 0.128f, t);
            
            if (h > num)
            {
                h = MathUtils.Lerp(h, num, weight);
            }
            if (h > num2)
            {
                float t2 = MathUtils.LerpStep(0.85f, 1f, weight);
                h = MathUtils.Lerp(h, num2, t2);
            }
            return h;
        }
    }
}
