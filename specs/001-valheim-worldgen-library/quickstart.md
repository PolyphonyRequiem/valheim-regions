# Quick Start: Valheim World Generator Library

**Target Audience**: Developers integrating the WorldGen library into their projects  
**Time to Complete**: 5 minutes  
**Prerequisites**: .NET Framework 4.7.2 or later

---

## Installation

### Option 1: NuGet Package (Future)

```powershell
# Not yet published - coming in v0.1.0 release
Install-Package WorldZones.WorldGen
```

### Option 2: Source Reference

```powershell
# Clone repository
git clone https://github.com/yourusername/worldzones.git
cd worldzones

# Add project reference to your .csproj
dotnet add reference src/WorldZones.WorldGen/WorldZones.WorldGen.csproj
```

---

## Basic Usage

### 1. Create a World Generator

```csharp
using WorldZones.WorldGen;

// Initialize with your world seed
var generator = new WorldGenerator("MySeedName");

// Or use actual Valheim world seed
var generator = new WorldGenerator("dG8gYmUgb3Igbm90IHRvIGJl");
```

**Important**: Use the exact seed from your Valheim world to get matching terrain!

---

### 2. Query Biome at a Location

```csharp
// Check biome at specific coordinates
float x = 1500f;  // East-west position
float z = 2000f;  // North-south position

BiomeType biome = generator.GetBiome(x, z);

Console.WriteLine($"Biome at ({x}, {z}): {biome}");
// Output: "Biome at (1500, 2000): Meadows"
```

**Coordinate System**: 
- Origin (0, 0) is at the world center
- X-axis: west (negative) to east (positive)
- Z-axis: south (negative) to north (positive)
- Units are Valheim world units (same as in-game coordinates)

---

### 3. Query Terrain Height

```csharp
float height = generator.GetHeight(x, z);

Console.WriteLine($"Height: {height:F3}");

if (height > 0.4f)
{
    Console.WriteLine("Mountain terrain!");
}
else if (height < 0.02f)
{
    Console.WriteLine("Ocean (below sea level)");
}
```

**Height Values**:
- Normalized range approximately [-2.0, 2.0]
- Ocean: < 0.02
- Land: 0.02 - 0.4
- Mountains: > 0.4

---

### 4. Generate Biome Map for Region

```csharp
// Define region to query (1000x1000 area around spawn)
var region = new CoordinateRegion
{
    minX = -500f,
    minZ = -500f,
    maxX = 500f,
    maxZ = 500f
};

// Get biome data for entire region
BiomeType[,] biomeMap = generator.GetBiomeMap(region);

// Access specific coordinate in map
int localX = (int)(100f - region.minX);  // Convert world coord to array index
int localZ = (int)(200f - region.minZ);
BiomeType biomeAt100_200 = biomeMap[localX, localZ];
```

**Performance Notes**:
- 1000x1000 region: ~1 second
- 5000x5000 region: ~30 seconds
- 10000x10000 region: ~1 minute

---

## Common Use Cases

### Use Case 1: Find Nearest Biome

```csharp
// Search outward from spawn point for specific biome
BiomeType targetBiome = BiomeType.BlackForest;
float searchRadius = 5000f;
float step = 50f;  // Check every 50 units

for (float radius = 0; radius < searchRadius; radius += step)
{
    // Check points in a circle at this radius
    for (float angle = 0; angle < 360; angle += 10)
    {
        float x = radius * MathF.Cos(angle * MathF.PI / 180f);
        float z = radius * MathF.Sin(angle * MathF.PI / 180f);
        
        BiomeType biome = generator.GetBiome(x, z);
        
        if (biome == targetBiome)
        {
            Console.WriteLine($"Found {targetBiome} at ({x:F0}, {z:F0})");
            return;
        }
    }
}
```

---

### Use Case 2: Analyze Biome Distribution

