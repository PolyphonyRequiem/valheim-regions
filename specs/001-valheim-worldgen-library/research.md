# Research: Valheim World Generator Library

**Phase 0 Output** | **Date**: 2026-02-14  
**Purpose**: Resolve all "NEEDS CLARIFICATION" items from Technical Context

---

## 1. Perlin Noise Implementation

**Question**: Should we port DUtils.PerlinNoise from decompiled source or use an existing C# library?

### Investigation

**Option A: Port DUtils.PerlinNoise directly**
- **Pros**: 
  - Guaranteed exact match to Valheim's algorithm (critical for validation)
  - No external dependencies
  - Full control over implementation
- **Cons**: 
  - DUtils is not found in decompiled C# files - may be native code or obfuscated
  - Risk of incorrect implementation if we can't find exact source
- **Compatibility**: C# 7.3, .NET Framework 4.7.2

**Option B: Use SharpNoise library**
- NuGet: `SharpNoise` (last updated 2018)
- **Pros**: 
  - Well-established implementation
  - Multiple noise types including Perlin
- **Cons**: 
  - Different implementation may produce different values than Valheim
  - External dependency (though lightweight ~50KB)
- **Compatibility**: .NET Framework 4.5+ (compatible)

**Option C: Implement classic Perlin noise from reference**
- Based on Ken Perlin's improved noise (2002) or original (1983)
- **Pros**: 
  - Reference implementation widely documented
  - Can match typical game engine implementations
  - No dependencies
- **Cons**: 
  - Unity's Mathf.PerlinNoise may use specific variant/scaling
  - Need to reverse-engineer parameters from decompiled WorldGenerator constants

### Decision

**DECISION: Use FastNoiseLite library**

