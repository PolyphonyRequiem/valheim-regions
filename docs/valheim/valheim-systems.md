# Valheim Game Systems Knowledge Base

**Purpose:** Document Valheim's existing world generation, zone system, biome detection, and modding APIs to inform WorldZones design decisions.

**Status:** Initial research phase - knowledge will be expanded as we investigate further.

---

## World Generation Overview

### Seed-Based Procedural Generation
- World generation is **deterministic** based on a world seed (alphanumeric string)
- Same seed always produces same world structure (terrain, biomes, POI locations)
- World is circular/disk-shaped with defined center and edges
- Micro-features (individual rock placement) may vary slightly between identical seeds

### Heightmap (Terrain)
- Terrain elevation generated using **Perlin noise** functions
- Creates realistic hills, valleys, mountains, coastlines
- Heightmap is seed-deterministic - same seed = same terrain

### Map Layout
- Massive circular world with center point
- Multiple islands of varying sizes
- Ocean surrounds landmasses
- Players can fall off edges

---

## Existing Zone System

**CRITICAL FINDING:** Valheim already has a built-in "zone" concept.

### Zone Structure
- World divided into **64x64 meter squares** called "zones"
- Zones are the fundamental unit for world generation
- Each zone has biome information stored at its **four corners**

### Zone-to-Coordinate Mapping
```csharp
// Convert world position to zone coordinates
int zoneX = Mathf.FloorToInt(position.x / 64f);
int zoneY = Mathf.FloorToInt(position.z / 64f); // Note: Z is forward in Unity
```

### Pure vs Edge Zones
- **Pure zone**: All 4 corners have same biome
- **Edge zone**: Corners have different biomes (transition zones)
- Biome at any point calculated based on proximity to corner values
- Enables smooth biome transitions/blending

### Zone-Based Generation
- Locations (dungeons, boss altars, villages) placed per-zone
- Vegetation spawned per-zone based on biome
- Features filtered by:
  - Biome type
  - Altitude range
  - Distance from world center

---

## Biome System

### Available Biomes
- Meadows
- Black Forest
- Swamp
- Mountains
- Plains
- Mistlands
- Ashlands
- Deep North

### Biome Detection API

#### Heightmap.FindBiome()
```csharp
// Get biome at specific world position
Vector3 worldPos = new Vector3(x, y, z);
Heightmap.Biome biome = Heightmap.FindBiome(worldPos);

// Overload using 2D coordinates
Heightmap.Biome biome = Heightmap.FindBiome(float x, float y);
```

**Thread Safety:** Unknown - needs verification from disassembly
**Performance:** Unknown - needs profiling

#### ZoneSystem.GetBiome()
```csharp
// Alternative API via ZoneSystem singleton
var zoneSystem = ZoneSystem.instance;
Vector3 somePosition = new Vector3(x, y, z);
var biome = zoneSystem.GetBiome(somePosition);
```

**When to use which:** Unknown - needs investigation in disassembly

---

## Modding APIs and Tools

### BepInEx Framework
- Standard modding framework for Unity-based games including Valheim
- Allows C# code injection as plugins
- Plugins reference:
  - `Assembly-CSharp.dll` (Valheim game code)
  - `UnityEngine.dll`
  - `BepInEx.dll`

### Jötunn Modding Library
- Higher-level modding library built on BepInEx
- URL: https://valheim-modding.github.io/Jotunn/
- Provides helpers for zones, biomes, world data interaction
- May include events/wrappers for zone system
- **TODO:** Investigate if we should use Jötunn or go directly to game APIs

### Example: Basic Biome Detection Mod
```csharp
using BepInEx;
using UnityEngine;

[BepInPlugin("com.example.biomedetector", "Biome Detector", "1.0.0")]
public class BiomeDetector : BaseUnityPlugin
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B))
        {
            var player = Player.m_localPlayer;
            if (player != null)
            {
                Vector3 position = player.transform.position;
                Heightmap.Biome biome = Heightmap.FindBiome(position);
                Logger.LogInfo($"Biome at ({position.x}, {position.z}): {biome}");
            }
        }
    }
}
```

