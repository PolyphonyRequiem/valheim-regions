# Tasks: Inland Water Attribution

**Input**: Design documents from `/specs/004-inland-water-attribution/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., [US1], [US2], [US3])
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature-specific scaffolding and validation entry points.

- [x] T001 Create inland-water feature scaffold file in src/WorldZones.Regions/InlandWaterAttributionOptions.cs
- [x] T002 [P] Create shared inland-water test fixture utilities in tests/WorldZones.Regions.Tests/InlandWaterTestFixtures.cs

**Checkpoint**: Feature scaffolding and test fixtures exist for implementation.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core inland-water domain primitives and attribution pipeline hooks required before any user story implementation.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 [P] Define water connectivity enum in src/WorldZones.Regions/WaterConnectivityKind.cs
- [x] T005 [P] Define inland water body model in src/WorldZones.Regions/InlandWaterBody.cs
- [x] T006 [P] Define inland attribution result model in src/WorldZones.Regions/InlandWaterAttributionResult.cs
- [x] T007 Implement ocean-connectivity flood-fill categorizer in src/WorldZones.Regions/InlandWaterConnectivityCategorizer.cs
- [x] T008 Implement deterministic inland-water attributor in src/WorldZones.Regions/InlandWaterAttributor.cs
- [x] T009 Wire optional inland-water attribution switch into generation entrypoint in src/WorldZones.Regions/ProtoRegionGenerator.cs

**Checkpoint**: Core inland-water attribution infrastructure exists and generation pipeline can invoke it.

---

## Phase 3: User Story 1 - Inland Water Belongs to Regions (Priority: P1) 🎯 MVP

**Goal**: Ensure enclosed inland water is assigned to exactly one neighboring region while ocean-connected water is excluded.

**Independent Test**: Generate ownership for synthetic and known seed worlds; confirm inland water assigns to one region, ocean-connected water stays excluded, and land ownership is unchanged.

### Tests for User Story 1

- [x] T010 [P] [US1] Add inland-vs-ocean connectivity categorization tests in tests/WorldZones.Regions.Tests/InlandWaterConnectivityCategorizerTests.cs
- [x] T011 [P] [US1] Add attribution winner and tie-break tests in tests/WorldZones.Regions.Tests/InlandWaterAttributorTests.cs
- [x] T012 [US1] Add land-ownership-unchanged regression test in tests/WorldZones.Regions.Tests/InlandWaterAttributionIntegrationTests.cs
- [x] T031 [US1] Add FR-008 safe-fail regression test for inland water with no adjacent assigned region in tests/WorldZones.Regions.Tests/InlandWaterAttributionIntegrationTests.cs
- [x] T032 [US1] Add FR-007 disabled-mode exact-grid-equivalence regression test in tests/WorldZones.Regions.Tests/InlandWaterAttributionIntegrationTests.cs

### Implementation for User Story 1

- [x] T013 [US1] Integrate connectivity categorization and attribution pass into region generation flow in src/WorldZones.Regions/ProtoRegionGenerator.cs
- [x] T014 [US1] Record attributed/unassigned inland-water counts in generation output in src/WorldZones.Regions/ProtoRegionResult.cs
- [x] T015 [US1] Add inland-water attribution options defaults and validation in src/WorldZones.Regions/InlandWaterAttributionOptions.cs

**Checkpoint**: Inland water ownership behavior is functional and independently testable.

---

## Phase 4: User Story 2 - Deterministic and Stable Attribution (Priority: P2)

**Goal**: Guarantee deterministic inland-water attribution outputs for repeated runs of identical inputs.

**Independent Test**: Run repeated generation with same seed/config and assert identical attribution grids and counts, including deterministic tie outcomes.

### Tests for User Story 2

- [x] T016 [P] [US2] Add repeated-run determinism tests for attribution outputs in tests/WorldZones.Regions.Tests/InlandWaterAttributionDeterminismTests.cs
- [x] T017 [P] [US2] Add deterministic ordering assertions for tie scenarios in tests/WorldZones.Regions.Tests/InlandWaterTieBreakDeterminismTests.cs

### Implementation for User Story 2

- [x] T018 [US2] Enforce stable traversal and candidate ordering in src/WorldZones.Regions/InlandWaterConnectivityCategorizer.cs
- [x] T019 [US2] Enforce deterministic winner selection implementation in src/WorldZones.Regions/InlandWaterAttributor.cs
- [x] T020 [US2] Add ownership grid comparison helper for deterministic regression tests in tests/WorldZones.Regions.Tests/OwnershipGridAssertions.cs

**Checkpoint**: Deterministic behavior is proven and independently testable.

---

## Phase 5: User Story 3 - Region Metrics Reflect Territory (Priority: P3)

**Goal**: Ensure region summary metrics include inland-water territory without altering baseline land area accounting.

**Independent Test**: Compare summaries before/after attribution and verify totals are correct; verify unchanged summaries for worlds without inland water.

### Tests for User Story 3

- [x] T021 [P] [US3] Add region summary aggregation tests for land/inland/total area in tests/WorldZones.Regions.Tests/InlandWaterRegionSummaryTests.cs
- [x] T022 [P] [US3] Add no-inland-water no-change summary test in tests/WorldZones.Regions.Tests/InlandWaterNoOpSummaryTests.cs

### Implementation for User Story 3

- [x] T023 [US3] Extend proto region summary fields for inland-water area accounting in src/WorldZones.Regions/ProtoRegionResult.cs
- [x] T024 [US3] Update region aggregation logic for total area derivation in src/WorldZones.Regions/ProtoRegionGenerator.cs

**Checkpoint**: Region summaries include inland-water territory and remain consistent.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finish validation, documentation, and integration-level confidence checks.

- [x] T025 [P] Add XML documentation for all new inland-water public APIs in src/WorldZones.Regions/WaterConnectivityKind.cs
- [x] T026 [P] Add XML documentation for all new inland-water public APIs in src/WorldZones.Regions/InlandWaterBody.cs
- [x] T033 [P] Add XML documentation for all new inland-water public APIs in src/WorldZones.Regions/InlandWaterAttributionOptions.cs
- [x] T034 [P] Add XML documentation for all new inland-water public APIs in src/WorldZones.Regions/InlandWaterAttributionResult.cs
- [x] T035 [P] Add XML documentation for all new inland-water public APIs in src/WorldZones.Regions/InlandWaterConnectivityCategorizer.cs
- [x] T036 [P] Add XML documentation for all new inland-water public APIs in src/WorldZones.Regions/InlandWaterAttributor.cs
- [x] T027 Add CLI overlay export path for baseline vs candidate PNG comparisons in src/WorldZones.Cli/Program.cs
- [x] T029 Execute visual PNG comparison validation workflow and record outcomes in specs/004-inland-water-attribution/quickstart.md
- [x] T038 Add data-model invariant consistency tests (ownership/result/summary guarantees) in tests/WorldZones.Regions.Tests/InlandWaterModelConsistencyTests.cs

**Checkpoint**: Feature passes algorithmic tests, visual PNG validation, and model-consistency checks.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies.
- **Phase 2 (Foundational)**: Depends on Phase 1; blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2; delivers MVP inland-water ownership.
- **Phase 4 (US2)**: Depends on Phase 3; hardens deterministic behavior.
- **Phase 5 (US3)**: Depends on Phase 3; extends region summary semantics.
- **Phase 6 (Polish)**: Depends on Phases 3–5.

### User Story Dependencies

- **US1 (P1)**: Starts after foundational phase; no dependency on US2/US3.
- **US2 (P2)**: Starts after US1 baseline integration.
- **US3 (P3)**: Starts after US1 baseline integration; can proceed in parallel with US2 once US1 is complete.

### Within Each User Story

- Write story tests first and confirm failure before implementation.
- Implement core logic after tests.
- Verify story-level independent test criteria before moving on.

---

## Parallel Execution Examples

### User Story 1

```bash
# Parallel test authoring
T010 + T011

# Then implement integration and outputs
T013 -> T014 -> T015
```

### User Story 2

```bash
# Parallel deterministic test coverage
T016 + T017

# Then deterministic implementation hardening
T018 + T019
```

### User Story 3

```bash
# Parallel summary test coverage
T021 + T022

# Then summary model + aggregation implementation
T023 -> T024
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate inland-water attribution behavior independently (ownership correctness + land unchanged).
4. Pause for review before hardening and summary expansion.

### Incremental Delivery

1. **MVP**: US1 inland-water assignment correctness.
2. **Reliability**: US2 deterministic behavior.
3. **Semantics**: US3 summary/accounting updates.
4. **Validation**: Polish phase with PNG visual comparison and model-consistency checks.

### Suggested MVP Scope

- Deliver through **Phase 3 (US1)** only for first shippable increment.
- Treat US2/US3 + Phase 6 as follow-on hardening and reporting increments.

---

## Notes

- All tasks follow strict checklist format: checkbox, task ID, optional `[P]`, required story label for story phases, and explicit file path.
- Tests are included because the plan/spec explicitly require deterministic validation and regression safety.
- Validation coverage includes artifact-level visual checks and model-consistency checks.
- Data-model invariant consistency is a mandatory completion criterion for this feature.