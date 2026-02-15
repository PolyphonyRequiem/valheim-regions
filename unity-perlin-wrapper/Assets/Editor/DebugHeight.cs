using UnityEngine;
using UnityEditor;

public class DebugHeight
{
    [MenuItem("Debug/Compare Heights")]
    public static void CompareHeights()
    {
        float worldX = -2692f;
        float worldZ = 563f;
        
        Debug.Log($"=== Coordinate ({worldX}, {worldZ}) - Expected: Meadows ===");
        
        int seedHash = 298112588;
        Random.InitState(seedHash);
        double offset0 = Random.value * 100000.0;
        double offset1 = Random.value * 100000.0;
        
        float distance = Mathf.Sqrt(worldX * worldX + worldZ * worldZ);
        Debug.Log($"Distance: {distance:F1}");
        Debug.Log("");
        
        // Current complex heightmap
        double x = worldX + 100000.0 + offset0;
        double y = worldZ + 100000.0 + offset1;
        
        float h1 = Mathf.PerlinNoise((float)(x * 0.001), (float)(y * 0.001));
        Debug.Log($"Simple test noise: {h1:F4}");
        
        float h2 = Mathf.PerlinNoise((float)(worldX * 0.002), (float)(worldZ * 0.002));
        Debug.Log($"Without offset noise: {h2:F4}");
        Debug.Log("");
        Debug.Log("We get Mountain (height > 0.4). But should get Meadows.");
        Debug.Log("This means our heightmap algorithm is fundamentally wrong.");
    }
}