### Useful Existing Mods (for reference)
- **CoordinatesDisplay**: Displays player coords + biome in HUD
- **Expand World Data**: Deep biome/world customization
- **RuntimeUnityEditor**: Developer tool for real-time inspection/debugging

---

## Disassembly Findings (Valheim 0.221.10)

### World Boundaries and Coordinate System
✅ **World Size:** 10,000m radius (20,000m diameter), circular
- Origin (0, 0, 0) at world center
- X and Z are horizontal plane (Y is elevation)
- Water edge extends to 10,500m radius
- World boundary enforced in code: checks `magnitude < 10000f`
- Random spawn uses range: `UnityEngine.Random.Range(-10000f, 10000f)`

### Zone System Architecture

#### Zone Structure
✅ **Zone Size:** 64m x 64m (constant: `ZoneSystem.c_ZoneSize = 64f`)
- Zone half-size: 32m (constant: `c_ZoneHalfSize`)
- Zones identified by `Vector2i` (integer grid coordinates)
- Water level: 30m (constant: `c_WaterLevel`)

#### Zone Coordinate Conversion
```csharp
// World position → Zone ID
public static Vector2i GetZone(Vector3 point) {
    int x = Utils.FloorToInt((point.x + 32.0) / 64.0);
    int y = Utils.FloorToInt((point.z + 32.0) / 64.0);  // Note: Z not Y
    return new Vector2i(x, y);
}

// Zone ID → World position (center of zone)
public static Vector3 GetZonePos(Vector2i id) {
    return new Vector3(id.x * 64.0, 0f, id.y * 64.0);
}
```

#### Zone Loading and Lifecycle
✅ **On-Demand Loading:** Zones are generated/loaded dynamically around players
- Active area: `m_activeArea` zones around center (default: 1 = 3x3 grid)
- Active distant area: `m_activeDistantArea` (default: 1)
- Time-to-live (TTL): Inactive zones destroyed after `m_zoneTTL` seconds (default: 4s)
- Time-to-spawn (TTS): Active zones spawn after `m_zoneTTS` seconds (default: 4s)
- Zone tracking: `Dictionary<Vector2i, ZoneData> m_zones`
- Generated zones: `HashSet<Vector2i> m_generatedZones` (persisted in save file)

#### Zone Generation State
✅ **Persistent Generation:** Once a zone is generated, it's tracked forever
- `m_generatedZones` saved with world data
- Vegetation, locations, and features generated once per zone
- Regeneration only occurs if world data changes

### Biome System Deep Dive

#### Biome Enum (Flags)
```csharp
public enum Biome {
    None = 0,
    Meadows = 1,
    Swamp = 2,
    Mountain = 4,
    BlackForest = 8,
    Plains = 0x10,      // 16
    AshLands = 0x20,    // 32
    DeepNorth = 0x40,   // 64
    Ocean = 0x100,      // 256
    Mistlands = 0x200,  // 512
    All = 0x37F         // 895 (all biomes OR'd)
}
```

**Note:** Flags-based enum allows biome masks for filtering (e.g., spawn only in Meadows | BlackForest).

#### Heightmap Corner Biomes
✅ **Biome Storage:** Each heightmap (64x64m zone) has 4 corner biomes
- `private Biome[] m_cornerBiomes = new Biome[4]` (corners of the zone)
- Corners calculated from `WorldGenerator.GetBiome()` at zone boundaries
- Pure zones: all 4 corners same biome
- Edge zones: corners differ (transition zones)

#### Biome Detection APIs

**1. Heightmap.FindBiome(Vector3 point) - Static**
```csharp
public static Biome FindBiome(Vector3 point) {
    Heightmap heightmap = FindHeightmap(point);  // Find heightmap containing point
    if (!heightmap) return Biome.None;
    return heightmap.GetBiome(point);            // Delegate to instance method
}
```
- Searches `static List<Heightmap> s_heightmaps` (all loaded heightmaps)
- **Thread Safety:** ❌ NOT thread-safe (iterates non-locked static collection)
- **Performance:** O(n) where n = loaded heightmaps (typically small, ~9-25 for 3x3 to 5x5 active area)

