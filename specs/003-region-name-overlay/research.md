# Research: Valheim Region Name Overlay

**Phase 0 Output** | **Date**: 2026-02-28  
**Purpose**: Resolve design decisions for deterministic region naming using a fixed 500-name testing catalog.

---

## 1) Deterministic Name Mapping Model

**Decision**: Replace GUID display with deterministic name selection from a fixed catalog of 500 literal names stored in source.

**Rationale**:
- Provides easier visual verification during in-game testing than GUID strings.
- Keeps deterministic behavior required by world/region reproducibility.
- Avoids runtime content loading and keeps behavior fully testable offline.

**Alternatives considered**:
- Continue GUID display (rejected: poor readability for rapid field testing).
- Random runtime name selection (rejected: nondeterministic and breaks reproducibility).
- External file-based name source (rejected: introduces deployment/file integrity complexity not needed for test scope).

---

## 2) Catalog Overflow Behavior (>500 Regions)

**Decision**: Use deterministic modular indexing for catalog reuse when region count exceeds 500.

**Rationale**:
- Guarantees total coverage for any region count without failure or missing names.
- Keeps lookup constant-time and simple.
- Preserves stable mapping across sessions for a fixed world and region identity.

**Alternatives considered**:
- Fail on overflow (rejected: unacceptable UX and invalid for larger maps).
- Append numeric suffixes dynamically (rejected: unnecessary complexity for test-focused release).
- Expand catalog at runtime (rejected: nondeterministic unless carefully constrained and persisted).

---

## 3) Placement of Naming Logic

**Decision**: Keep catalog and deterministic mapping in `WorldZones.Regions` so all consumers (mod and CLI) resolve display names consistently.

**Rationale**:
- Aligns with library-first constitution requirements.
- Prevents duplicated naming logic across integration layers.
- Enables direct unit testing of mapping and determinism.

**Alternatives considered**:
- Implement in mod project only (rejected: violates shared-library intent and creates drift).
- Keep mapping in UI layer (rejected: mixes domain identity rules with rendering concerns).

---

## 4) Discovery Persistence Keying

**Decision**: Discovery persistence should key on stable region identity and/or resolved deterministic region name while maintaining once-per-region semantics.

**Rationale**:
- Preserves behavior when visible representation changes from GUIDs to names.
- Keeps persisted behavior deterministic and migration-friendly.

**Alternatives considered**:
- Keep GUID-only discovery keys forever (rejected: mismatch with visible name model).
- Key only by transient UI text (rejected: fragile if display formatting evolves).

---

## 5) UI Coexistence with Biome Text

**Decision**: Continue using dedicated region label elements and avoid writing into vanilla biome text fields directly.

**Rationale**:
- Prevents regression where region text overwrites biome information.
- Keeps independent control for placement and autosizing.

**Alternatives considered**:
- Reuse biome text object for region name (rejected: causes UI conflicts and mode coupling).

---

## 6) Runtime Hook Strategy

**Decision**: Keep existing minimal patch strategy (`Minimap.UpdateBiome(Player)` and `Player.UpdateBiome(float)`) with naming resolution swapped to test-name catalog.

**Rationale**:
- Maintains low patch surface.
- Limits behavior changes to naming model rather than hook architecture.

**Alternatives considered**:
- Introduce additional map polling hooks (rejected: unnecessary complexity for naming-only change).

---

## 7) Multiplayer Scope Handling

**Decision**: Keep multiplayer authority/synchronization deferred.

**Rationale**:
- Naming model change does not require multiplayer design expansion.
- Avoids scope creep during test-name transition.

**Alternatives considered**:
- Introduce multiplayer naming authority now (rejected: out of current feature scope).