```csharp
// Count how many coordinates of each biome in a region
var region = new CoordinateRegion { minX = -2000f, minZ = -2000f, maxX = 2000f, maxZ = 2000f };
BiomeType[,] map = generator.GetBiomeMap(region);

var counts = new Dictionary<BiomeType, int>();

for (int x = 0; x < map.GetLength(0); x++)
{
    for (int z = 0; z < map.GetLength(1); z++)
    {
        BiomeType biome = map[x, z];
        counts[biome] = counts.GetValueOrDefault(biome, 0) + 1;
    }
}

int total = region.Width * region.Height;
foreach (var kvp in counts.OrderByDescending(k => k.Value))
{
    float percentage = (kvp.Value / (float)total) * 100f;
    Console.WriteLine($"{kvp.Key}: {percentage:F1}% ({kvp.Value} coordinates)");
}

// Output example:
// Ocean: 45.2% (7232 coordinates)
// Meadows: 23.1% (3696 coordinates)
// BlackForest: 15.3% (2448 coordinates)
// ...
```

---

### Use Case 3: Validate Against Reference Map

```csharp
// Test known coordinates from online map generator
var testCases = new (float x, float z, BiomeType expected)[]
{
    (0f, 0f, BiomeType.Meadows),        // Spawn always Meadows
    (8500f, 0f, BiomeType.Ocean),       // Far east = ocean
    (3000f, 3000f, BiomeType.Plains),   // (example, varies by seed)
};

var generator = new WorldGenerator("42");  // Known reference seed

foreach (var (x, z, expected) in testCases)
{
    BiomeType actual = generator.GetBiome(x, z);
    bool matches = actual == expected;
    
    Console.WriteLine($"({x}, {z}): Expected {expected}, Got {actual} [{(matches ? "✓" : "✗")}]");
}
```

---

## Export Biome Map as PNG (CLI Tool)

```powershell
# Using the CLI tool (once implemented)
cd src/WorldZones.WorldGen.Cli
dotnet run -- --seed "MySeed" --region -5000,-5000,5000,5000 --output myworld.png

# Output: myworld.png with color-coded biomes
```

**Color Legend** (TBD in implementation):
- Meadows: Green
- BlackForest: Dark Green
- Swamp: Brown
- Mountain: White/Gray
- Plains: Yellow
- Ocean: Blue
- Mistlands: Purple
- AshLands: Red
- DeepNorth: Light Blue

---

## Troubleshooting

### Issue: Biome doesn't match my Valheim world

**Solution**: Ensure you're using the EXACT seed string from your world.

1. In Valheim, press F5 to open console
2. Type `world` command to see world name and seed
3. Use the seed string exactly as shown (case-sensitive)

### Issue: GetBiomeMap is slow

**Solution**: Reduce region size or use coarser sampling:

```csharp
// Instead of querying every coordinate
BiomeType[,] map = generator.GetBiomeMap(region);

// Sample every 10th coordinate
int step = 10;
var sampledRegion = new CoordinateRegion
{
    minX = region.minX,
    minZ = region.minZ,
    maxX = region.maxX,
    maxZ = region.maxZ
};

int width = (int)((region.maxX - region.minX) / step);
int height = (int)((region.maxZ - region.minZ) / step);
BiomeType[,] sampledMap = new BiomeType[width, height];

for (int x = 0; x < width; x++)
{
    for (int z = 0; z < height; z++)
    {
        float worldX = region.minX + (x * step);
        float worldZ = region.minZ + (z * step);
        sampledMap[x, z] = generator.GetBiome(worldX, worldZ);
    }
}
```

### Issue: Height values seem wrong

**Solution**: Remember heights are normalized, not real Valheim terrain heights:

```csharp
float normalizedHeight = generator.GetHeight(x, z);

// This is NOT the actual terrain elevation in meters
// It's a normalized value used for biome determination

// Use biome to determine actual terrain type
BiomeType biome = generator.GetBiome(x, z);
if (biome == BiomeType.Mountain)
{
    Console.WriteLine("High elevation terrain");
}
```

---

## Advanced Topics

### Thread Safety

