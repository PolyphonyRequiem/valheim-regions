# Feature Specification: Inland Water Attribution

**Feature Branch**: `[004-inland-water-attribution]`  
**Created**: 2026-03-02  
**Status**: Draft  
**Input**: User description: "Include inland lakes and enclosed water in region ownership while keeping land-seeded proto generation as the base model"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Inland Water Belongs to Regions (Priority: P1)

As a map and region system designer, I need enclosed inland water (lakes and other non-ocean-connected water) to belong to a region so each region represents a full territory instead of only dry land.

**Why this priority**: This is the core behavior change and primary value of the feature.

**Independent Test**: Can be fully tested by generating region ownership for a known world and verifying that enclosed inland water zones are assigned to neighboring regions while ocean-connected water remains unassigned.

**Acceptance Scenarios**:

1. **Given** a generated world with enclosed inland lakes, **When** region ownership is computed, **Then** each inland water zone is assigned to exactly one region.
2. **Given** a generated world with ocean-connected water, **When** region ownership is computed, **Then** ocean-connected water zones are not attributed as inland-water territory.
3. **Given** a land-seeded region baseline, **When** inland water attribution is applied, **Then** existing land-zone region ownership remains unchanged.
4. **Given** an inland water body with no adjacent assigned region, **When** inland water attribution is applied, **Then** the water body remains unassigned and is counted in safe-fail metrics.

---

### User Story 2 - Deterministic and Stable Attribution (Priority: P2)

As a developer validating world generation outputs, I need inland water attribution to be deterministic so repeated runs for the same world produce identical region ownership.

**Why this priority**: Determinism is required for reproducibility, debugging, and downstream tooling.

**Independent Test**: Can be fully tested by running the same world seed multiple times and confirming inland-water ownership outputs are identical.

**Acceptance Scenarios**:

1. **Given** the same world input and configuration, **When** inland water attribution is run repeatedly, **Then** identical inland-water ownership is produced each run.
2. **Given** an inland water zone touching multiple regions, **When** attribution resolves ties, **Then** the same region is chosen every run using a deterministic tie-break rule.
3. **Given** inland-water attribution is disabled, **When** generation runs with identical inputs, **Then** the full ownership grid matches the baseline land-seeded output exactly.

---

### User Story 3 - Region Metrics Reflect Territory (Priority: P3)

As a feature consumer using region metadata and overlays, I need region statistics to reflect territory that includes inland water so reported area and visualization are internally consistent.

**Why this priority**: This ensures outputs remain understandable once the ownership model changes.

**Independent Test**: Can be fully tested by comparing pre/post attribution summaries and validating that region totals increase only by inland-water area that was newly attributed.

**Acceptance Scenarios**:

1. **Given** region summaries before inland-water attribution, **When** attribution is applied, **Then** each affected region’s area reflects its newly attributed inland-water zones.
2. **Given** a world with no enclosed inland water, **When** attribution is applied, **Then** region summaries remain unchanged.

---

### Edge Cases

- Inland water bodies connected to ocean through a narrow channel must be treated as ocean-connected, not inland.
- Very small enclosed water pockets fully surrounded by one region must still be attributed and not ignored.
- Inland water touching multiple regions with equal boundary contact must resolve to one deterministic winner.
- Inland water with no adjacent assigned region (for example, due to upstream data gaps) must fail safe and remain unassigned instead of misassigned.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST categorize water zones as either ocean-connected water or inland water.
- **FR-002**: The system MUST attribute inland water zones to regions while leaving ocean-connected water unassigned by this feature.
- **FR-003**: The system MUST preserve existing land-zone ownership produced by the baseline land-seeded generation.
- **FR-004**: The system MUST assign each attributed inland water zone to exactly one region.
- **FR-005**: The system MUST use deterministic tie-break behavior when an inland water zone is equally attributable to multiple regions.
- **FR-006**: The system MUST expose updated region summary outputs that include attributed inland-water area.
- **FR-007**: The system MUST support running with inland-water attribution disabled, with exact ownership-grid equivalence to the baseline land-seeded output for identical inputs.
- **FR-008**: The system MUST continue operating safely when attribution cannot determine a valid owning region for a water zone, leaving that zone unassigned, incrementing explicit safe-fail counters, and avoiding any forced fallback assignment.

### Key Entities *(include if feature involves data)*

- **Water Connectivity Kind**: Categorization of each water zone as inland or ocean-connected.
- **Region Ownership Grid**: Territory ownership map including existing land ownership and newly attributed inland-water ownership.
- **Inland Water Body**: A contiguous set of inland water zones considered for attribution.
- **Region Summary**: Region-level aggregate metadata including territory area after inland-water attribution.

### Assumptions

- Land-seeded region generation remains the source of initial ownership and is not replaced by this feature.
- Ocean-connected water is outside the MVP scope for region ownership.
- Existing consumers can accept region-area changes resulting from inland-water inclusion.

### Dependencies

- Availability of existing zone categorization and baseline region ownership output.
- Availability of deterministic world input and reproducible generation settings.

## Decision Log

- **DL-001 (Public API scope)**: New public APIs for this feature are limited to inland-water categorization/attribution option/result types and any exposed summary fields they require.
- **DL-002 (FR-008 canonical fixture)**: Canonical safe-fail fixture is a synthetic inland-water body with zero adjacent assigned regions, expected to remain unassigned.
- **DL-003 (Contract status)**: `data-model.md` is the canonical informational contract for this feature; any YAML contract artifact is optional reference material and not a normative implementation gate.
- **DL-004 (Performance threshold)**: Attribution pass target is ≤ 1.5x baseline generation runtime and ≤ 250 ms additional runtime for default world radius on the required known validation seed (`HHcLC5acQt`) and any optional reproducible seeds used.
- **DL-005 (Visual sample set)**: Visual comparison requires `HHcLC5acQt` baseline + candidate artifacts, with optional additional reproducible seed pairs when available.
- **DL-006 (Validation sign-off)**: Sign-off requires one designated reviewer for visual validation and one for in-game validation, recorded in quickstart result tables.
- **DL-007 (Disabled-mode criterion)**: Disabled mode requires exact ownership-grid equality, not approximate or metric-only equality.
- **DL-008 (MVP integration scope)**: CLI and in-game validation tasks remain in feature scope because acceptance criteria require artifact and runtime verification.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For validation worlds containing enclosed inland lakes, 100% of inland water zones are either attributed to exactly one region or explicitly reported as unassigned due to a safe-fail rule.
- **SC-002**: Across repeated runs for the same world input, inland-water ownership output is identical in all runs.
- **SC-003**: Land-zone ownership differs by 0 zones before vs. after inland-water attribution.
- **SC-004**: For validation worlds with no enclosed inland water, region ownership and summary totals remain unchanged.
