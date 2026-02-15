# Unity PerlinNoise Wrapper

This minimal Unity project builds a DLL that wraps Unity's native `Mathf.PerlinNoise` implementation for use in the WorldZones library.

## Why This Exists

Unity's `Mathf.PerlinNoise` uses an internal, undocumented permutation table that cannot be exactly replicated in pure C#. Valheim's world generator depends on Unity's exact implementation, so we must use Unity's native code to achieve accurate results.

## Prerequisites

- Unity 2022.3 or later (match Valheim's Unity version if possible)
- Unity installed with CLI tools accessible

## Building the DLL

### Command Line Build (Recommended)

```powershell
cd scripts
.\build-unity-perlin-wrapper.ps1
```

The script will:
1. Find your Unity installation automatically
2. Build the DLL via Unity CLI
3. Show you the next steps

### Manual Build

```powershell
# Find Unity path
$unityPath = "C:\Program Files\Unity\Hub\Editor\2022.3.X\Editor\Unity.exe"

# Build
& $unityPath -quit -batchmode -projectPath ".\unity-perlin-wrapper" -executeMethod BuildDLL.Build -logFile unity-perlin-wrapper\build.log
```

## Build Output

After successful build, DLL will be at: `unity-perlin-wrapper\Build\UnityPerlinNoise.dll`

## Next Steps

See build script output for integration instructions.
