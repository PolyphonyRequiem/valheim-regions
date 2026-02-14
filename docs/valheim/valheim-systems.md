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

## Open Questions (Need Disassembly Investigation)

### Zone System Internals
- [ ] What does `ZoneSystem` class look like internally?
- [ ] Are zones generated on-demand or pre-computed?
- [ ] Can we query all zones in a region efficiently?
- [ ] Is there zone data persistence/caching?

### Biome Detection
- [ ] Thread safety of `Heightmap.FindBiome()` and `ZoneSystem.GetBiome()`?
- [ ] Performance characteristics - can we call it thousands of times?
- [ ] What's the difference between the two APIs?
- [ ] Do they cache results?

### Coordinates & Bounds
- [ ] What coordinate system does Valheim use? (Origin location, scale, limits)
- [ ] What are the world boundaries? (min/max X and Z)
- [ ] Is Y (elevation) important for region definition?

### Existing Region Concepts
- [ ] Does Valheim have any built-in concept of "regions" beyond zones?
- [ ] Are there named areas in the game already?
- [ ] How do boss locations/spawn points work - are they "regions"?

### Multiplayer Considerations
- [ ] How does ZoneSystem work in multiplayer? (server-side? client-side? synced?)
- [ ] Can mods add persistent data to zones?
- [ ] How would region names sync across players?

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
