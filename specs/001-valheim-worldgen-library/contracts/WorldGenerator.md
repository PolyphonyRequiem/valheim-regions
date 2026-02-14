# API Contract: WorldGenerator

**Package**: `WorldZones.WorldGen`  
**Version**: 0.1.0 (initial)  
**Stability**: Pre-release

---

## Class: WorldGenerator

**Namespace**: `WorldZones.WorldGen`

**Purpose**: Main entry point for Valheim world generation. Provides deterministic heightmap and biome data for any world coordinate based on a seed string.

**Thread Safety**: ✅ Thread-safe for query operations (GetBiome, GetHeight) after construction. Immutable internal state.

---

## Constructor

### `WorldGenerator(string seed)`

Initializes a new world generator with the specified seed.

**Parameters**:
- `seed` (string): World seed string. Must not be null.

**Throws**:
- `ArgumentNullException`: If seed is null

**Behavior**:
- Computes deterministic offset values from seed hash
- Same seed always produces identical offset values
- Empty string is valid (uses hash of empty string)

**Performance**: O(1), typically <1ms

**Example**:
```csharp
var generator = new WorldGenerator("TestSeed123");
```

---

## Public Methods

### `GetBiome(float x, float z)`

Returns the biome type at the specified world coordinates.

**Parameters**:
- `x` (float): X-axis world coordinate (east-west)
- `z` (float): Z-axis world coordinate (north-south)

**Returns**: `BiomeType` - The biome at the given coordinates

**Throws**: None (all inputs are valid)

**Behavior**:
- Deterministic: same coordinates always return same biome
- Uses base height, distance from origin, and Perlin noise
- Matches Valheim's biome placement algorithm exactly
- Handles edge cases (world boundaries, ocean, mountains)

**Performance**: O(1), target <1ms per call

**Example**:
```csharp
var biome = generator.GetBiome(1500.0f, 2000.0f);
if (biome == BiomeType.Meadows)
{
    Console.WriteLine("Starting area biome");
}
```

**Edge Cases**:
- Origin (0, 0): Always returns `BiomeType.Meadows`
- Beyond ±10500 units: Returns `BiomeType.Ocean` (deep ocean)
- Float.NaN or Float.Infinity: Undefined behavior (invalid input)

---

### `GetBiome(float x, float z, float oceanLevel, bool waterAlwaysOcean)`

Overload with custom ocean level threshold and ocean-forcing option.

**Parameters**:
- `x` (float): X-axis world coordinate
- `z` (float): Z-axis world coordinate  
- `oceanLevel` (float): Height threshold below which coordinates are considered ocean (default: 0.02)
- `waterAlwaysOcean` (bool): If true, any coordinate at/below ocean level returns Ocean biome (default: false)

**Returns**: `BiomeType` - The biome at the given coordinates

**Throws**: None

**Behavior**:
- Same as primary GetBiome() but with custom thresholds
- `waterAlwaysOcean=true` ensures no land below sea level (overrides swamp logic)
- Default values match Valheim's standard parameters

**Performance**: O(1), target <1ms per call

**Example**:
```csharp
// Check if coordinate would be underwater with custom sea level
var biome = generator.GetBiome(x, z, oceanLevel: 0.05f, waterAlwaysOcean: true);
```

---

### `GetHeight(float x, float z)`

Returns the terrain height at the specified world coordinates.

**Parameters**:
- `x` (float): X-axis world coordinate
- `z` (float): Z-axis world coordinate

**Returns**: `float` - Normalized height value

**Throws**: None

**Behavior**:
- Returns base height before biome-specific modifications
- Range approximately [-2.0, 2.0] (normalized)
- Values below 0.02 are typically ocean
- Values above 0.4 are typically mountains
- Deterministic for same coordinates

**Performance**: O(1), target <1ms per call

**Example**:
```csharp
float height = generator.GetHeight(500f, 750f);
if (height > 0.4f)
{
    Console.WriteLine("Mountain terrain detected");
}
```

**Notes**:
- Height values are normalized, not in Valheim's internal units
- Use GetBiome() for actual biome determination (accounts for all placement rules)

---

### `GetBiomeMap(CoordinateRegion region)`

Queries biome data for a rectangular region, returning a 2D array.

**Parameters**:
- `region` (CoordinateRegion): The rectangular region to query

**Returns**: `BiomeType[,]` - 2D array indexed as `[x, z]` where indices correspond to world coordinates within region

**Throws**:
- `ArgumentException`: If region is invalid (maxX <= minX or maxZ <= minZ)

**Behavior**:
- Iterates over every integer coordinate in region
- Array dimensions: `[width, height]` where width = (maxX - minX), height = (maxZ - minZ)
- Each element is the biome at that coordinate
- Coordinates are sampled at integer boundaries

**Performance**: O(n) where n = region area, target <1 minute for 10000x10000 region

**Example**:
```csharp
var region = new CoordinateRegion
{
    minX = -5000f,
    minZ = -5000f,
    maxX = 5000f,
    maxZ = 5000f
};

BiomeType[,] biomeMap = generator.GetBiomeMap(region);

// Access biome at world coordinate (100, 200) - need to offset by region min
int arrayX = (int)(100f - region.minX);
int arrayZ = (int)(200f - region.minZ);
BiomeType biome = biomeMap[arrayX, arrayZ];
```

