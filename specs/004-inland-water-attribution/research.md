# Research: Inland Water Attribution

**Phase 0 Output** | **Date**: 2026-03-02  
**Purpose**: Resolve design decisions for inland water ownership while preserving existing land-seeded proto-region behavior.

---

## 1) Ownership Expansion Strategy

**Decision**: Keep land-seeded proto-region generation unchanged and add a deterministic post-pass to attribute inland water.

**Rationale**:
- Minimizes risk to existing region generation behavior.
- Preserves current tested invariants for land assignment.
- Delivers the requested semantics change (lakes belong to regions) with limited scope.

**Alternatives considered**:
- Rework proto generation to traverse water directly (rejected: broad behavior change and harder regression isolation).
- Full region model rewrite before attribution (rejected: exceeds MVP scope).

---

## 2) Inland vs Ocean-Connected Water Categorization

**Decision**: Use connectivity flood-fill from map boundaries to categorize ocean-connected water; unvisited water is inland.

**Rationale**:
- Deterministic and topology-based.
- Handles narrow channels naturally.
- Avoids biome heuristics that can misclassify enclosed basins.

**Alternatives considered**:
- Biome-only ocean detection (rejected: insufficient for enclosed/ocean-adjacent complexity).
- Distance-to-edge thresholding (rejected: unreliable for irregular coastlines).

---

## 3) Inland Water Attribution Rule

**Decision**: Attribute each inland water body to adjacent regions using highest shared boundary count; deterministic tie-break on lowest region ID.

**Rationale**:
- Aligns with existing merge tie-break style in current region generation patterns.
- Produces stable and explainable results.
- Easy to test with synthetic grids.

**Alternatives considered**:
- Nearest-seed attribution through water (rejected: can conflict with local shoreline intuition).
- Random tie resolution (rejected: nondeterministic).

---

## 4) Unassignable Inland Water Handling

**Decision**: Leave inland water unassigned when no adjacent assigned region exists; emit explicit count/metric.

**Rationale**:
- Safe-fail behavior avoids incorrect ownership.
- Surfaces data anomalies for debugging.

**Alternatives considered**:
- Force assignment to nearest region globally (rejected: non-local and potentially misleading).
- Treat as hard error (rejected: unnecessary fragility for runtime generation).

---

## 5) Validation Strategy

**Decision**: Validate using both (1) visual PNG comparison of old vs new samples and (2) in-game verification that lakes are incorporated.

**Rationale**:
- PNG visual comparison quickly reveals global topology/coverage changes.
- In-game validation confirms practical UX behavior in real map usage.

**Alternatives considered**:
- Test-only validation (rejected: misses visual/integration regressions).
- In-game-only validation (rejected: slower and less reproducible than artifact diffing).

---

## 6) Scope Guardrails

**Decision**: Explicitly defer boundary geometry reforms (polylines/splines/high-detail LOD boundaries) to future features.

**Rationale**:
- Keeps this feature focused on territory semantics (inland water inclusion).
- Avoids coupling attribution work to rendering architecture changes.

**Alternatives considered**:
- Coupling attribution with boundary rendering overhaul (rejected: too large for MVP).