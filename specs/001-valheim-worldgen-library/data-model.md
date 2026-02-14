# Data Model: Valheim World Generator Library

**Phase 1 Output** | **Date**: 2026-02-14  
**Purpose**: Define core entities, their fields, relationships, and validation rules

---

## Core Entities

### 1. WorldGenerator

**Purpose**: Main entry point for world generation. Initialized with a seed string and provides query methods for height and biome data.

**Fields**:
- `seed: string` - World seed string used to initialize random number generators
- `offset0: int` - Derived seed offset for base heightmap noise layer
- `offset1: int` - Derived seed offset for biome placement (Perlin noise variation)
- `offset2: int` - Derived seed offset for Black Forest biome placement
- `offset3: int` - Derived seed offset for detailed terrain features
- `offset4: int` - Derived seed offset for Mistlands biome placement
- `minMountainDistance: float` - Minimum distance from origin where mountains can generate (from seed)

**Relationships**:
- Uses `PerlinNoise` class for noise generation
- Uses `MathUtils` for mathematical operations
- Returns `BiomeType` enum values

**Validation Rules**:
- Seed string MUST NOT be null or empty
- Seed string MAY contain any UTF-8 characters
- Offsets are deterministically derived from seed using hash function
- Same seed always produces same offset values (deterministic)

**State Transitions**: Immutable after construction - all fields readonly

---

### 2. BiomeType (Enum)

**Purpose**: Represents the environmental zone types present in Valheim

**Values**:
```csharp
public enum BiomeType
{
    None = 0,
    Meadows = 1,
    BlackForest = 2,
    Swamp = 4,
    Mountain = 8,
    Plains = 16,
    Ocean = 256,
    Mistlands = 512,
    AshLands = 1024,      // Note: "AshLands" in decompiled source
    DeepNorth = 2048
}
```

**Validation Rules**:
- Enum uses flags pattern (powers of 2) matching Valheim's internal representation
- Valid for bitwise operations (e.g., biome masks)
- String representations match game's internal names

**Notes**: 
- Enum values match Heightmap.Biome from decompiled source
- "None" represents uninitialized or invalid biome

---

### 3. CoordinateRegion (Struct)

**Purpose**: Defines a rectangular region of world coordinates for batch queries or export

**Fields**:
- `minX: float` - Minimum X coordinate (western boundary)
- `minZ: float` - Minimum Z coordinate (southern boundary)
- `maxX: float` - Maximum X coordinate (eastern boundary)
- `maxZ: float` - Maximum Z coordinate (northern boundary)

**Relationships**:
- Used by batch query methods
- Used by PNG export functionality

**Validation Rules**:
- `maxX` MUST be greater than `minX`
- `maxZ` MUST be greater than `minZ`
- Region size `(maxX - minX) * (maxZ - minZ)` SHOULD NOT exceed 25,000,000 units² (5000x5000) for performance
- Coordinates MAY be negative (world origin is 0,0)

**Derived Properties**:
- `Width => maxX - minX`
- `Height => maxZ - minZ`
- `Area => Width * Height`
- `Center => ((minX + maxX) / 2, (minZ + maxZ) / 2)`

---

### 4. PerlinNoise (Static Class)

**Purpose**: Implements Perlin noise algorithm for procedural terrain generation

**Fields** (internal state):
- `permutation: int[]` - Permutation table for hash function (256 entries, doubled to 512)
- Seed-independent (uses standard permutation table from Perlin reference)

**Methods**:
- `Noise(double x, double y): float` - Generate 2D Perlin noise value [-1, 1] for given coordinates

**Validation Rules**:
- Input coordinates MAY be any valid double value
- Output MUST be in range [-1.0, 1.0]
- Same input coordinates always produce same output (deterministic)

**Implementation Notes**:
- Based on Ken Perlin's improved noise (2002)
- Uses gradient vectors and interpolation
- Double precision internally, returns float for compatibility

---

### 5. MathUtils (Static Class)

**Purpose**: Provides mathematical utility functions replacing Unity's Mathf class

**Methods**:
```csharp
static float Length(float x, float z)           // 2D distance from origin
static float Lerp(float a, float b, float t)   // Linear interpolation
static float LerpStep(float a, float b, float value) // Inverse lerp with clamping
static float SmoothStep(float a, float b, float t)   // Smooth interpolation
static float Clamp01(float value)              // Clamp to [0, 1]
static double Clamp01(double value)            // Clamp to [0, 1] (double)
static float Abs(float value)                  // Absolute value
```

**Validation Rules**:
- All methods are pure functions (no side effects)
- Thread-safe (stateless static methods)
- Handle edge cases (e.g., LerpStep when a == b)

---

## Data Flow