**Performance Warning**: Large regions (>5000x5000) may take significant time. Consider using progress callbacks for UI applications (future enhancement).

---

## Properties

### `Seed` (get-only)

Returns the seed string used to initialize this generator.

**Type**: `string`

**Example**:
```csharp
string originalSeed = generator.Seed;
```

---

## Enumerations

### `BiomeType`

```csharp
[Flags]
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
    AshLands = 1024,
    DeepNorth = 2048
}
```

**Usage**:
- Individual biome values (powers of 2 for flags pattern)
- Can be used with bitwise operations for biome masks
- String representation matches Valheim's internal naming

---

## Structures

### `CoordinateRegion`

```csharp
public struct CoordinateRegion
{
    public float minX;
    public float minZ;
    public float maxX;
    public float maxZ;
    
    public float Width => maxX - minX;
    public float Height => maxZ - minZ;
}
```

**Validation**:
- `maxX` must be greater than `minX`
- `maxZ` must be greater than `minZ`

---

## Usage Examples

### Basic World Query

```csharp
using WorldZones.WorldGen;

var generator = new WorldGenerator("MySeedName");

// Query single coordinate
BiomeType biome = generator.GetBiome(1500f, 2000f);
float height = generator.GetHeight(1500f, 2000f);

Console.WriteLine($"Biome: {biome}, Height: {height:F3}");
```

### Batch Query for Region

```csharp
var region = new CoordinateRegion
{
    minX = 0f,
    minZ = 0f,
    maxX = 1000f,
    maxZ = 1000f
};

BiomeType[,] map = generator.GetBiomeMap(region);

// Count biomes in region
var biomeCounts = new Dictionary<BiomeType, int>();
for (int x = 0; x < map.GetLength(0); x++)
{
    for (int z = 0; z < map.GetLength(1); z++)
    {
        BiomeType biome = map[x, z];
        biomeCounts[biome] = biomeCounts.GetValueOrDefault(biome) + 1;
    }
}

foreach (var kvp in biomeCounts)
{
    Console.WriteLine($"{kvp.Key}: {kvp.Value} coordinates");
}
```

### Determinism Verification

```csharp
// Verify same seed produces identical results
var gen1 = new WorldGenerator("TestSeed");
var gen2 = new WorldGenerator("TestSeed");

for (int i = 0; i < 1000; i++)
{
    float x = i * 10f;
    float z = i * 15f;
    
    BiomeType biome1 = gen1.GetBiome(x, z);
    BiomeType biome2 = gen2.GetBiome(x, z);
    
    Debug.Assert(biome1 == biome2, "Determinism violated!");
}

Console.WriteLine("Determinism verified for 1000 coordinates");
```

---

## Performance Contracts

| Operation | Complexity | Target Time | Memory |
|-----------|-----------|-------------|---------|
| Constructor | O(1) | <1ms | ~100 bytes |
| GetBiome(x, z) | O(1) | <1ms | 0 (no allocation) |
| GetHeight(x, z) | O(1) | <1ms | 0 (no allocation) |
| GetBiomeMap(1000x1000) | O(n) | <1 second | 1 MB |
| GetBiomeMap(10000x10000) | O(n) | <1 minute | 100 MB |

**Notes**:
- Query methods are allocation-free (no GC pressure)
- Batch queries allocate single result array
- No internal caching (stateless per-call computation)

---

## Breaking Changes Policy

**Current Version**: 0.1.0 (pre-release)

**Pre-1.0 Breaking Changes**: May occur without deprecation warnings

**Post-1.0 Breaking Changes**: 
- Deprecated one MINOR version before removal
- Migration guide provided
- `[Obsolete]` attribute with guidance

**Compatible Changes** (MINOR version bump):
- New methods/properties
- New BiomeType values (if Valheim adds biomes)
- Performance improvements
- Bug fixes maintaining behavior

**Incompatible Changes** (MAJOR version bump):
- Signature changes to existing methods
- Removal of methods/properties
- Behavior changes breaking determinism

---

## Validation & Testing

### Contract Tests Required

All public APIs must have tests verifying:
1. **Determinism**: Same input → same output across multiple runs
2. **Range validity**: Return values within documented ranges
3. **Error handling**: Documented exceptions thrown correctly
4. **Performance**: Operations complete within target time
5. **Edge cases**: Boundary conditions handled correctly

### Reference Test Seeds

- "42" - Validated against online map generator
- "TestWorld" - Secondary reference seed
- "" (empty) - Edge case seed
- "世界の種" - Unicode seed test

---

## Dependencies

**Runtime**: 
- .NET Framework 4.7.2 or later
- No external package dependencies

**Testing**:
- NUnit 3.x (test framework)

---

## License

TBD - Specify project license

---

## Changelog

### 0.1.0 (2026-02-14) - Initial Design
- API contract defined
- Core methods: GetBiome, GetHeight, GetBiomeMap
- BiomeType enum with 9 biomes
- CoordinateRegion struct for batch queries
