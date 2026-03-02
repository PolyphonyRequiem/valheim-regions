# Implementation Plan: Inland Water Attribution

**Branch**: `004-inland-water-attribution` | **Date**: 2026-03-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-inland-water-attribution/spec.md`

## Summary

Add a post-processing inland-water attribution pass on top of existing land-seeded proto-region ownership. The pass classifies water into ocean-connected vs inland, assigns inland water to neighboring regions deterministically, preserves all pre-existing land ownership, and updates region summaries. Validation combines visual PNG comparison against existing samples and in-game checks confirming lakes are incorporated into region territory.

**Contract Status**: `data-model.md` is the canonical informational contract for feature semantics and validation invariants. Any YAML contract artifact is optional and non-normative.

## Technical Context

**Language/Version**: C# 9.0 targeting .NET Framework 4.7.2  
**Primary Dependencies**: `WorldZones.Regions`, `WorldZones.WorldGen`, `WorldZones.Cli`, BepInEx/Valheim integration for in-game validation  
**Storage**: In-memory region ownership grids plus existing PNG artifacts in feature/test output paths  
**Testing**: `dotnet test` for deterministic logic + visual PNG regression checks + manual in-game validation of lake incorporation  
**Target Platform**: Windows Valheim client + .NET build/test environment  
**Project Type**: Multi-project library + CLI + game integration plugin  
**Performance Goals**: Attribution-enabled generation runtime MUST be ≤ 1.5x baseline generation runtime and add ≤ 250 ms at default world radius for the required known validation seed (`HHcLC5acQt`) and any optional reproducible seeds used; map overlay behavior must show no user-observable lag during in-game validation  
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

## Public API Documentation Coverage

- All new public classes, methods, and properties introduced by this feature MUST have XML documentation comments.
- Coverage includes inland-water option/result models, categorization and attribution components, and any newly exposed summary fields.
- Verification of this requirement is tracked explicitly in `tasks.md` documentation tasks.

## Post-Design Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Critical Collaboration Partnership | ✅ PASS | Added explicit non-goals to avoid hidden scope expansion |
| II. Library-First Architecture | ✅ PASS | Data model keeps core logic library-owned |
| III. Pragmatic Testing Strategy | ✅ PASS | Validation strategy explicitly includes PNG visual diff + in-game lake verification |
| IV. Stable Public API | ✅ PASS | Public API changes are additive and versionable |
| V. Simplicity Bias | ✅ PASS | No polygon/spline overhaul in this feature |
| VI. Clear Contracts | ✅ PASS | Data model defines inland/ocean semantics and tie-break behavior |
| VII. Iterative Development Process | ✅ PASS | Implementation task scope and dependencies are defined and sequenced |

**Gate result (post-design): PASS — ready for implementation.**

## Consistency Re-analysis Summary

| Finding | Status | Resolution |
|---------|--------|------------|
| XML documentation coverage not exhaustive | ✅ Resolved | Added explicit plan coverage requirement and expanded docs tasks in `tasks.md` |
| FR-008 safe-fail behavior missing direct test mapping | ✅ Resolved | Added dedicated FR-008 safe-fail regression task |
| Performance goal not testable | ✅ Resolved | Added numeric thresholds and performance verification task |
| FR-007 disabled-mode equivalence underspecified | ✅ Resolved | Spec now requires exact ownership-grid equivalence and tasks include dedicated regression |
| Contract artifact mismatch with implementation scope | ✅ Resolved | Data model set as canonical informational contract; YAML contract conformance tasks removed |
| Quickstart validation audit trail underspecified | ✅ Resolved | Added required known seed + optional reproducible seeds, result templates, and sign-off recording requirements |

## Outstanding Items

No outstanding critical or high-severity consistency findings remain for implementation scope.
