# Research: Valheim Region Name Overlay

**Phase 0 Output** | **Date**: 2026-02-28  
**Purpose**: Resolve all technical unknowns from `plan.md` and record final decisions.

---

## 1) Runtime Hook Strategy with Minimal Patches

**Decision**: Use two primary Harmony patches: `Minimap.UpdateBiome(Player)` for minimap/full-map label updates and `Player.UpdateBiome(float)` for discovery detection.

**Rationale**:
- Covers all required UX paths with minimal patch surface.
- Aligns with existing Valheim update cadence for biome/map context.
- Reduces maintenance burden versus many narrow hooks.

**Alternatives considered**:
- Patch `Minimap.Update()` and poll continuously (rejected: higher overhead and less semantic alignment).
- Patch `MessageHud` only (rejected: cannot drive minimap/full-map labels).
- Multiple micro-patches for each UI branch (rejected: violates patch-minimization goal).

---

## 2) Region Logic Placement and Dependency Split

**Decision**: Keep as much region logic as possible in `WorldZones.Regions`; introduce provider abstractions so `WorldZones.Regions` does not require `WorldZones.WorldGen` for mod use.

**Rationale**:
- Satisfies requirement that mod must not reference hand-spun worldgen directly or indirectly.
- Enables dual-provider model: CLI path uses hand-spun provider, mod path uses Valheim runtime provider.
- Preserves library-first architecture and testability.

**Alternatives considered**:
- Keep current direct `WorldZones.Regions -> WorldZones.WorldGen` dependency (rejected: violates mod dependency constraint).
- Duplicate region logic in mod project (rejected: drift risk, lower testability).
- Move all logic to mod plugin (rejected: violates library-first principle).

---

## 3) Deterministic Region GUID Naming

**Decision**: Generate deterministic GUID strings from world identity + region identity using a stable hash-to-guid mapping in `WorldZones.Regions`.

**Rationale**:
- Meets v1 requirement for GUID names.
- Guarantees repeatability for identical world/region inputs.
- Keeps naming implementation independent of UI/integration concerns.

**Alternatives considered**:
- Random GUID at discovery time (rejected: nondeterministic and unstable).
- Persisted generated GUID only (rejected: requires pre-seeding storage and complicates recovery).
- Human-readable names in v1 (rejected: explicitly out of scope).

---

## 4) Discovery State Persistence

**Decision**: Store discovered region GUID set per player in a local file (plugin-scoped path under BepInEx config/data).

**Rationale**:
- Supports once-per-region-per-player banner rule across sessions.
- Simple, inspectable format with low runtime overhead.
- Keeps persistence implementation isolated from core region library.

**Alternatives considered**:
- In-memory only session state (rejected: fails persistence requirement).
- Embed in world save via game internals (rejected: invasive and brittle).
- External database (rejected: unnecessary complexity).

---

## 5) Deployment and Rapid Test Execution Workflow

**Decision**: Add two scripts: one for build/deploy (`Deploy-RegionOverlayMod.ps1`) and one for fast launch preparation (`Launch-Valheim-TestSession.ps1`) using configured Valheim path and known test assets.

**Rationale**:
- Delivers repeatable local workflow for rapid iteration.
- Makes dependency validation explicit (BepInEx presence, plugin copy targets).
- Reduces manual setup errors between code changes and in-game validation.

**Alternatives considered**:
- Manual copy + manual game launch (rejected: slow and error-prone).
- Full external mod manager integration (rejected: over-scoped for v1).
- Auto-install third-party dependencies from internet (rejected: reliability/security concerns).

---

## 6) Unity Dev Console Testing Scope

**Decision**: Treat Unity dev-console checks as optional diagnostics, not required acceptance gates.

**Rationale**:
- Matches explicit requirement (“permitted but not required”).
- Keeps required validation focused on deterministic library tests and reproducible in-game checks.

**Alternatives considered**:
- Require dev-console tests for all changes (rejected: higher friction with limited additional value).
- Prohibit dev-console tests (rejected: removes useful debugging aid).

---

## 7) Multiplayer Scope Handling

**Decision**: Explicitly defer multiplayer authority/sync semantics in this feature.

**Rationale**:
- Already declared in feature spec.
- Avoids premature protocol and ownership design.
- Keeps first delivery focused and testable.

**Alternatives considered**:
- Define provisional multiplayer sync now (rejected: high uncertainty and expanded scope).
- Block feature until multiplayer finalized (rejected: prevents incremental delivery).