```csharp
// WorldGenerator is thread-safe for queries
var generator = new WorldGenerator("MySeed");

// Safe to query from multiple threads
Parallel.For(0, 1000, i =>
{
    float x = i * 100f;
    float z = i * 150f;
    BiomeType biome = generator.GetBiome(x, z);
    
    // Process biome data...
});
```

**Note**: Constructor is not thread-safe. Create WorldGenerator instances on main thread, then query from any thread.

---

### Custom Ocean Level

```csharp
// Use custom ocean threshold
float customOceanLevel = 0.05f;  // Default is 0.02
bool forceOcean = true;  // Treat all water as ocean (ignore swamp logic)

BiomeType biome = generator.GetBiome(x, z, customOceanLevel, forceOcean);
```

---

### Biome Flags and Masks

```csharp
// BiomeType is a flags enum - can use bitwise operations
BiomeType landBiomes = BiomeType.Meadows | BiomeType.BlackForest | 
                       BiomeType.Plains | BiomeType.Swamp;

BiomeType currentBiome = generator.GetBiome(x, z);

if ((currentBiome & landBiomes) != 0)
{
    Console.WriteLine("This is a land biome");
}
```

---

## Next Steps

1. **Explore the API**: See [contracts/WorldGenerator.md](./contracts/WorldGenerator.md) for complete API documentation
2. **Understand the algorithms**: Read [data-model.md](./data-model.md) for implementation details
3. **View examples**: Check `examples/` directory for complete sample projects
4. **Run tests**: See how the library is validated in `tests/WorldZones.WorldGen.Tests/`

---

## Need Help?

- **API Reference**: [contracts/WorldGenerator.md](./contracts/WorldGenerator.md)
- **Design Docs**: [data-model.md](./data-model.md)
- **Issues**: GitHub Issues (link TBD)
- **Discussions**: GitHub Discussions (link TBD)

---

## Complete Example Program

```csharp
using System;
using WorldZones.WorldGen;

namespace WorldGenExample
{
    class Program
    {
        static void Main(string[] args)
        {
            // Parse command line args
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: WorldGenExample <seed>");
                return;
            }
            
            string seed = args[0];
            Console.WriteLine($"Generating world with seed: {seed}");
            
            // Initialize generator
            var generator = new WorldGenerator(seed);
            
            // Test some known locations
            TestLocation(generator, 0f, 0f, "Spawn");
            TestLocation(generator, 1500f, 2000f, "Northeast meadows");
            TestLocation(generator, 5000f, 0f, "Far east");
            TestLocation(generator, 0f, 8000f, "Far north");
            
            // Generate small map around spawn
            Console.WriteLine("\nGenerating 1000x1000 map around spawn...");
            var region = new CoordinateRegion
            {
                minX = -500f,
                minZ = -500f,
                maxX = 500f,
                maxZ = 500f
            };
            
            var startTime = DateTime.Now;
            BiomeType[,] map = generator.GetBiomeMap(region);
            var elapsed = DateTime.Now - startTime;
            
            Console.WriteLine($"Generated in {elapsed.TotalMilliseconds:F0}ms");
            
            // Analyze biomes
            var counts = new Dictionary<BiomeType, int>();
            for (int x = 0; x < map.GetLength(0); x++)
            {
                for (int z = 0; z < map.GetLength(1); z++)
                {
                    BiomeType biome = map[x, z];
                    counts[biome] = counts.GetValueOrDefault(biome, 0) + 1;
                }
            }
            
            Console.WriteLine("\nBiome distribution:");
            foreach (var kvp in counts.OrderByDescending(k => k.Value))
            {
                float pct = (kvp.Value / 1000000f) * 100f;
                Console.WriteLine($"  {kvp.Key}: {pct:F1}%");
            }
        }
        
        static void TestLocation(WorldGenerator gen, float x, float z, string label)
        {
            BiomeType biome = gen.GetBiome(x, z);
            float height = gen.GetHeight(x, z);
            Console.WriteLine($"{label} ({x}, {z}): {biome} (height: {height:F3})");
        }
    }
}
```

Save as `Program.cs`, compile with:
```powershell
dotnet build
dotnet run MySeedName
```
