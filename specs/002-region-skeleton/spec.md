# Feature Specification: Region Skeleton v0

**Feature Branch**: `002-region-skeleton`  
**Created**: 2026-02-21  
**Status**: Draft  

**Intent**  
Establish a first-pass geographic "region skeleton" over the world that is:
- deterministic
- biome-aware
- shallow-aware
- visually inspectable

This feature exists to validate core geographic assumptions before region merging, naming, or gameplay semantics are introduced.

---

## Scope

### In Scope
- Zone-based land / shallow / deep classification
- Connected-component analysis for land and land∪shallow
- Detection of archipelago *candidates* (bias signals, not regions)
- Proto-territory generation via simple geodesic Voronoi
- Debug visualization overlays

### Explicitly Out of Scope
- Region merging or size normalization
- Feature-aware borders (ridges, rivers, cliffs)
- Gameplay semantics (ownership, PvP, scaling)
- Region naming
- Ocean regions

---

## Core Definitions

### Zone
A **Zone** is a 64×64 meter square spatial unit, matching Valheim's `ZoneSystem.c_ZoneSize = 64`.
All region logic operates on zones.

Each zone has:
- 4-neighbor adjacency
- Depth classification
- Biome corner samples (4 per zone)

### DepthClass
Each zone is classified as exactly one of:
- `Land`
- `Shallow`
- `Deep`

Definitions:
- `Land`: above sea level
- `Shallow`: below sea level but above shallow threshold
- `Deep`: below shallow threshold

### Land Component
A connected component over zones where `DepthClass == Land`.

### Shelf Component
A connected component over zones where  
`DepthClass ∈ { Land, Shallow }`.

Shelf components define *coherence realms* for islands.

### Archipelago Candidate
A **Shelf Component** is flagged as an *archipelago candidate* if:
- It contains **≥ N distinct land components**
- No single land component exceeds **X%** of total land area in the shelf

Archipelago candidates influence later merging but do not create regions in v0.

### Proto-Territory
A preliminary territorial partition created by assigning zones to the nearest seed via weighted adjacency distance over zones.

Elevation is used only for DepthClass classification (land/shallow/deep) in v0. Proto-territory distance uses weighted zone adjacency (land vs shallow vs deep blocked) and does not incorporate slope, ridge, or elevation cost.

Proto-territories:
- Always contain land
- May include shallow zones
- Never include deep zones

---

## User Scenarios & Testing

### User Story 1 — Identify Land and Shelf Components (P1)

As a developer,  
I need to identify contiguous land bodies and continental shelves  
so that geographic structure is explicit and inspectable.

**Acceptance Criteria**
1. Every zone is classified as Land, Shallow, or Deep
2. Land components are contiguous only via land adjacency
3. Shelf components connect land via shallow zones
4. Deep zones do not participate in any component

---

### User Story 2 — Detect Archipelago Candidates (P2)

As a developer,  
I need to detect island clusters that share shallow connectivity  
so that archipelagos are not lost in later region processing.

**Acceptance Criteria**
1. A shelf with multiple land components may be flagged as an archipelago
2. A dominant landmass prevents archipelago classification
3. Archipelago detection is deterministic
4. Archipelago candidates are metadata only (no regions created)

---

### User Story 3 — Generate Proto-Territories (P3)

As a developer,  
I need a first-pass territorial partition  
to evaluate whether the geographic model produces plausible regions.

**Rules**
- Seeds are placed only on land zones
- Distance is computed over zones:
  - Land cost = 1
  - Shallow cost > 1
  - Deep = impassable

**Acceptance Criteria**
1. Every land zone belongs to exactly one proto-territory
2. Proto-territories never cross deep water
3. Shallow zones are assigned only when attached to land
4. Proto-territories are contiguous

---

### User Story 4 — Debug Visualization (P4)

As a developer,  
I need visual overlays to validate results by inspection.

**Required Outputs**
- `land_components.png`
- `shelf_components.png`
- `archipelago_candidates.png`
- `proto_territories.png`

**Acceptance Criteria**
- Overlays align with biome map coordinates
- Boundaries are visually coherent
- No gaps or overlaps exist

---

## Functional Requirements

- **FR-001**: System MUST operate on a 64×64 meter zone grid aligned to Valheim's ZoneSystem
- **FR-002**: Every zone MUST be classified as Land, Shallow, or Deep
- **FR-003**: Land components MUST be computed via connected components
- **FR-004**: Shelf components MUST be computed over Land ∪ Shallow
- **FR-005**: Archipelago candidates MUST be derived from shelf components
- **FR-006**: Proto-territories MUST be generated via weighted zone adjacency distance (not elevation-based geodesic)
- **FR-007**: Deep zones MUST NOT be assigned to any proto-territory
- **FR-008**: All results MUST be deterministic
- **FR-009**: Debug overlays MUST be exportable as PNGs

---

## Invariants

- Every land zone belongs to exactly one proto-territory
- No proto-territory contains deep zones
- Shallow zones never form a region by themselves
- Outputs are identical for the same seed and parameters

---

## Parameters (Configurable)

- Shallow traversal cost
- Seed count or density
- Archipelago thresholds:
  - Minimum land components
  - Maximum dominant land share

---

## Success Criteria

- Results visually align with human intuition on real maps
- Archipelagos remain coherent
- Mainland regions do not balloon along coasts
- Ashlands / Deep North naturally isolate without special casing
- Full-world run completes in acceptable offline time

---

## Rationale

This feature intentionally prioritizes **structural validation** over completeness.  
If the region skeleton is plausible, later refinement stages (merging, snapping, naming) can be added with confidence.

---

## Dependencies

- Feature 001: WorldGenerator + biome map export
- Height sampling for depth classification
- PNG export utilities

---

## Out of Scope

- Region merging
- Feature-aware borders
- Gameplay logic
- Naming
- Persistence formats