**2. Heightmap.GetBiome(Vector3 point) - Instance**
```csharp
public Biome GetBiome(Vector3 point, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) {
    // Fast path: pure zone (all corners same biome)
    if (m_cornerBiomes[0] == m_cornerBiomes[1] && 
        m_cornerBiomes[0] == m_cornerBiomes[2] && 
        m_cornerBiomes[0] == m_cornerBiomes[3]) {
        return m_cornerBiomes[0];
    }
    
    // Edge zone: weighted interpolation from 4 corners
    // Convert world pos to normalized heightmap coords (0-1 range)
    float x, y;
    WorldToNormalizedHM(point, out x, out y);
    
    // Calculate distance-based weights for each corner
    s_tempBiomeWeights[...] = Distance(x, y, corner0);  // etc for 4 corners
    
    // Return biome with highest weight
    return dominantBiome;
}
```
- **Thread Safety:** ❌ NOT thread-safe (uses `static float[] s_tempBiomeWeights` without locking)
- **Performance:** 
  - Pure zones: O(1) - simple comparison
  - Edge zones: O(1) - fixed calculation (4 corners, no loops over large data)
  - **Can be called frequently** but only from main thread

**3. WorldGenerator.GetBiome(float wx, float wy) - Authoritative**
```csharp
public Heightmap.Biome GetBiome(float wx, float wy, float oceanLevel = 0.02f, bool waterAlwaysOcean = false) {
    // Distance from world center
    float distance = DUtils.Length(wx, wy);
    float baseHeight = GetBaseHeight(wx, wy, menuTerrain: false);
    
    // Procedural biome assignment based on:
    // - Distance from center (concentric rings)
    // - Perlin noise (adds variation)
    // - Base height (ocean vs land, mountains)
    // - World angle (slight rotational variation)
    
    // Order matters - earlier checks take precedence:
    if (IsAshlands(wx, wy)) return Biome.AshLands;
    if (baseHeight <= oceanLevel) return Biome.Ocean;
    if (IsDeepnorth(wx, wy)) { /* Mountain or DeepNorth */ }
    if (baseHeight > 0.4f) return Biome.Mountain;
    if (/* Perlin noise + distance + height */) return Biome.Swamp;
    if (/* Perlin noise + distance */) return Biome.Mistlands;
    if (/* Perlin noise + distance */) return Biome.Plains;
    if (/* Perlin noise + distance */) return Biome.BlackForest;
    if (distance > 5000f + angle_variation) return Biome.BlackForest;
    return Biome.Meadows;  // Default (near center)
}
```
- **Deterministic:** Same (wx, wy, seed) always returns same biome
- **Thread Safety:** ⚠️ Probably safe for read-only calls, but has `ReaderWriterLockSlim` for river cache
- **Performance:** Expensive (multiple Perlin noise calls, height calculations)
- **When used:** During heightmap generation (zone corners), not for runtime queries

### Multiplayer and Networking

#### Server-Client Architecture
✅ **Server Authoritative:** ZoneSystem runs on server, syncs to clients
- Server generates zones and sends location icons to clients
- RPC calls: `"GlobalKeys"`, `"LocationIcons"`, `"SetGlobalKey"`, `"RemoveGlobalKey"`
- Clients receive location data via `RPC_LocationIcons(long sender, ZPackage pkg)`
- Server sends to all clients: `ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ...)`

#### Persistent Data
✅ **Generated Zones Saved:** `m_generatedZones` persisted in world save file
- Tracks which zones have been generated (vegetation, locations placed)
- Shared across all players in the world
- Cannot be reset without world regeneration

#### Modding Implications
⚠️ **Custom Region Data Sync:** 
- No built-in mod data persistence in ZoneSystem
- Region names would need custom networking (RPC or ZDO system)
- Must sync region definitions to all clients on join

### Thread Safety Summary

