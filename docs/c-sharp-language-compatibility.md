# C# Language Compatibility Guide - BepInEx Mod Context

**Target Framework**: .NET Framework 4.7.2  
**Language Version**: C# 9.0  
**Runtime**: Unity 2019.4 via BepInEx mod loading

## Rationale

**CRITICAL DISTINCTION:** BepInEx mods compile OUTSIDE Unity using standard Roslyn compiler, then load as DLLs at runtime. This means:

- ✅ We can use ANY C# 9 features that emit .NET Framework 4.7.2-compatible IL
- ✅ Unity's C# 7.x compiler limitations DON'T apply to us
- ✅ Only runtime compatibility matters (can the .NET 4.7.2 runtime execute our IL?)

C# 9 records and init properties are **compiler features** that emit standard IL. With the `IsExternalInit` polyfill, they work perfectly on .NET Framework 4.7.2 runtime.

## Fully Supported Features (C# 8-9)

### ✅ C# 8.0 Features
- **Nullable reference types** (`string?`, `string!`) - Compiler annotations only, zero runtime impact
- **Pattern matching enhancements** - Just IL patterns
- **Using declarations** - `using var file = ...`
- **Static local functions**
- **Null-coalescing assignment** - `??=`

### ✅ C# 9.0 Features (With IsExternalInit Polyfill)
- **Records (reference types)** - `public record WorldConfig(int Seed);` - FULLY SUPPORTED
- **Record structs** - `public readonly record struct WorldPosition(float X, float Z);` - FULLY SUPPORTED
- **init-only properties** - `{ get; init; }` - FULLY SUPPORTED
- **Target-typed new** - `List<string> list = new();`
- **Pattern matching improvements** - Relational patterns, logical patterns

## IsExternalInit Polyfill (Already Included)

Located at `src/WorldZones.WorldGen/IsExternalInit.cs` - this internal type allows C# 9 init/record features to work on .NET Framework 4.7.2. No assembly conflicts since it's internal to our DLL.

## Restricted Features

### ❌ DO NOT USE
- **C# 8.0 Default interface methods** - Requires .NET Core/.NET 5+ runtime (not available in .NET Framework 4.7.2)
- **C# 10+ Features** - Untested with net472, likely incompatible

## Example: Using C# 9 Features Safely

```csharp
// ✅ EXCELLENT: Record for immutable value type
public readonly record struct WorldPosition(float X, float Z);

// ✅ EXCELLENT: Record class for immutable reference type
public record WorldConfig(int Seed, float WorldRadius);

// ✅ EXCELLENT: Init-only properties with null safety
public class BiomeResult
{
    public required BiomeType Biome { get; init; }
    public required float Confidence { get; init; }
}

// ✅ EXCELLENT: Nullable reference types for explicit null handling
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
        var noise = this.customNoise ?? this.CreateDefaultNoise();
        // ...
    }
}
```

## Validation Strategy

**Phase 1-3: Library Development (Current)**
- Use C# 9.0 features freely (records, init, nullable reference types)
- IsExternalInit polyfill already included
- Build and test library in isolation

**Phase 4: BepInEx Integration Testing**
- Create minimal BepInEx plugin that references WorldZones.WorldGen.dll
- Load in actual Valheim and call library methods
- Monitor for TypeLoadException or MissingMethodException
- **Expected outcome:** Everything works (C# 9 features are runtime-compatible)

**Phase 5: Continuous Validation**
- Every major change: quick in-game smoke test
- If runtime issues appear, document and fallback to C# 7.3 equivalent

## References

- [C# 8.0 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-8)
- [C# 9.0 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-9)
- [Unity Scripting Restrictions](https://docs.unity3d.com/2019.4/Documentation/Manual/ScriptingRestrictions.html)
