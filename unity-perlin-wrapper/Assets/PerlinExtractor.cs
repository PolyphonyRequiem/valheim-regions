using UnityEngine;
using System.IO;
using System.Text;

public class PerlinExtractor : MonoBehaviour
{
    void Start()
    {
        ExtractData();
    }
    
    void ExtractData()
    {
        StringBuilder sb = new StringBuilder();
        
        sb.AppendLine("// Unity PerlinNoise exact values");
        sb.AppendLine("// Can be used to reverse-engineer or create lookup table");
        sb.AppendLine();
        sb.AppendLine("namespace WorldZones.WorldGen");
        sb.AppendLine("{");
        sb.AppendLine("    public static class UnityPerlinLookup");
        sb.AppendLine("    {");
        
        // Extract values at key coordinates that Valheim uses
        sb.AppendLine("        // Sample test values");
        float[] coords = { 0f, 0.002f, 0.003f, 0.01f, 0.1f, 0.5f, 1f, 5f, 10f, 100f, 500f, 1000f, 5000f };
        
        sb.AppendLine("        public static float GetSample(float x, float y)");
        sb.AppendLine("        {");
        sb.AppendLine("            // Lookup table for testing");
        
        foreach (float x in coords)
        {
            foreach (float y in coords)
            {
                float val = Mathf.PerlinNoise(x, y);
                sb.AppendLine($"            // ({x}, {y}) = {val:F10}f");
            }
        }
        
        sb.AppendLine("            return 0f; // Add actual lookup logic");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        
        string path = Path.Combine(Application.dataPath, "..", "UnityPerlinValues.cs");
        File.WriteAllText(path, sb.ToString());
        
        Debug.Log($"Extracted to: {path}");
        
        #if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
        #endif
    }
}