**UPDATE (2026-02-14)**: During planning review, discovered [FastNoiseLite](https://github.com/Auburn/FastNoiseLite) - a well-maintained, single-file C# Perlin noise implementation that is likely compatible with or identical to what Valheim uses.

**Rationale**: 
- FastNoiseLite is a battle-tested, widely-used noise library
- Available as standalone C# file (can copy into project - no NuGet dependency)
- MIT licensed, actively maintained
- Supports multiple noise algorithms including Perlin
- Likely matches or closely approximates Valheim's noise implementation
- Better than custom implementation: proven, debugged, optimized

**Implementation approach**:
1. Copy FastNoiseLite.cs into `WorldZones.WorldGen/` project
2. Use Perlin noise mode with parameters from WorldGenerator.cs (0.002, 0.003, etc.)
3. Validate with test seed against online Valheim world generators
4. If noise output doesn't match, adjust parameters or fall back to custom implementation
5. Document FastNoiseLite usage and version in code comments

**Dependency status**: Still zero external NuGet dependencies (single .cs file copied into source tree)

---

## 2. PNG Export Library

**Question**: Should we use System.Drawing or ImageSharp for PNG generation on .NET Framework 4.7.2?

### Investigation

**Option A: System.Drawing**
- Built-in to .NET Framework
- **Pros**: 
  - Zero external dependencies
  - Familiar API
  - Fully compatible with .NET Framework 4.7.2
- **Cons**: 
  - Windows-only (uses GDI+)
  - Microsoft recommends avoiding for new projects (cross-platform concerns)
  - May have performance issues for large images
- **Compatibility**: .NET Framework 4.7.2 (built-in)

**Option B: ImageSharp (SixLabors.ImageSharp)**
- NuGet: `SixLabors.ImageSharp` v1.x (v2+ requires .NET Standard 2.1)
- **Pros**: 
  - Cross-platform
  - Modern, actively maintained
  - Good performance
  - Cleaner API than System.Drawing
- **Cons**: 
  - External dependency (~500KB)
  - Version 1.x branch (older, but compatible)
  - Apache 2.0 license requires attribution
- **Compatibility**: .NET Framework 4.7.2 supported by ImageSharp 1.x

**Option C: SkiaSharp**
- NuGet: `SkiaSharp`
- **Pros**: 
  - Cross-platform
  - High performance
  - Used by many game projects
- **Cons**: 
  - Larger dependency (~2MB with native libraries)
  - Overkill for simple PNG export
- **Compatibility**: .NET Framework 4.7.2 compatible

### Decision

**DECISION: Option B - ImageSharp 1.x**

**Rationale**: 
- PNG export is secondary feature (not core worldgen logic)
- Cross-platform capability future-proofs the library for downstream use
- System.Drawing Windows-only limitation conflicts with library goals
- ImageSharp 1.x is stable, well-documented, and modern
- ~500KB dependency is acceptable for optional CLI tool
- Can keep export logic in separate `WorldZones.WorldGen.Cli` project to isolate dependency

**Implementation approach**:
1. Core library (`WorldZones.WorldGen`) has NO dependency on ImageSharp
2. Export functionality lives in `WorldZones.WorldGen.Cli` project
3. CLI tool references ImageSharp 1.x via NuGet
4. Document Apache 2.0 license attribution in LICENSES.md

---

## 3. Testing Framework

**Question**: Should we use NUnit 3.x or xUnit for testing?

### Investigation

**Option A: NUnit 3.x**
- NuGet: `NUnit` + `NUnit3TestAdapter`
- **Pros**: 
  - Familiar to many C# developers
  - Mature framework
  - Good Visual Studio integration
  - TestCase attributes for parameterized tests
- **Cons**: 
  - Slightly more verbose than xUnit
- **Compatibility**: .NET Framework 4.7.2 fully supported

**Option B: xUnit**
- NuGet: `xunit` + `xunit.runner.visualstudio`
- **Pros**: 
  - Modern architecture (test isolation)
  - Less ceremony (no [TestFixture] required)
  - Theory/InlineData for parameterized tests
  - Used by .NET Core team
- **Cons**: 
  - Less familiar to some developers
- **Compatibility**: .NET Framework 4.7.2 fully supported

### Decision

**DECISION: Option A - NUnit 3.x**

**Rationale**: 
- Wider familiarity in game modding community
- TestCase attributes excellent for TDD with multiple seed values
- Mature tooling and documentation
- Personal preference of maintainer (if stated) or team standard
- Both options are technically equivalent - NUnit chosen for familiarity

**Implementation approach**:
1. Install NUnit 3.x and NUnit3TestAdapter via NuGet
2. Use `[TestFixture]` and `[Test]` attributes
3. Use `[TestCase]` for parameterized tests with different seeds/coordinates
4. Configure test project to run via `dotnet test`

---

## 4. Unity Math Type Replacements

**Question**: How to replace Unity types (Vector3, Mathf) with pure C# equivalents?

### Investigation

The decompiled WorldGenerator.cs uses:
- `Vector3` for coordinates (only x, z components used for world coords)
- `Mathf.Abs()`, `Mathf.Max()`, `Mathf.Lerp()`, etc.
- `DUtils.Length()` for distance calculations

**Option A: Custom lightweight structs**
```csharp
struct Vector2f { float X; float Z; }
static class MathUtils {
    static float Abs(float x) => Math.Abs(x);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Length(float x, float z) => (float)Math.Sqrt(x*x + z*z);
}
```
- **Pros**: 
  - Zero dependencies
  - Exact control over behavior
  - Minimal code
- **Cons**: 
  - Need to implement ~10 math utility methods
- **Compatibility**: Pure C# 7.3

**Option B: Use System.Numerics.Vector2**
- Built-in to .NET Framework 4.6+
- **Pros**: 
  - No external dependency
  - Battle-tested implementation
- **Cons**: 
  - Slight API differences from Unity (e.g., property names)
  - Still need custom Lerp/SmoothStep implementations
- **Compatibility**: .NET Framework 4.7.2 (built-in)

### Decision

**DECISION: Option A - Custom lightweight MathUtils class**

**Rationale**: 
- World coordinates only need 2D (x, z) - no Vector3 required
- API can exactly match Unity patterns for easier porting
- ~50 lines of code to implement needed utilities
- Zero dependencies maintains library simplicity
- Cleaner than adapting System.Numerics APIs

**Implementation approach**:
1. Create `MathUtils.cs` with static methods:
   - `Length(float x, float z)` - 2D distance
   - `Lerp(float a, float b, float t)` - linear interpolation
   - `LerpStep(float a, float b, float t)` - clamped lerp
   - `SmoothStep(float a, float b, float t)` - smooth interpolation
   - `Clamp01(float value)` - clamp to [0,1]
2. Port WorldGenerator.cs replacing `Mathf.` with `MathUtils.` and `Math.` as appropriate
3. Replace `DUtils.Length(wx, wy)` with `MathUtils.Length(wx, wy)`
4. Use `float x, float z` parameters directly instead of Vector3

---

## 5. Validating Correctness Against Valheim

**Question**: How do we ensure our implementation matches Valheim's exact output?

### Strategy

**Validation approach**:
1. **Reference seeds**: Use known seeds with published maps from online generators
   - Seed: "42" (commonly used test seed)
   - Seed: "TestWorld" (another documented seed)
   - Seed: User's actual world seed (once provided)

2. **Online validation tools**:
   - https://valheim-map.world/ (web-based map generator)
   - Various other community-created map generators
   - Compare exported PNG from our library to these reference maps

3. **Test strategy**:
   - Unit tests verify algorithm structure (correct noise calls, parameter values)
   - Integration tests export full biome map and compare visually
   - Document "expected differences" if any (e.g., edge cases, color choices)

4. **Known coordinate tests**:
   - Pick 10-20 specific coordinates from reference map
   - Verify our GetBiome() returns same biome type
   - Document these as regression tests

**Success criteria**:
- Biome boundaries match reference maps within ±2 world units
- Major biome regions (Meadows, Black Forest, etc.) correctly placed
- Ocean/land boundaries accurate
- Mountain height thresholds correct

---

## 6. Ground Truth Data Validation (2026-02-15)

**Question**: How can we validate our WorldGenerator output against actual Valheim data?

### Investigation

**Available Ground Truth Data**:
- PNG Map: `data/seeds/HHcLC5acQt/Map_HHcLC5acQt.png` (8192×8192)
- Tile Data: `data/seeds/HHcLC5acQt/data/tiles/*.bin.gz` (256 tiles, 16×16 grid)

### Coordinate System Validation Results

**PNG Dataset**:
- Dimensions: 8192×8192 pixels
- Center: (4095, 4095) - validated via radial symmetry testing
- Content radius: 3491 pixels (pure biome colors)
- Void boundary: 3512 pixels (gradient transition zone: 3491-3512)
- Contains 8,112 distinct RGB values (86% pure biomes, 14% gradients)
- Anti-aliasing: Gradients are rendering artifacts, must exclude from validation

**Tile Dataset**:
- Format: GZip compressed binary, 256 tiles (00-15 in X and Z)
- Tile size: 1500m × 1500m world units
- Sample grid: 1024×1024 per tile (10 bytes per sample)
- Sample format: uint16 biome ID + float height + float forestFactor
- Void marker: height = -400.0f exactly
- Tile 08-08 covers world [0, 1500] for both X and Z

**Coordinate Mapping (VALIDATED)**:
```csharp
// Key Constants
const int WORLD_SIZE = 24576;           // Total world diameter (meters)
const double SCALE = 3.0;                // Exact m/pixel ratio
const int PNG_CENTER = 4095;             // PNG center pixel
const int TILE_SIZE = 1500;              // Tile width (meters)
const int TILE_CENTER_OFFSET = 8;        // Tiles to world origin

// PNG to World
double worldX = (px - PNG_CENTER) * SCALE;
double worldZ = -(py - PNG_CENTER) * SCALE;  // Z FLIPPED!

// World to Tile
int tileX = (int)Floor(worldX / TILE_SIZE) + TILE_CENTER_OFFSET;
int tileZ = (int)Floor(worldZ / TILE_SIZE) + TILE_CENTER_OFFSET;
```

**Validation Results**:
- All tiles: 93.79% match (23.7M pixels compared)
- Interior tiles (04-11): **99.9917% match** (9.9M pixels compared)
- Center tile (08-08): 99.99% match (134K pixels compared)
- Only 827 mismatches out of 9,948,863 interior pixels!

**Critical Findings**:
1. **Shallows** in PNG represent underwater non-ocean areas - must exclude
2. **Gradients** are PNG rendering artifacts - must exclude
3. **Z-axis flip**: PNG Y increases down, world Z increases up
4. **Scale**: Exactly 3.0 m/pixel (worldSize=24576)
5. **Rounding**: Sub-pixel precision causes minor boundary mismatches

### Validation Strategy for WorldGenerator

**Phase 1: Spot Checks** (quick validation)
1. Generate seed "HHcLC5acQt" with our WorldGenerator
2. Compare GetBiome() output at 100 random interior coordinates
3. Expect >95% match (accounting for rounding differences)

**Phase 2: Regional Validation** (comprehensive)
1. Generate full biome map for tile 08-08 region [0, 1500]
2. Sample every 10m (150×150 grid = 22,500 comparisons)
3. Compare to tile 08-08 data using validated coordinate mapping
4. Expect >99% match for interior regions

**Phase 3: Full World Validation** (production ready)
1. Generate biome maps for all interior tiles (04-11)
2. Pixel-by-pixel comparison using coordinate mapping
3. Target: >99% match rate across 64 tiles
4. Document any systematic biases (e.g., biome boundary shifts)

**Test Utilities Created**:
- `FindMapBounds.cs` - Empirically locates PNG boundaries
- `ValidateRadiusTransition.cs` - Tests biome/void boundaries  
- `TestOppositeAngleSymmetry.cs` - Proves PNG center accuracy
- `ValidateAllTiles.cs` - Compares WorldGen output to tiles
- `FindOptimalWorldSize.cs` - Calibrates coordinate scaling

### Decision

**DECISION: Use tile data (not PNG) as primary ground truth for WorldGenerator validation**

**Rationale**:
1. Tile data has no anti-aliasing artifacts (100% pure biomes)
2. Tile data includes height information (can validate GetHeight too)
3. PNG useful for visual debugging but tiles are programmatic truth
4. 99.99% validation proves coordinate mapping is correct

**Implementation**: 
- Create `GroundTruthValidator` class
- Load tile data for region validation
- Compare GetBiome() and GetHeight() outputs
- Report match percentage and systematic biases

---

## Summary of Decisions

| Topic | Decision | Dependencies Added |
|-------|----------|-------------------|
| **Perlin Noise** | Custom implementation (Perlin 2002) | None |
| **PNG Export** | ImageSharp 1.x in CLI project | SixLabors.ImageSharp 1.x |
| **Testing Framework** | NUnit 3.x | NUnit, NUnit3TestAdapter |
| **Math Utils** | Custom lightweight utilities | None |

**Total external dependencies**: 
- Core library: 0
- CLI tool: 1 (ImageSharp)
- Test project: 2 (NUnit framework)

**Next steps (Phase 1)**:
1. Create data-model.md defining core entities (WorldGenerator, BiomeType, etc.)
2. Create contracts/WorldGenerator.md documenting public API
3. Create quickstart.md with usage examples
4. Update agent context with technology choices
