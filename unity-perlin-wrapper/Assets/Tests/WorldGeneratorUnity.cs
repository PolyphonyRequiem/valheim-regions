using UnityEngine;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// WorldGenerator implementation that uses Unity's native runtime.
    /// This is the reference implementation for validating the lookup table approach.
    /// </summary>
    public class WorldGeneratorUnity
    {
        private readonly string seed;
        private readonly int seedHash;
        private readonly double offset0, offset1, offset2, offset3, offset4;
        private readonly float minDarklandNoise = 0.4f;
        private readonly float maxMarshDistance = 6000f;
        
        public WorldGeneratorUnity(string seed)
        {
            this.seed = seed ?? throw new System.ArgumentNullException(nameof(seed));
            
            // Use Valheim's GetStableHashCode
            this.seedHash = string.IsNullOrEmpty(seed) ? 0 : GetStableHashCode(seed);
            
            // Generate deterministic offsets using Unity's Random
            Random.InitState(this.seedHash);
            this.offset0 = Random.value * 100000.0;
            this.offset1 = Random.value * 100000.0;
            this.offset2 = Random.value * 100000.0;
            this.offset3 = Random.value * 100000.0;
            this.offset4 = Random.value * 100000.0;
            
            Debug.Log($"[WorldGeneratorUnity] Seed: {seed}, Hash: {seedHash}");
            Debug.Log($"  Offsets: {offset0:F4}, {offset1:F4}, {offset2:F4}, {offset3:F4}, {offset4:F4}");
        }
        
        public BiomeType GetBiome(float worldX, float worldZ)
        {
            float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
            float angle = Mathf.Atan2(worldX, worldZ);
            float angleVariation = Mathf.PerlinNoise(angle * 100f, 0f) * 500f;
            
            float baseHeight = GetBaseHeight(worldX, worldZ);
            bool waterAlwaysOcean = distance >= 10000f;
            
            if (waterAlwaysOcean && GetHeight(worldX, worldZ) <= 0.05f)
            {
                return BiomeType.Ocean;
            }
            
            // Check base ocean condition (using 0.05 threshold, NOT 0.02!)
            if (!waterAlwaysOcean && baseHeight <= 0.05f)
            {
                return BiomeType.Ocean;
            }
            
            // Mountain (high elevation)
            if (baseHeight > 0.4f)
            {
                return BiomeType.Mountain;
            }
            
            // Swamp
            double swampX = (this.offset0 + worldX) * 0.001;
            double swampZ = (this.offset0 + worldZ) * 0.001;
            if (Mathf.PerlinNoise((float)swampX, (float)swampZ) > 0.6f 
                && distance > 2000f 
                && distance < this.maxMarshDistance 
                && baseHeight > 0.05f 
                && baseHeight < 0.25f)
            {
                return BiomeType.Swamp;
            }
            
            // Mistlands
            double mistX = (this.offset4 + worldX) * 0.001;
            double mistZ = (this.offset4 + worldZ) * 0.001;
            if (Mathf.PerlinNoise((float)mistX, (float)mistZ) > this.minDarklandNoise 
                && distance > 6000f 
                && baseHeight > 0.05f)
            {
                return BiomeType.Mistlands;
            }
            
            // BlackForest (CHECK THIS BEFORE PLAINS!)
            double forestX = (this.offset2 + worldX) * 0.001;
            double forestZ = (this.offset2 + worldZ) * 0.001;
            if (Mathf.PerlinNoise((float)forestX, (float)forestZ) > 0.4f 
                && distance > 600f && distance < 6000f)
            {
                return BiomeType.BlackForest;
            }
            
            // Plains
            double plainsX = (this.offset1 + worldX) * 0.001;
            double plainsZ = (this.offset1 + worldZ) * 0.001;
            if (Mathf.PerlinNoise((float)plainsX, (float)plainsZ) > 0.4f 
                && distance > 3000f && distance < 8000f)
            {
                return BiomeType.Plains;
            }
            
            // BlackForest (far ring fallback)
            if (Mathf.PerlinNoise((float)forestX, (float)forestZ) > 0.4f 
                && distance > 8500f)
            {
                return BiomeType.BlackForest;
            }
            
            // Default: Meadows (starting biome, center of world)
            return BiomeType.Meadows;
        }
        
        public float GetBaseHeight(float worldX, float worldZ)
        {
            double x = this.offset0 + worldX;
            double y = this.offset0 + worldZ;
            float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
            
            // Multi-octave noise
            float height = 0f;
            
            float n1 = Mathf.PerlinNoise((float)(x * 0.002 * 0.5), (float)(y * 0.002 * 0.5));
            float n2 = Mathf.PerlinNoise((float)(x * 0.003 * 0.5), (float)(y * 0.003 * 0.5));
            height += n1 * n2 * 1.0f;
            
            float n3 = Mathf.PerlinNoise((float)(x * 0.002), (float)(y * 0.002));
            float n4 = Mathf.PerlinNoise((float)(x * 0.003), (float)(y * 0.003));
            height += n3 * n4 * height * 0.9f;
            
            float n5 = Mathf.PerlinNoise((float)(x * 0.005), (float)(y * 0.005));
            float n6 = Mathf.PerlinNoise((float)(x * 0.01), (float)(y * 0.01));
            height += n5 * n6 * 0.5f * height;
            
            height -= 0.07f;
            
            // Rivers
            float river1 = Mathf.PerlinNoise((float)(x * 0.002 * 0.25 + 0.123), (float)(y * 0.002 * 0.25 + 0.15123));
            float river2 = Mathf.PerlinNoise((float)(x * 0.002 * 0.25 + 0.321), (float)(y * 0.002 * 0.25 + 0.231));
            float riverDelta = Mathf.Abs(river1 - river2);
            float riverIntensity = 1f - LerpStep(0.02f, 0.12f, riverDelta);
            float riverDistanceFade = SmoothStep(744f, 1000f, distance);
            float riverFactor = riverIntensity * riverDistanceFade;
            height *= (1f - riverFactor);
            
            // Edge fade
            if (distance > 10000f)
            {
                float edgeFade = LerpStep(10000f, 10500f, distance);
                height = Mathf.Lerp(height, -0.2f, edgeFade);
                
                if (distance > 10490f)
                {
                    float trenchFade = LerpStep(10490f, 10500f, distance);
                    height = Mathf.Lerp(height, -2f, trenchFade);
                }
            }
            
            return height;
        }
        
        private float LerpStep(float min, float max, float t)
        {
            return Mathf.Clamp01((t - min) / (max - min));
        }
        
        private float SmoothStep(float min, float max, float t)
        {
            float x = LerpStep(min, max, t);
            return x * x * (3f - 2f * x);
        }
        
        private int GetStableHashCode(string str)
        {
            int hash1 = 5381;
            int hash2 = hash1;
            
            for (int i = 0; i < str.Length; i++)
            {
                int c = str[i];
                if (i % 2 == 0)
                    hash1 = ((hash1 << 5) + hash1) ^ c;
                else
                    hash2 = ((hash2 << 5) + hash2) ^ c;
            }
            
            return hash1 + (hash2 * 1566083941);
        }
    }
}
