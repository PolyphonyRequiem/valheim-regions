using System;
using System.Collections.Generic;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Builds heightmap data by sampling WorldGenerator and blending biome heights at boundaries.
    /// Replicates Valheim's HeightmapBuilder.Build() logic.
    /// </summary>
    public class HeightmapBuilder
    {
        /// <summary>
        /// Builds heightmap data for a terrain chunk.
        /// </summary>
        /// <param name="worldGen">World generator to sample from.</param>
        /// <param name="center">Center position of the heightmap chunk.</param>
        /// <param name="width">Width of the heightmap in vertices (e.g., 32 for a 32×32 chunk).</param>
        /// <param name="scale">Scale per vertex (e.g., 1.0 means 1 meter between vertices).</param>
        /// <returns>Array of heights in meters, one per vertex.</returns>
        public static float[] BuildHeightmap(WorldGenerator worldGen, Vector2 center, int width, float scale)
        {
            int num = width + 1; // vertices = width + 1 (e.g., 33 vertices for 32×32 chunk)
            int num2 = num * num;
            
            // Calculate bottom-left corner of the heightmap (line 145)
            Vector2 bottomLeft = center + new Vector2(width * scale * -0.5f, width * scale * -0.5f);
            
            // Get corner biomes (lines 148-151)
            var cornerBiomes = new BiomeType[4];
            cornerBiomes[0] = worldGen.GetBiome(bottomLeft.x, bottomLeft.y);
            cornerBiomes[1] = worldGen.GetBiome(bottomLeft.x + width * scale, bottomLeft.y);
            cornerBiomes[2] = worldGen.GetBiome(bottomLeft.x, bottomLeft.y + width * scale);
            cornerBiomes[3] = worldGen.GetBiome(bottomLeft.x + width * scale, bottomLeft.y + width * scale);
            
            var biome = cornerBiomes[0];
            var biome2 = cornerBiomes[1];
            var biome3 = cornerBiomes[2];
            var biome4 = cornerBiomes[3];
            
            var heights = new float[num2];
            
            // Build heightmap grid (lines 167-203)
            for (int k = 0; k < num; k++)
            {
                float wy = bottomLeft.y + k * scale;
                float t = SmoothStep(0f, 1f, (float)k / width);
                
                for (int l = 0; l < num; l++)
                {
                    float wx = bottomLeft.x + l * scale;
                    float t2 = SmoothStep(0f, 1f, (float)l / width);
                    float height = 0f;
                    
                    // Check if all corners are the same biome (line 182)
                    if (biome3 == biome && biome2 == biome && biome4 == biome)
                    {
                        // Single biome - no blending needed (line 184)
                        height = worldGen.GetBiomeHeight(biome, wx, wy);
                    }
                    else
                    {
                        // Multiple biomes - blend heights (lines 188-198)
                        float biomeHeight = worldGen.GetBiomeHeight(biome, wx, wy);
                        float biomeHeight2 = worldGen.GetBiomeHeight(biome2, wx, wy);
                        float biomeHeight3 = worldGen.GetBiomeHeight(biome3, wx, wy);
                        float biomeHeight4 = worldGen.GetBiomeHeight(biome4, wx, wy);
                        
                        // Bilinear interpolation with smoothstep
                        float a = Lerp(biomeHeight, biomeHeight2, t2);
                        float b = Lerp(biomeHeight3, biomeHeight4, t2);
                        height = Lerp(a, b, t);
                    }
                    
                    heights[k * num + l] = height;
                }
            }
            
            return heights;
        }
        
        /// <summary>
        /// Gets biome at a position using heightmap corner-based interpolation.
        /// Replicates Heightmap.GetBiome() logic.
        /// </summary>
        public static BiomeType GetHeightmapBiome(WorldGenerator worldGen, Vector2 worldPos, int chunkSize, float scale)
        {
            // Find the heightmap chunk this position belongs to
            float chunkWorldX = (float)Math.Floor(worldPos.x / (chunkSize * scale)) * (chunkSize * scale);
            float chunkWorldY = (float)Math.Floor(worldPos.y / (chunkSize * scale)) * (chunkSize * scale);
            var chunkCenter = new Vector2(chunkWorldX + (chunkSize * scale) * 0.5f, chunkWorldY + (chunkSize * scale) * 0.5f);
            
            // Get corner biomes
            Vector2 bottomLeft = chunkCenter + new Vector2(chunkSize * scale * -0.5f, chunkSize * scale * -0.5f);
            var corner0 = worldGen.GetBiome(bottomLeft.x, bottomLeft.y);
            var corner1 = worldGen.GetBiome(bottomLeft.x + chunkSize * scale, bottomLeft.y);
            var corner2 = worldGen.GetBiome(bottomLeft.x, bottomLeft.y + chunkSize * scale);
            var corner3 = worldGen.GetBiome(bottomLeft.x + chunkSize * scale, bottomLeft.y + chunkSize * scale);
            
            // If all corners are the same, return that biome
            if (corner0 == corner1 && corner0 == corner2 && corner0 == corner3)
            {
                return corner0;
            }
            
            // Interpolate biomes based on position within chunk
            float localX = (worldPos.x - bottomLeft.x) / (chunkSize * scale);
            float localY = (worldPos.y - bottomLeft.y) / (chunkSize * scale);
            
            // Distance-weighted voting (matching Heightmap.cs)
            var weights = new Dictionary<BiomeType, float>();
            float dist0 = Distance(localX, localY, 0f, 0f);
            float dist1 = Distance(localX, localY, 1f, 0f);
            float dist2 = Distance(localX, localY, 0f, 1f);
            float dist3 = Distance(localX, localY, 1f, 1f);
            
            AddWeight(weights, corner0, dist0);
            AddWeight(weights, corner1, dist1);
            AddWeight(weights, corner2, dist2);
            AddWeight(weights, corner3, dist3);
            
            // Return biome with highest weight
            float maxWeight = -1f;
            BiomeType result = BiomeType.None;
            foreach (var kv in weights)
            {
                if (kv.Value > maxWeight)
                {
                    maxWeight = kv.Value;
                    result = kv.Key;
                }
            }
            return result;
        }
        
        private static void AddWeight(Dictionary<BiomeType, float> weights, BiomeType biome, float weight)
        {
            if (!weights.ContainsKey(biome))
            {
                weights[biome] = 0f;
            }
            weights[biome] += weight;
        }
        
        private static float Distance(float x, float y, float tx, float ty)
        {
            float dx = x - tx;
            float dy = y - ty;
            return Math.Max(0f, 1f - (float)Math.Sqrt(dx * dx + dy * dy));
        }
        
        private static float SmoothStep(float min, float max, float value)
        {
            float t = MathUtils.Clamp01((value - min) / (max - min));
            return t * t * (3f - 2f * t);
        }
        
        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
