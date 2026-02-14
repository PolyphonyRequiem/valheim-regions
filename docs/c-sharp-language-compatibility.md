# C# Language Compatibility Guide - BepInEx Mod Context

**Target Framework**: .NET Framework 4.7.2  
**Language Version**: C# 9.0  
**Runtime**: Unity 2019.4 via BepInEx mod loading  
**Philosophy**: Optimistic but cautious - use modern features, but architect for fallback

## Strategy

**"Try It, Rewrite If Needed"**

We use C# 9 features for cleaner, safer code BUT design architecture so no feature is load-bearing. If runtime issues appear, we can mechanically translate:

- Records → manual structs/classes with Equals/GetHashCode
- `init` properties → `private set` + constructor assignment  
- Nullable annotations → remove (keep the null-avoidance design)

**DO:** Write with modern features for quality of life  
**DON'T:** Design APIs that *require* C# 9 to function  
**WHEN BLOCKED:** Rewrite affected types to C# 7.3 equivalent

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
- **init-only properties** - `{ get; init; }` - ✅ CONFIRMED WORKING (used in CoordinateRegion)
- **Target-typed new** - `List<string> list = new();` - Should work (compiler feature)
- **Pattern matching improvements** - Relational patterns, logical patterns - Should work (IL patterns)

### ⚠️ C# 9.0 Features (Claimed but Actually C# 10)
- **Records (reference types)** - `public record WorldConfig(int Seed);` - ❌ C# 10 ONLY
- **Record structs** - `public readonly record struct WorldPosition(float X, float Z);` - ❌ C# 10 ONLY
- **Workaround:** Use `readonly struct` with `init` properties instead (same immutability, slightly more verbose)

## IsExternalInit Polyfill (Already Included)

Located at `src/WorldZones.WorldGen/IsExternalInit.cs` - this internal type allows C# 9 init/record features to work on .NET Framework 4.7.2. No assembly conflicts since it's internal to our DLL.

## Restricted Features

### ❌ C# 10+ Features (NOT AVAILABLE in C# 9)
**Empirically discovered during Phase 2 implementation:**
- **File-scoped namespaces** - `namespace Foo;` (without braces) - Requires LangVersion 10.0+
- **Record structs** - `record struct Point(float X, float Z);` - Requires LangVersion 10.0+
- **Reference type records** - `record Person(string Name);` - Requires LangVersion 10.0+ (despite online claims they're C# 9)

**Microsoft's documentation can be misleading** - record structs are sometimes listed as C# 9, but the compiler requires C# 10.

### ❌ Runtime Incompatible
- **C# 8.0 Default interface methods** - Requires .NET Core/.NET 5+ runtime (not available in .NET Framework 4.7.2)

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

**Phase 1-3: Library Development**
- Use C# 9.0 features freely (init properties, nullable reference types, pattern matching)
- IsExternalInit polyfill already included
- **AVOID** features that claim to be C# 9 but actually require C# 10 (see Restricted Features)
- Build and test library in isolation

**Phase 2 Validation Results (2026-02-14):**
- ✅ `readonly struct` with `init` properties - Works perfectly
- ✅ `this.` qualification throughout - Compiles cleanly
- ✅ Nullable reference types enabled - No issues
- ❌ Record structs - Compiler rejected (CS8773: requires C# 10)
- ❌ File-scoped namespaces - Compiler rejected (CS8773: requires C# 10)
- **Fallback:** Converted to `readonly struct` + manual constructor - trivial change, validates "optimistic but cautious" strategy

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