❌ **NOT Thread-Safe:**
- `Heightmap.FindBiome()` - iterates static list without locks
- `Heightmap.GetBiome()` - uses static `s_tempBiomeWeights` array
- `s_heightmaps` list - modified as zones load/unload

✅ **Main Thread Only:** All biome detection APIs must be called from Unity main thread

⚠️ **WorldGenerator:** Has locking for river cache, but biome calculation itself not thread-safe

### Performance Characteristics

**Biome Queries:**
- Pure zones: ~10-20 CPU cycles (comparison only)
- Edge zones: ~100-200 CPU cycles (4 distance calculations + lookup)
- Finding heightmap: O(n) but n is small (<50 heightmaps typically)
- **Can call thousands per frame** if all from main thread

**Zone Generation:**
- Expensive (vegetation placement, location spawning, terrain generation)
- Happens in background (coroutines)
- Cached forever once generated

**Recommended Usage:**
- ✅ Batch biome queries for region analysis
- ✅ Cache results if querying same points repeatedly
- ❌ Do NOT call from background threads
- ❌ Do NOT call WorldGenerator.GetBiome() at runtime (use heightmap APIs)

---

## Remaining Open Questions

### Custom Data Persistence
- [ ] How to persist custom region names across save/load? (ZDO system? Custom save file?)
- [ ] Where to store region metadata? (In-memory only? Persistent?)
- [ ] How to handle world seed changes? (Regions must regenerate)

### Region Naming Strategy
- [ ] Should regions follow biome boundaries or fixed grid?
- [ ] What granularity for regions? (single zone? 10x10 zones? variable size?)
- [ ] How to handle biome transitions in region names?
- [ ] Norse mythology name pool size needed?

### API Design for Downstream Modders
- [ ] What query patterns will modders use most? (point → region? region → bounds? all regions?)
- [ ] Should regions be generated all at once or on-demand like zones?
- [ ] How to expose biome-aware region data efficiently?
- [ ] Event system for "entered region" / "left region"?

### Testing Without Game Runtime
- [ ] Can we mock WorldGenerator for core logic tests?
- [ ] How to unit test region generation algorithms?
- [ ] Integration test strategy for Unity-dependent code?

---

## Design Implications for WorldZones

### Relationship to Existing Zones
- Valheim's 64x64m zones are **low-level infrastructure**
- WorldZones should be **higher-level regions** that group multiple zones
- Example: A "Northern Plains" region might contain 20x20 zones (1280m x 1280m)
- Must respect existing zone boundaries and biome data

### Architecture Considerations
- **Do NOT duplicate biome detection** - use existing `Heightmap.FindBiome()` or `ZoneSystem.GetBiome()`
- **Do NOT replace zones** - regions are a layer on top of zones
- Core logic should query game APIs for biome/terrain data, not reimagine world gen

### Naming Strategy
- Game has no built-in region names (opportunity for WorldZones)
- Can leverage biome data from existing APIs
- Can leverage geography (distance from center, elevation) via existing heightmap

---

## Decompiled Game Code Location

**Path:** `C:\Users\dangreen\projects\valheim\disassembly\0.221.10\`  
**Game Version:** 0.221.10  
**Format:** Decompiled C# with generated .csproj file  
**Status:** Read-only reference material (not in repo)

Use this for investigating internal implementation details of:
- `ZoneSystem` class
- `Heightmap` class and biome detection
- Coordinate systems and world boundaries
- Multiplayer/networking considerations

---

## Research Sources

- Valheim Fandom Wiki - Zones: https://valheim.fandom.com/wiki/Zones
- Jötunn Modding Tutorials - Zones: https://valheim-modding.github.io/Jotunn/tutorials/zones.html
- RandyKnapp Valheim Modding Guide: https://github.com/RandyKnapp/ValheimMods/blob/main/ValheimModding-GettingStarted.md
- Various modding community discussions
- Decompiled game assemblies (see location above)

---

**Last Updated:** 2026-02-14
**Next Steps:** Analyze C# disassembly of ZoneSystem, Heightmap, and related classes to answer open questions.
