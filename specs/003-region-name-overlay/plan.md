# Implementation Plan: Valheim Region Name Overlay

**Branch**: `003-region-name-overlay` | **Date**: 2026-02-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-region-name-overlay/spec.md`

## Summary

Deliver a minimal Valheim mod that shows deterministic region GUID names in three places: minimap bottom label, full-map hover top-left label, and one-time discovery banner per region per player. Keep patch count minimal by centering runtime integration on `Minimap.UpdateBiome(Player)` and `Player.UpdateBiome(float)`, while moving region logic into `WorldZones.Regions` and isolating Unity/BepInEx-specific code in a new mod integration project. Add a deploy-and-run developer workflow to build the plugin, copy artifacts/dependencies into the Valheim path, and launch a repeatable test session quickly.

## Technical Context

**Language/Version**: C# 9.0 targeting .NET Framework 4.7.2  
**Primary Dependencies**: BepInEx 5.x, HarmonyX, Valheim `Assembly-CSharp` + Unity runtime assemblies, `WorldZones.Regions` (core region logic)  
**Storage**: Local file-based persistence for per-player discovered regions (under BepInEx/plugin-managed config path)  
**Testing**: `dotnet test` for library logic; manual in-game validation for UI and patch behavior; Unity dev-console tests permitted but optional  
**Target Platform**: Windows Valheim client with BepInEx installed
**Project Type**: Multi-project library + game integration plugin  
**Performance Goals**: Region lookup/update path under 1 ms on zone changes; no player-observable UI stutter during movement/map hover  
**Constraints**: Keep Harmony patch count minimal (target two primary patches); mod project MUST NOT reference `WorldZones.WorldGen` directly or indirectly; CLI MUST continue using hand-spun worldgen pipeline  
**Scale/Scope**: Single-player/local-first behavior for v1; multiplayer authority/sync explicitly deferred

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration | ✅ PASS | Design challenges existing dependency shape and introduces provider abstraction to satisfy mod/runtime separation requirements |
| II. Library-First Architecture | ✅ PASS | Region logic remains in `WorldZones.Regions`; BepInEx/Harmony/Unity gameplay hooks isolated to new mod integration project |
| III. Pragmatic Testing Strategy | ✅ PASS | Core deterministic mapping and naming covered by `dotnet test`; UI patches validated manually in-game |
| IV. Stable Public API | ✅ PASS | New region-provider abstractions and naming APIs are additive; no breaking changes to existing external APIs planned |
| V. Simplicity Bias | ✅ PASS | Patch count intentionally constrained; concrete adapter pattern selected over framework-heavy indirection |
| VI. Clear Contracts | ✅ PASS | Planning includes explicit contracts for region resolution, discovery state, deployment workflow, and deferred multiplayer concerns |
| VII. Iterative Development Process | ✅ PASS | Work decomposed into independent slices: refactor dependencies, overlay rendering, discovery state, deploy/run workflow |

**Gate result (pre-research): PASS — proceed to Phase 0 research.**

## Project Structure

### Documentation (this feature)

```text
specs/003-region-name-overlay/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── region-overlay-api.yaml
└── tasks.md             # Created by /speckit.tasks
```

### Source Code (repository root)
```text
src/
├── WorldZones.WorldGen/                         # Existing hand-spun worldgen (CLI/testing only)
├── WorldZones.Regions/                          # Core region logic + deterministic naming + provider abstractions
├── WorldZones.Cli/                              # Existing CLI (continues to use hand-spun worldgen via adapter)
└── WorldZones.Mod.RegionOverlay/                # New BepInEx/Harmony integration layer
    ├── RegionOverlayPlugin.cs
    ├── Patches/
    │   ├── MinimapUpdateBiomePatch.cs
    │   └── PlayerUpdateBiomePatch.cs
    ├── Integration/
    │   ├── ValheimWorldDataProvider.cs
    │   └── MinimapLabelController.cs
    └── Persistence/
        └── DiscoveryStore.cs

scripts/
├── Deploy-RegionOverlayMod.ps1                  # Build + copy plugin/dependencies to game path
└── Launch-Valheim-TestSession.ps1               # Rapid local launch with predefined test profile assets

tests/
├── WorldZones.Regions.Tests/                    # Deterministic naming/provider tests
└── WorldZones.Mod.RegionOverlay.Tests/          # Optional plugin-level non-Unity tests (if feasible)
```

**Structure Decision**: Use a strict library/integration split. `WorldZones.Regions` becomes independent of `WorldZones.WorldGen` through provider abstractions so the mod can consume real Valheim world data while CLI remains on hand-spun worldgen via a separate adapter path.

## Complexity Tracking

No constitution violations require exception handling for this plan.

## Post-Design Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration | ✅ PASS | Plan explicitly rejected direct/indirect mod dependency on hand-spun worldgen and introduced safer separation approach |
| II. Library-First Architecture | ✅ PASS | Core region logic and naming remain in library; plugin remains thin integration layer |
| III. Pragmatic Testing Strategy | ✅ PASS | High-value deterministic and boundary tests prioritized; Unity manual checks retained for integration-only behavior |
| IV. Stable Public API | ✅ PASS | Provider abstraction introduced as additive contract with backward compatibility strategy for CLI |
| V. Simplicity Bias | ✅ PASS | Limited patch surface and concrete adapters; no DI framework or speculative abstractions |
| VI. Clear Contracts | ✅ PASS | Data model and OpenAPI contract capture runtime/deployment boundaries and state rules |
| VII. Iterative Development Process | ✅ PASS | Tasks naturally phase into refactor, overlay UI, discovery, and deploy/run automation |

**Gate result (post-design): PASS — ready for `/speckit.tasks`.**
