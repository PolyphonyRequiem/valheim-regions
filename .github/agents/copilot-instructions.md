# worldzones Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-14

## Active Technologies
- C# 9.0 targeting .NET Framework 4.7.2 + BepInEx 5.x, HarmonyX, Valheim `Assembly-CSharp` + Unity runtime assemblies, `WorldZones.Regions` (core region logic) (003-region-name-overlay)
- Local file-based persistence for per-player discovered regions (under BepInEx/plugin-managed config path) (003-region-name-overlay)
- C# 9.0 targeting .NET Framework 4.7.2 + `WorldZones.Regions`, BepInEx 5.x, HarmonyX, Valheim `assembly_valheim`, Unity runtime assemblies (including TextMeshPro for minimap-native text rendering) (003-region-name-overlay)
- Local file-based discovery persistence in plugin-managed path; static in-code 500-name catalog literals in `WorldZones.Regions` (003-region-name-overlay)
- C# 9.0 targeting .NET Framework 4.7.2 + `WorldZones.Regions`, `WorldZones.WorldGen`, `WorldZones.Cli`, BepInEx/Valheim integration for in-game validation (004-inland-water-attribution)
- In-memory region ownership grids plus existing PNG artifacts in feature/test output paths (004-inland-water-attribution)

- C# 7.3 (Unity 2019 compatibility for Valheim) (001-valheim-worldgen-library)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for C# 7.3 (Unity 2019 compatibility for Valheim)

## Code Style

C# 7.3 (Unity 2019 compatibility for Valheim): Follow standard conventions

## Recent Changes
- 004-inland-water-attribution: Added C# 9.0 targeting .NET Framework 4.7.2 + `WorldZones.Regions`, `WorldZones.WorldGen`, `WorldZones.Cli`, BepInEx/Valheim integration for in-game validation
- 003-region-name-overlay: Added C# 9.0 targeting .NET Framework 4.7.2 + `WorldZones.Regions`, BepInEx 5.x, HarmonyX, Valheim `assembly_valheim`, Unity runtime assemblies (including TextMeshPro for minimap-native text rendering)
- 003-region-name-overlay: Added C# 9.0 targeting .NET Framework 4.7.2 + BepInEx 5.x, HarmonyX, Valheim `Assembly-CSharp` + Unity runtime assemblies, `WorldZones.Regions` (core region logic)


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
