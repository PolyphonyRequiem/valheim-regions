# Implementation Plan: Valheim Region Name Overlay

**Branch**: `003-region-name-overlay` | **Date**: 2026-02-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-region-name-overlay/spec.md`

## Summary

Deliver region labels using a deterministic testing-name model instead of GUID strings. Names come from a fixed 500-name literal catalog owned by `WorldZones.Regions`, with deterministic mapping from `(worldId, regionId)` to one catalog entry. Preserve existing UI locations and discovery behavior, keep biome UI untouched, and keep the library/integration boundary intact.

## Technical Context

**Language/Version**: C# 9.0 targeting .NET Framework 4.7.2  
**Primary Dependencies**: `WorldZones.Regions`, BepInEx 5.x, HarmonyX, Valheim `assembly_valheim`, Unity runtime assemblies (including TextMeshPro for minimap-native text rendering)  
**Storage**: Local file-based discovery persistence in plugin-managed path; static in-code 500-name catalog literals in `WorldZones.Regions`  
**Testing**: `dotnet test` for library behavior and deterministic naming; manual in-game validation for minimap/full-map UI and discovery banner behavior  
**Target Platform**: Windows Valheim client with BepInEx installed  
**Project Type**: Multi-project library + game integration plugin  
**Performance Goals**: Region lookup and name resolution remains effectively O(1) per UI update; no user-observable minimap/map stutter  
**Constraints**: Keep Harmony patch surface minimal; mod project must not reference `WorldZones.WorldGen` directly or indirectly; visible names must come only from fixed 500-name literal catalog  
**Scale/Scope**: Single-player/local-first behavior for v1; multiplayer authority/sync remains deferred

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration Partnership | ✅ PASS | Feature direction challenged and revised from GUIDs to test-name catalog to improve UX testing fidelity |
| II. Library-First Architecture | ✅ PASS | Naming algorithm and catalog remain in `WorldZones.Regions`; mod remains integration layer |
| III. Pragmatic Testing Strategy | ✅ PASS | Deterministic mapping validated via `dotnet test`; UI behavior validated manually in-game |
| IV. Stable Public API | ✅ PASS | Existing lookup model remains additive; display-value semantics shift is feature-scoped and documented |
| V. Simplicity Bias | ✅ PASS | Static literal catalog + deterministic selector avoids external services or runtime content pipelines |
| VI. Clear Contracts | ✅ PASS | Data model and API contract explicitly define `regionName` and 500-catalog behavior |
| VII. Iterative Development Process | ✅ PASS | Change handled as spec/plan update before further implementation |

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
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── WorldZones.Regions/
│   ├── IRegionLookupService.cs
│   ├── RegionLookupService.cs
│   ├── RegionGuidNameService.cs              # Renamed/repurposed by follow-up tasks for test-name mapping
│   └── ProtoRegionGenerator.cs
├── WorldZones.Cli/
│   └── Program.cs
└── WorldZones.Mod.RegionOverlay/
    ├── RegionOverlayPlugin.cs
    ├── Integration/
    │   └── MinimapLabelController.cs
    └── Patches/
        ├── MinimapUpdateBiomePatch.cs
        └── PlayerUpdateBiomePatch.cs

tests/
└── WorldZones.Regions.Tests/
```

**Structure Decision**: Keep the existing architecture and shift naming responsibility entirely into `WorldZones.Regions` with a static 500-name catalog, while UI adapters consume resolved display names unchanged.

## Complexity Tracking

No constitution violations requiring justification.

## Post-Design Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration Partnership | ✅ PASS | Requirement change incorporated before coding continuation |
| II. Library-First Architecture | ✅ PASS | Catalog and mapping model are library-owned and reusable across mod/CLI |
| III. Pragmatic Testing Strategy | ✅ PASS | Determinism and bounds behavior are testable offline in region tests |
| IV. Stable Public API | ✅ PASS | Contracts updated to `regionName` while preserving response structure shape |
| V. Simplicity Bias | ✅ PASS | Literal source-file catalog avoids external assets/parsers/network concerns |
| VI. Clear Contracts | ✅ PASS | Updated contracts and quickstart describe catalog size, determinism, and overflow behavior |
| VII. Iterative Development Process | ✅ PASS | Phase outputs refreshed prior to task execution |

**Gate result (post-design): PASS — ready for `/speckit.tasks`.**
