# Implementation Plan: Inland Water Attribution

**Branch**: `004-inland-water-attribution` | **Date**: 2026-03-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-inland-water-attribution/spec.md`

## Summary

Add a post-processing inland-water attribution pass on top of existing land-seeded proto-region ownership. The pass classifies water into ocean-connected vs inland, assigns inland water to neighboring regions deterministically, preserves all pre-existing land ownership, and updates region summaries. Validation combines visual PNG comparison against existing samples and in-game checks confirming lakes are incorporated into region territory.

## Technical Context

**Language/Version**: C# 9.0 targeting .NET Framework 4.7.2  
**Primary Dependencies**: `WorldZones.Regions`, `WorldZones.WorldGen`, `WorldZones.Cli`, BepInEx/Valheim integration for in-game validation  
**Storage**: In-memory region ownership grids plus existing PNG artifacts in feature/test output paths  
**Testing**: `dotnet test` for deterministic logic + visual PNG regression checks + manual in-game validation of lake incorporation  
**Target Platform**: Windows Valheim client + .NET build/test environment  
**Project Type**: Multi-project library + CLI + game integration plugin  
**Performance Goals**: Attribution pass completes within same order of magnitude as current proto generation; no user-observable map overlay lag  
**Constraints**: Keep land-seeded generation unchanged; deterministic outputs for same seed/config; ocean-connected water remains out-of-scope for ownership  
**Scale/Scope**: World-scale zone grid (±10,000m) across all generated components with MVP scope limited to inland water inclusion

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration Partnership | ✅ PASS | Scope constrained to MVP attribution pass; deferred larger boundary reform |
| II. Library-First Architecture | ✅ PASS | Attribution logic remains in `WorldZones.Regions`; integration layers consume outputs |
| III. Pragmatic Testing Strategy | ✅ PASS | High-value algorithm tests plus visual and in-game validation added |
| IV. Stable Public API | ✅ PASS | Extend outputs additively; no breaking removal of existing entry points |
| V. Simplicity Bias | ✅ PASS | Single attribution pass over existing grids, no speculative geometry rewrite |
| VI. Clear Contracts | ✅ PASS | Plan adds explicit data model and contract artifacts for ownership semantics |
| VII. Iterative Development Process | ✅ PASS | Feature scoped as standalone iteration with clear acceptance gates |

**Gate result (pre-research): PASS — proceed to Phase 0 research.**

## Project Structure

### Documentation (this feature)

```text
specs/004-inland-water-attribution/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── inland-water-attribution-api.yaml
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── WorldZones.Regions/
│   ├── ProtoRegionGenerator.cs
│   ├── ComponentLabeler.cs
│   ├── ZoneGrid.cs
│   ├── ProtoRegionResult.cs
│   └── (new) InlandWaterAttribution*.cs
├── WorldZones.Cli/
│   └── Program.cs
└── WorldZones.Mod.RegionOverlay/
    └── RegionOverlayPlugin.cs

tests/
└── WorldZones.Regions.Tests/
    └── (new) InlandWaterAttributionTests.cs
```

**Structure Decision**: Keep all attribution and ownership semantics in `WorldZones.Regions`; use CLI and mod integration layers only for visualization/output and in-game verification.

## Complexity Tracking

No constitution violations requiring justification.

## Post-Design Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration Partnership | ✅ PASS | Added explicit non-goals to avoid hidden scope expansion |
| II. Library-First Architecture | ✅ PASS | Data model and API contract keep core logic library-owned |
| III. Pragmatic Testing Strategy | ✅ PASS | Validation strategy explicitly includes PNG visual diff + in-game lake verification |
| IV. Stable Public API | ✅ PASS | Contract is additive and versionable |
| V. Simplicity Bias | ✅ PASS | No polygon/spline overhaul in this feature |
| VI. Clear Contracts | ✅ PASS | Contract + data model define inland/ocean semantics and tie-break behavior |
| VII. Iterative Development Process | ✅ PASS | Planning artifacts complete and scoped for `/speckit.tasks` |

**Gate result (post-design): PASS — ready for `/speckit.tasks`.**
