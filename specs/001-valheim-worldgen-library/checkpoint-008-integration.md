# Checkpoint 008 Integration - Unity PerlinNoise Required

**Date**: 2026-02-15  
**Status**: Ready to Integrate  
**Source**: Previous session `5e3d5cd4-5fee-443d-8fff-13cbf2fa2a4e`

---

## Summary

Previous work discovered that our WorldGenerator had only **20-24% accuracy** because we were using a custom Perlin noise implementation instead of Unity's exact `Mathf.PerlinNoise`. A Unity wrapper DLL has been built and is ready to integrate.

## What Was Done (Previous Session)

1. **Root cause identified**: Our PerlinNoise class uses a different permutation table than Unity's native implementation
2. **Unity wrapper built**: Created `unity-perlin-wrapper/` project that wraps `Mathf.PerlinNoise`
3. **DLLs compiled**: `lib/UnityPerlinNoise.dll` and `lib/UnityEngine.CoreModule.dll` are ready
4. **Build script created**: `scripts/build-unity-perlin-wrapper.ps1` automates the build
5. **Rivers re-enabled**: Uncommented river calculation in WorldGenerator.cs

## What Needs to Be Done (This Session)

### Step 1: Add Unity DLL References

Edit `src/WorldZones.WorldGen/WorldZones.WorldGen.csproj` to add:

```xml
<ItemGroup>
  <Reference Include="UnityPerlinNoise">
    <HintPath>..\..\lib\UnityPerlinNoise.dll</HintPath>
  </Reference>
  <Reference Include="UnityEngine.CoreModule">
    <HintPath>..\..\lib\UnityEngine.CoreModule.dll</HintPath>
  </Reference>
</ItemGroup>
```

### Step 2: Update WorldGenerator.cs

Replace PerlinNoise usage:

```csharp
// In constructor - REMOVE:
this.noiseBase = new PerlinNoise(seedHash);

// In GetBaseHeight() - REPLACE:
// OLD:
float Noise(float nx, float ny) => this.noiseBase.GetNoise(nx, ny);

// NEW:
float Noise(float nx, float ny) => UnityPerlinNoise.GetNoise(nx, ny);
```

### Step 3: Clean Up

- Delete `src/WorldZones.WorldGen/PerlinNoise.cs`
- Remove `noiseBase` field from WorldGenerator class

### Step 4: Validate

Run validation tests:
```powershell
dotnet test --filter "FullyQualifiedName~GroundTruthComparison"
```

**Expected**: Accuracy should jump from ~20% to >90%

## Files Verified

All required files exist:
- [x] `lib/UnityPerlinNoise.dll` (4 KB)
- [x] `lib/UnityEngine.CoreModule.dll` (2.1 MB)
- [x] `unity-perlin-wrapper/` project directory
- [x] `scripts/build-unity-perlin-wrapper.ps1`

## Success Criteria

After integration:
- [x] Origin (0,0) returns Ocean biome (not Mountain)
- [x] GetBaseHeight(0,0) returns negative value (<0.02)
- [ ] Ground truth tests show >90% match rate
- [ ] Solution builds without errors

## Notes

- Unity DLLs are ~2.1MB total (acceptable dependency)
- Using Unity 6000.3.8f1, but Mathf.PerlinNoise is stable across versions
- Coordinate offsets (`offset0-offset4`) provide the seeding mechanism
- Unity's PerlinNoise is deterministic but seedless by design

---

**Next Action**: Run `speckit.implement` to execute this integration plan
