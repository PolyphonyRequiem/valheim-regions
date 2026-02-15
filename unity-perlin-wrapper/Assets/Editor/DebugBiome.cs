using UnityEngine;
using UnityEditor;

public class DebugBiome
{
    [MenuItem("Debug/Check Biome")]
    public static void CheckBiome()
    {
        float worldX = -3476f;
        float worldZ = 979f;
        
        // Initialize with seed
        int seedHash = 298112588; // HHcLC5acQt
        Random.InitState(seedHash);
        double offset0 = Random.value * 100000.0;
        double offset1 = Random.value * 100000.0;
        double offset2 = Random.value * 100000.0;
        
        Debug.Log($"Testing coordinate ({worldX}, {worldZ})");
        Debug.Log($"Expected: BlackForest");
        Debug.Log("");
        
        // Distance
        float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
        Debug.Log($"Distance from center: {distance:F1}");
        
        // Base height
        float baseHeight = GetBaseHeight(worldX, worldZ);
        Debug.Log($"Base height: {baseHeight:F4}");
        Debug.Log($"  Is Ocean (< 0.05)? {(baseHeight < 0.05f ? "YES" : "NO")}");
        Debug.Log($"  Is Mountain (> 0.4)? {(baseHeight > 0.4f ? "YES" : "NO")}");
        Debug.Log("");
        
        // BlackForest check
        double forestX = (offset2 + worldX) * 0.001;
        double forestZ = (offset2 + worldZ) * 0.001;
        float forestNoise = Mathf.PerlinNoise((float)forestX, (float)forestZ);
        Debug.Log($"BlackForest noise: {forestNoise:F4} (threshold: 0.4)");
        Debug.Log($"  Noise > 0.4? {(forestNoise > 0.4f ? "YES" : "NO")}");
        Debug.Log($"  Distance > 600? {(distance > 600f ? "YES" : "NO")}");
        Debug.Log($"  Distance < 6000? {(distance < 6000f ? "YES" : "NO")}");
        
        // Plains check
        double plainsX = (offset1 + worldX) * 0.001;
        double plainsZ = (offset1 + worldZ) * 0.001;
        float plainsNoise = Mathf.PerlinNoise((float)plainsX, (float)plainsZ);
        Debug.Log("");
        Debug.Log($"Plains noise: {plainsNoise:F4} (threshold: 0.4)");
        Debug.Log($"  Would be Plains? {(plainsNoise > 0.4f && distance > 3000f && distance < 8000f ? "YES" : "NO")}");
    }
    
    static float GetBaseHeight(float worldX, float worldZ)
    {
        const float WorldRadius = 10000f;
        const float WorldEdgeRadius = 10500f;
        
        double wx = worldX * 0.002;
        double wz = worldZ * 0.002;
        double rx = worldX * 0.0005;
        double rz = worldZ * 0.0005;
        double rx2 = worldX * 0.00025;
        double rz2 = worldZ * 0.00025;
        
        float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
        float distanceEdge = Mathf.Min(distance / WorldRadius, 1f);
        
        float height = Mathf.PerlinNoise((float)wx, (float)wz) * Mathf.PerlinNoise((float)rx, (float)rz) 
                     * Mathf.PerlinNoise((float)rx2, (float)rz2);
        
        height += distanceEdge * 0.15f;
        
        float ridgeNoise = Mathf.Abs(Mathf.PerlinNoise((float)wx * 3f, (float)wz * 3f) - 0.5f) * 2f;
        height += ridgeNoise * 0.1f;
        
        if (distance > WorldEdgeRadius)
        {
            float edgeFade = Mathf.Clamp01((distance - WorldEdgeRadius) / 500f);
            height = Mathf.Lerp(height, -0.2f, edgeFade);
        }
        
        return height;
    }
}
