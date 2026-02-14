# C# Language Compatibility Guide

**Target Framework**: .NET Framework 4.7.2  
**Language Version**: C# 9.0  
**Unity Compatibility**: Unity 2019.4 (Valheim runtime)

## Rationale

C# language version and target framework are NOT 1:1 coupled. Many modern C# features are **compiler features** that emit IL compatible with older runtimes. We maximize language quality while maintaining runtime compatibility.

## Safe Features (C# 8-9)

### ✅ C# 8.0 Features (Compiler-Only)
- **Nullable reference types** (`string?`, `string!`) - Compile-time null safety
- **Pattern matching enhancements** - `switch` expressions, property patterns
- **Using declarations** - `using var file = ...` (no braces)
- **Static local functions**
- **Null-coalescing assignment** - `??=`

### ✅ C# 9.0 Features (Compiler-Only)
- **init-only properties** - `{ get; init; }` for immutability
- **Target-typed new** - `List<string> list = new();`
- **Pattern matching improvements** - Relational patterns, logical patterns
- **Record structs** - `record struct Point(float X, float Z);` (value semantics)

## Restricted Features

### ⚠️ UNITY 2019.4 COMPATIBILITY ISSUES

**Critical Discovery (2026-02-14):** Unity 2019.4 uses C# 7.x compiler and .NET 4.x runtime that lacks C# 9 support classes.

**Records Compatibility:**
- ❌ **Reference type records** (`record class`) - Emit `IsExternalInit` attribute Unity runtime doesn't have
- ⚠️ **Record structs** - Same issue, UNTESTED with polyfill
- **Workaround**: Include `IsExternalInit` polyfill (see below), but TEST IN UNITY EARLY

**Init Properties:**
- ⚠️ Require `IsExternalInit` polyfill for Unity compatibility
- Safe to use in our library IF we include polyfill

### IsExternalInit Polyfill (Required for Unity)

```csharp
// Add to WorldZones.WorldGen project
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
```

This allows C# 9 `init` and `record` features to work in .NET Framework 4.7.2 and Unity 2019.4.

### ❌ DO NOT USE (Confirmed Incompatible)
- **C# 8.0 Default interface methods** - Requires .NET Core runtime
- **C# 10+ Features** - Not tested with net472 IL emission

## Example: Null-Safe API Design

```csharp
// ✅ GOOD: Nullable reference types + init properties
public class WorldGenerator
{
    private readonly int worldSeed;
    private readonly FastNoiseLite? customNoise;  // null explicitly allowed
    
    public WorldGenerator(int seed, FastNoiseLite? noise = null)
    {
        this.worldSeed = seed;
        this.customNoise = noise;
    }
    
    public BiomeType GetBiome(WorldPosition position)
    {
        // Compiler enforces null check before use
        var noise = this.customNoise ?? CreateDefaultNoise();
        // ...
    }
}

// ✅ GOOD: Record struct for immutable data
public readonly record struct WorldPosition(float X, float Z);

// ✅ GOOD: init-only properties
public class BiomeResult
{
    public BiomeType Biome { get; init; }
    public float Confidence { get; init; }
}
```

## Validation Strategy

**Phase 1: Library Development (Current)**
- Develop with C# 9.0 + nullable enabled
- Include IsExternalInit polyfill immediately
- Avoid reference type records entirely (use readonly classes)
- Record structs are EXPERIMENTAL - test early

**Phase 2: Unity Integration Testing (Critical)**
- Build BepInEx plugin that references WorldZones.WorldGen.dll
- Load in actual Valheim (Unity 2019.4 runtime)
- If Unity throws TypeLoadException or MissingMethodException on init/record:
  - Fallback to C# 7.3 for affected types
  - Document incompatibility in this file

**Phase 3: Continuous Validation**
- Every merge to main should test in-game
- If Unity compatibility breaks, revert language features

## References

- [C# 8.0 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-8)
- [C# 9.0 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-9)
- [Unity Scripting Restrictions](https://docs.unity3d.com/2019.4/Documentation/Manual/ScriptingRestrictions.html)
