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

### ⚠️ Use With Caution
- **C# 9.0 Records (reference types)** - Emit runtime attributes Unity may not have
  - **Alternative**: Use record structs or manual readonly classes

### ❌ DO NOT USE
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

## Validation

- All IL must load in Unity 2019.4 runtime (.NET Framework 4.7.2)
- Test early with actual game integration to catch runtime issues
- If Unity complains about attributes or IL opcodes, fallback to C# 7.3 equivalent

## References

- [C# 8.0 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-8)
- [C# 9.0 Language Reference](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-9)
- [Unity Scripting Restrictions](https://docs.unity3d.com/2019.4/Documentation/Manual/ScriptingRestrictions.html)
