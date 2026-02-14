# Project Owner Style Guide

**Purpose:** Capture coding style preferences observed from Townsharp and stated preferences.

**Last Updated:** 2026-02-14

---

## Code Style Observations from Townsharp

### Field Naming & Qualification
```csharp
// ✅ OBSERVED PATTERN
private readonly BotCredential botCredential;  // lowercase field, NO underscore
this.botCredential = botCredential;            // ALWAYS use this. qualification
```

**Rules:**
- Fields: `lowercase` (NOT `_lowercase`)
- Always use `this.` qualification for field access
- Properties/classes: `PascalCase`

### Immutability by Default
```csharp
// ✅ OBSERVED: readonly fields
private readonly BotCredential botCredential;
private readonly ILoggerFactory loggerFactory;
private readonly BotTokenProvider botTokenProvider;
```

**Rules:**
- Default to `readonly` for fields
- Minimize mutable state

### Constructor Dependency Injection
```csharp
// ✅ OBSERVED: Constructor injection with immediate readonly assignment
internal protected BotClientBuilder(
    BotCredential botCredential, 
    ILoggerFactory loggerFactory, 
    IHttpClientFactory httpClientFactory)
{
    this.botCredential = botCredential;
    this.loggerFactory = loggerFactory;
    this.httpClientFactory = httpClientFactory;
    // ...
}
```

**Rules:**
- Constructor-based DI
- Assign to readonly fields immediately
- Use this. qualification in constructor

### Expression-Bodied Members
```csharp
// ✅ OBSERVED: Expression-bodied for simple returns
public ISubscriptionClient BuildSubscriptionClient(int concurrentConnections = 1) 
    => SubscriptionMultiplexer.Create(this.subscriptionClientFactory, this.loggerFactory, concurrentConnections);
```

**Rules:**
- Use `=>` for single-expression methods
- Prefer expression-bodied members over statement bodies when simple

### Access Modifiers
```csharp
// ✅ OBSERVED: Explicit access modifiers
internal protected BotClientBuilder(...) // Explicit combination
private readonly BotCredential botCredential;
public ISubscriptionClient BuildSubscriptionClient(...)
```

**Rules:**
- Always explicit (never rely on defaults)
- Use `internal protected` when needed for controlled visibility

---

## Stated Preferences (from Constitution)

### Functional Style
- Prefer pure functions
- Use LINQ over imperative loops (unless performance-critical)
- Leverage C# 7.3 features: tuples, pattern matching, local functions

### Thread Safety
- Design for immutability
- Thread-safe by default
- Document exceptions with comments

### Performance
- Functional style preferred
- Only optimize when profiling shows measurable impact

---

## Anti-Patterns (Warning from Owner)

**Owner Statement (2026-02-14):** *"I made a lot of bad choices here"* + **"I give you consent to critique the code that I gave you as reference btw if you think I've given BAD guidance here."**

**Critical Understanding:**
- Owner WANTS critical analysis, not blind copying
- Townsharp is a reference for style, NOT gospel for design
- Challenge patterns that don't make sense
- Modern/simpler alternatives preferred over complex patterns

**Watch for:**
- ⚠️ Potential over-engineering (mentioned by owner)
- ⚠️ Complexity that could be simpler
- ⚠️ Builder pattern with 8+ overloads (could use optional params or fluent API)
- Need to balance DI patterns with simplicity bias

---

## Recent Commits Analysis (August 2024)

### Builder Pattern Observations (Builders.cs - commit 4383fad)
```csharp
// ✅ OBSERVED: Static factory methods with method overloads
public static class Builders
{
   // Nested sealed class for internal implementation
   internal sealed class DefaultHttpClientFactory : IHttpClientFactory, IDisposable
   {
      private readonly Lazy<HttpMessageHandler> _handlerLazy = new(() => new HttpClientHandler());
      // ⚠️ NOTE: Uses _underscore in nested class (inconsistent with main pattern)
   }
   
   // Multiple overloads for flexibility
   public static BotClientBuilder CreateBotClientBuilder(BotCredential botCredential)
   public static BotClientBuilder CreateBotClientBuilder(BotCredential botCredential, ILoggerFactory loggerFactory)
   // ... more overloads
}
```

**Patterns:**
- Static factory methods for builders
- Method overloading for optional dependencies (⚠️ **CRITIQUE:** 8+ overloads is excessive - C# optional params or fluent builder would be cleaner)
- Nested sealed classes for internal implementations
- **Inconsistency note:** Nested class uses `_underscore` prefix (may be older code or .NET convention - avoid in WorldZones per owner preference)

### BotClientBuilder Pattern (commit 4383fad)
```csharp
public class BotClientBuilder
{
   private readonly BotCredential botCredential;
   private readonly ILoggerFactory loggerFactory;
   
   internal protected BotClientBuilder(...)  // Restricted construction
   {
      this.botCredential = botCredential;
      this.loggerFactory = loggerFactory;
   }
   
   public ISubscriptionClient BuildSubscriptionClient(...) 
      => SubscriptionMultiplexer.Create(...);  // Expression-bodied
}
```

**Confirmed Patterns:**
- `internal protected` for controlled instantiation (builder only via factory)
- Expression-bodied members for simple returns
- All fields readonly and lowercase
- Consistent `this.` qualification

---

## Anti-Patterns (Warning from Owner)

**From Townsharp:** Owner mentioned "I made a lot of bad choices here" - need to identify these during code review and avoid repeating.

**Watch for:**
- ⚠️ Potential over-engineering (he mentioned this)
- ⚠️ Complexity that could be simpler
- Need to balance DI patterns with simplicity bias

---

## Application to WorldZones

### For WorldGenerator Class
```csharp
// Apply Dan's style
public class WorldGenerator
{
    private readonly string seed;
    private readonly FastNoiseLite noise;
    
    public WorldGenerator(string seed)
    {
        this.seed = seed;
        this.noise = new FastNoiseLite(seed.GetHashCode());
    }
    
    public BiomeType GetBiome(float x, float z) 
        => this.CalculateBiome(x, z); // Expression-bodied
}
```

### Struct Example (WorldPosition)
```csharp
public readonly struct WorldPosition
{
    public float X { get; }
    public float Z { get; }
    
    public WorldPosition(float x, float z)
    {
        this.X = x;
        this.Z = z;
    }
}
```

---

## .editorconfig Enforcement

```ini
# Enforce style preferences
[*.cs]

# this. qualification (CRITICAL)
dotnet_style_qualification_for_field = true:warning
dotnet_style_qualification_for_property = true:warning
dotnet_style_qualification_for_method = true:warning
dotnet_style_qualification_for_event = true:warning

# Naming (lowercase fields, NO underscores)
dotnet_naming_rule.private_fields_lowercase.severity = warning
dotnet_naming_rule.private_fields_lowercase.symbols = private_fields
dotnet_naming_rule.private_fields_lowercase.style = lowercase_style

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.lowercase_style.capitalization = camel_case
dotnet_naming_style.lowercase_style.required_prefix = 

# readonly preference
csharp_prefer_readonly_field = true:warning

# Expression-bodied members
csharp_style_expression_bodied_methods = when_simple:suggestion
csharp_style_expression_bodied_properties = true:suggestion
```

---

**Next Steps:**
- Create .editorconfig with these rules (T007)
- Apply style consistently throughout WorldZones
- Update this document as more preferences are discovered