```
User Input (seed string)
    ↓
WorldGenerator Constructor
    ↓
Seed → Hash → Offsets (offset0-4, minMountainDistance)
    ↓
Query: GetBiome(x, z)
    ↓
├─→ GetBaseHeight(x, z) ────→ PerlinNoise.Noise() ──→ MathUtils
│                                                            ↓
└─→ Biome placement logic ──→ PerlinNoise.Noise() ────→ BiomeType
    (distance checks, noise thresholds)
    ↓
Return BiomeType enum
```

**Batch Query Flow**:
```
Query: GetBiomeMap(CoordinateRegion)
    ↓
Iterate over region (x, z pairs)
    ↓
Call GetBiome(x, z) for each coordinate
    ↓
Accumulate results in 2D array
    ↓
Return BiomeType[,] array
```

---

## Entity Relationships

```
WorldGenerator
    ├── depends on → PerlinNoise (static)
    ├── depends on → MathUtils (static)
    ├── returns → BiomeType (enum)
    └── accepts → CoordinateRegion (struct)

BiomeMapExporter (CLI tool)
    ├── depends on → WorldGenerator
    ├── depends on → ImageSharp (external)
    └── accepts → CoordinateRegion
```

---

## Validation Strategy

### Invariants

1. **Determinism**: Same seed + same coordinates MUST produce same results across executions
2. **Range validity**: Heights in range [-2.0, 2.0], noise in range [-1, 1]
3. **Biome consistency**: Adjacent coordinates should have coherent biome placement (no single-pixel islands)

### Test Data

**Reference Seeds**:
- "42" - Documented online map available
- "TestWorld" - Another reference seed
- Empty string - Edge case (should use default seed)
- Unicode seed - "世界の種" (internationalization test)

**Reference Coordinates**:
- Origin (0, 0) - Always Meadows biome
- Far distance (8000, 8000) - Black Forest or Ocean
- Mountain coordinates (varies by seed)
- Ocean coordinates (< sea level)

### State Validation

- WorldGenerator fields are readonly after construction
- No mutable state in static utility classes
- Thread-safe for concurrent queries (no shared mutable state)

---

## Performance Characteristics

| Operation | Complexity | Target Performance |
|-----------|-----------|-------------------|
| WorldGenerator construction | O(1) | <1ms |
| GetBiome(x, z) | O(1) | <1ms |
| GetHeight(x, z) | O(1) | <1ms |
| GetBiomeMap(region) | O(n) where n = region area | <1 minute for 10000x10000 |

**Memory usage**:
- WorldGenerator instance: ~100 bytes (offsets + fields)
- PerlinNoise permutation table: 2KB (static, shared)
- BiomeMap result: 1 byte per coordinate (BiomeType enum)

---

## Edge Cases

1. **Seed edge cases**:
   - Null seed → ArgumentNullException
   - Empty seed → Use empty string hash (valid)
   - Very long seed (>1000 chars) → Hash same as any string (deterministic)

2. **Coordinate edge cases**:
   - Origin (0, 0) → Valid, always Meadows
   - Extreme coordinates (±50000) → Valid, likely Ocean
   - Float.NaN or Float.Infinity → Undefined behavior (document as invalid input)

3. **Region edge cases**:
   - Inverted region (minX > maxX) → ArgumentException
   - Zero-size region → Returns empty array
   - Extremely large region → Performance warning, may timeout

---

## Constants (from Valheim)

```csharp
// Biome placement thresholds (from WorldGenerator.cs)
const float SEA_LEVEL_THRESHOLD = 0.02f;        // Below this = Ocean
const float MOUNTAIN_HEIGHT_THRESHOLD = 0.4f;   // Above this = Mountain
const float SWAMP_MIN_HEIGHT = 0.05f;
const float SWAMP_MAX_HEIGHT = 0.25f;

// Distance thresholds (from WorldGenerator.cs)
const float MIN_SWAMP_DISTANCE = 2000f;
const float MAX_SWAMP_DISTANCE = 6000f;         // maxMarshDistance in source
const float MIN_PLAINS_DISTANCE = 3000f;
const float MAX_PLAINS_DISTANCE = 8000f;
const float MIN_MISTLANDS_DISTANCE = 6000f;
const float MAX_MISTLANDS_DISTANCE = 10000f;
const float MIN_BLACKFOREST_DISTANCE = 600f;
const float MAX_BLACKFOREST_DISTANCE = 6000f;
const float WORLD_EDGE_DISTANCE = 10500f;       // Beyond this = deep ocean

// Noise thresholds (from WorldGenerator.cs)
const float SWAMP_NOISE_THRESHOLD = 0.6f;
const float PLAINS_NOISE_THRESHOLD = 0.4f;
const float MISTLANDS_NOISE_THRESHOLD = 0.4f;   // minDarklandNoise in source
const float BLACKFOREST_NOISE_THRESHOLD = 0.4f;
```

These constants are used in GetBiome() algorithm and must match decompiled source exactly.
