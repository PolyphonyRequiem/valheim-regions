# Implementation Plan: Region Skeleton v0

**Branch**: `002-region-skeleton` | **Date**: 2026-02-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-region-skeleton/spec.md`

## Summary

Build a first-pass geographic "region skeleton" over a generated Valheim world by classifying 64×64m zones (aligned to Valheim's ZoneSystem) into land/shallow/deep, computing connected components for land bodies and shelves, detecting archipelago candidates, generating geodesic Voronoi proto-territories, and exporting debug overlay PNGs. This validates core geographic assumptions before region merging, naming, or gameplay semantics.

## Technical Context

**Language/Version**: C# 9.0 targeting .NET Framework 4.7.2 (Unity 2019.4 runtime)
**Primary Dependencies**: WorldZones.WorldGen (Feature 001 — WorldGenerator, BiomeType, CoordinateRegion, HeightmapBuilder); UnityEngine.CoreModule (for Vector2, Mathf.PerlinNoise); System.Drawing or SkiaSharp for PNG export
**Storage**: N/A (in-memory computation + PNG file export)
**Testing**: xUnit via `dotnet test`; visual validation via exported PNG overlays
**Target Platform**: Windows development workstation (offline analysis tool)
**Project Type**: Single library project extending existing WorldZones solution
**Performance Goals**: Full-world zone grid (≈312×312 zones for ±10,000m radius at 64m resolution) processed in <5 minutes
**Constraints**: Must be deterministic; must not require Unity Editor or game runtime beyond assembly references; no external NuGet dependencies in core library
**Scale/Scope**: Single world seed at a time; ≈97,000 zones per world grid

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration | ✅ PASS | Agent researched zone sizing against disassembly; team decided to align with Valheim's 64m ZoneSystem |
| II. Library-First | ✅ PASS | Region skeleton is pure C# library code; no Unity gameplay logic. Note: WorldGenerator already references UnityEngine for Perlin noise — this is an existing dependency, not new coupling |
| III. Pragmatic Testing | ✅ PASS | Testing focuses on algorithmic correctness (connected components, geodesic distance) and visual validation overlays. Trivial type-safe containers skip unit tests |
| IV. Stable Public API | ✅ PASS | New public types need XML docs; this is a new feature, no breaking changes |
| V. Simplicity Bias | ✅ PASS | No repository patterns, DI, or abstract frameworks. Concrete types: ZoneGrid, LandComponent, ShelfComponent, ProtoTerritory. No inheritance hierarchies |
| VI. Clear Contracts | ✅ PASS | All public APIs will have XML docs, thread safety notes, and O(n) complexity documentation |
| VII. Iterative Development | ✅ PASS | 4 user stories = 4 natural phases, each independently testable and demonstrable |

**Gate result: PASS — no violations. Proceed to Phase 0.**

## Project Structure

### Documentation (this feature)

```text
specs/002-region-skeleton/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── WorldZones.WorldGen/          # Existing — WorldGenerator, BiomeType, etc.
└── WorldZones.Regions/           # NEW — Region skeleton library
    ├── DepthClass.cs             # Land/Shallow/Deep enum
    ├── ZoneGrid.cs               # 64×64m zone grid over world
    ├── ZoneClassifier.cs         # Classifies zones by depth
    ├── ComponentLabeler.cs       # Connected-component analysis (includes LandComponent)
    ├── ShelfComponent.cs         # Labeled shelf (land∪shallow)
    ├── ArchipelagoDetector.cs    # Archipelago candidate detection
    ├── ArchipelagoCandidate.cs   # Archipelago metadata
    ├── ProtoRegionGenerator.cs   # Land-only BFS partitioning (per-component seeding)
    ├── ProtoRegion.cs            # Region result (ID, seed, area)
    └── MinorIslet.cs             # Small land components below proto-region threshold

tests/
├── unity/                        # Existing — Unity test runner project
│   └── Assets/
│       ├── Editor/
│       │   └── LandComponentExporter.cs  # PNG overlay export (runs in Unity batch mode)
│       └── Plugins/
│           ├── WorldZones.WorldGen.dll    # Existing precompiled reference
│           └── WorldZones.Regions.dll     # NEW — copied after build
└── WorldZones.Regions.Tests/     # NEW — xUnit tests for region skeleton
    ├── ZoneClassifierTests.cs
    ├── ArchipelagoDetectorTests.cs
    └── ProtoRegionGeneratorTests.cs
```

**Structure Decision**: New `WorldZones.Regions` project alongside existing `WorldZones.WorldGen`, following the same pattern: standalone .NET library → build DLL → copy to `tests/unity/Assets/Plugins/` → Unity Editor scripts auto-reference precompiled DLLs from Plugins. Region logic depends on WorldGen but has no reverse dependency. A dedicated `scripts/Export-LandComponents.ps1` handles building and deploying the DLL.

**Naming Decision**: Original plan used "ProtoTerritory" terminology. During implementation, renamed to "ProtoRegion" to better reflect its purpose as a region precursor rather than a territory claim. "WeightedZoneDistance" (Dijkstra with shallow cost) was deferred; v0 uses land-only BFS.

**Discovered Concepts**: `MinorIslet` emerged during implementation — small land components (< 12 zones) that are too tiny for their own proto-region. Tracked as metadata for future merging decisions. `EffectiveLandmass` (Union-Find grouping of land components via shallow bridging) was prototyped and abandoned — per-component seeding proved simpler and more effective.

## Complexity Tracking

> No constitution violations to justify. All design choices use concrete types, no abstractions beyond what the domain requires.
