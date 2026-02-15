using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildDLL
{
    [MenuItem("Build/Build UnityPerlinNoise DLL")]
    public static void Build()
    {
        string outputPath = Path.Combine(Application.dataPath, "..", "Build");
        
        if (!Directory.Exists(outputPath))
        {
            Directory.CreateDirectory(outputPath);
        }
        
        string[] sourceFiles = new string[]
        {
            "Assets/UnityPerlinNoise.cs"
        };
        
        string outputDLL = Path.Combine(outputPath, "UnityPerlinNoise.dll");
        
        var assemblyBuilder = new UnityEditor.Compilation.AssemblyBuilder(outputDLL, sourceFiles);
        assemblyBuilder.referencesOptions = UnityEditor.Compilation.ReferencesOptions.UseEngineModules;
        
        assemblyBuilder.buildStarted += (assemblyPath) =>
        {
            Debug.Log($"Starting build: {assemblyPath}");
        };
        
        assemblyBuilder.buildFinished += (assemblyPath, messages) =>
        {
            bool hasErrors = false;
            foreach (var msg in messages)
            {
                if (msg.type == UnityEditor.Compilation.CompilerMessageType.Error)
                {
                    Debug.LogError(msg.message);
                    hasErrors = true;
                }
            }
            
            if (!hasErrors)
            {
                Debug.Log($"Build successful: {Path.GetFullPath(outputDLL)}");
            }
        };
        
        if (!assemblyBuilder.Build())
        {
            Debug.LogError("Failed to start assembly build");
        }
    }
}
