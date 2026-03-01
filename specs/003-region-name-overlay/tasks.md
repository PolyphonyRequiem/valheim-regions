# Tasks: Valheim Region Name Overlay

**Input**: Design documents from `/specs/003-region-name-overlay/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/region-overlay-api.yaml`, `quickstart.md`

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare branch artifacts and integration points for the naming-model transition.

- [X] T001 Confirm feature branch/task baseline in `specs/003-region-name-overlay/tasks.md`
- [X] T002 Verify mod deploy path and runtime references in `src/WorldZones.Mod.RegionOverlay/WorldZones.Mod.RegionOverlay.csproj`
- [X] T003 [P] Validate naming-model assumptions in `specs/003-region-name-overlay/plan.md`
- [X] T004 [P] Record implementation checkpoint note in `specs/003-region-name-overlay/status-review.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish deterministic 500-name catalog and shared naming contracts used by all user stories.

**⚠️ CRITICAL**: No user story implementation starts until this phase is complete.

- [X] T005 Create 500-name literal catalog source in `src/WorldZones.Regions/RegionNameCatalog.cs`
- [X] T006 Implement deterministic catalog-index mapping service in `src/WorldZones.Regions/RegionGuidNameService.cs`
- [X] T007 Update lookup result contract from GUID semantics to name semantics in `src/WorldZones.Regions/IRegionLookupService.cs`
- [X] T008 Update region lookup resolution to emit deterministic names in `src/WorldZones.Regions/RegionLookupService.cs`
- [X] T009 [P] Add deterministic-name and catalog-size tests in `tests/WorldZones.Regions.Tests/RegionNameCatalogTests.cs`
- [X] T010 [P] Add overflow reuse determinism tests (>500 regions) in `tests/WorldZones.Regions.Tests/RegionNameCatalogTests.cs`
- [X] T011 Update plugin integration compile points for renamed lookup fields in `src/WorldZones.Mod.RegionOverlay/RegionOverlayPlugin.cs`
- [ ] T012 Update CLI debug/name output wiring for naming contract parity in `src/WorldZones.Cli/Program.cs`
- [X] T013 Add foundational validation steps for 500-name catalog integrity in `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: Catalog naming contract is deterministic, shared, and test-validated; user stories can proceed independently.

---

## Phase 3: User Story 1 - Minimap Region Visibility (Priority: P1) 🎯 MVP

**Goal**: Show current region test name at the bottom of the minimap while minimap is visible.

**Independent Test**: Move through region boundaries in-game and verify minimap bottom label updates to deterministic test names and hides correctly.

- [X] T014 [US1] Refactor minimap label state properties from GUID text to region name text in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [X] T015 [US1] Bind minimap small-label rendering to deterministic region name output in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [X] T016 [P] [US1] Update minimap biome patch callback payload handling for name-based lookups in `src/WorldZones.Mod.RegionOverlay/Patches/MinimapUpdateBiomePatch.cs`
- [X] T017 [P] [US1] Update plugin diagnostics/log fields from `guid` to `regionName` in `src/WorldZones.Mod.RegionOverlay/RegionOverlayPlugin.cs`
- [X] T018 [US1] Preserve minimap visibility/unresolved fallback behavior with name output in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [X] T019 [US1] Add US1 name-based validation steps and expected results in `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: User Story 1 is fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - World Map Hover Region Context (Priority: P2)

**Goal**: Show hovered region test name in top-left on the full map without breaking biome text behavior.

**Independent Test**: Open full map, hover explored locations, and verify top-left region name updates while vanilla biome text remains intact.

- [ ] T020 [US2] Extend label controller with full-map hover name display state in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [ ] T021 [P] [US2] Implement hover-position region-name resolution in minimap patch flow in `src/WorldZones.Mod.RegionOverlay/Patches/MinimapUpdateBiomePatch.cs`
- [ ] T022 [P] [US2] Render hover region names in map top-left UI location in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [ ] T023 [US2] Handle out-of-bounds/unassigned hover contexts with blank name output in `src/WorldZones.Mod.RegionOverlay/Patches/MinimapUpdateBiomePatch.cs`
- [ ] T024 [US2] Add US2 hover-name validation steps and expected results in `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Region Discovery Feedback (Priority: P3)

**Goal**: Show one-time discovery banner per region per player and persist discovery state across sessions using name-based display semantics.

**Independent Test**: Enter undiscovered region once (banner appears with test name), re-enter same region (no banner), restart and verify suppression persists.

- [ ] T025 [P] [US3] Refactor discovery state model to store region names/identity-safe keys in `src/WorldZones.Mod.RegionOverlay/Persistence/DiscoveryState.cs`
- [ ] T026 [P] [US3] Update discovery store serialization fields from GUID keys to name-based keys in `src/WorldZones.Mod.RegionOverlay/Persistence/DiscoveryStore.cs`
- [ ] T027 [US3] Update discovery trigger patch to emit name-based discovery payload in `src/WorldZones.Mod.RegionOverlay/Patches/PlayerUpdateBiomePatch.cs`
- [ ] T028 [US3] Integrate name-based check-and-record flow in `src/WorldZones.Mod.RegionOverlay/RegionOverlayPlugin.cs`
- [ ] T029 [US3] Add persistence migration/fallback handling for pre-existing GUID discovery files in `src/WorldZones.Mod.RegionOverlay/Persistence/DiscoveryStore.cs`
- [ ] T030 [US3] Add US3 name-banner validation steps and expected results in `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finalize docs, validation, and deployment workflow for the name-catalog release.

- [ ] T031 [P] Update naming-model architecture notes in `docs/development-environment.md`
- [ ] T032 [P] Update mod setup expectations for test-name mode in `docs/valheim-path-setup.md`
- [ ] T033 Update local contract examples and payload fields in `specs/003-region-name-overlay/contracts/region-overlay-api.yaml`
- [ ] T034 Run end-to-end quickstart validation and record final notes in `specs/003-region-name-overlay/quickstart.md`
- [ ] T035 Finalize deploy/run script verification for current naming model in `scripts/Deploy-RegionOverlayMod.ps1`
- [ ] T036 Finalize rapid launch script verification for current naming model in `scripts/Launch-Valheim-TestSession.ps1`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: Starts immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1 and blocks all user stories.
- **Phase 3 (US1)**: Depends on Phase 2 completion.
- **Phase 4 (US2)**: Depends on Phase 2 completion; does not require US1 completion.
- **Phase 5 (US3)**: Depends on Phase 2 completion; does not require US1/US2 completion.
- **Phase 6 (Polish)**: Depends on completion of selected user stories.

### User Story Dependency Graph

- **US1 (P1)**: Independent after foundational phase.
- **US2 (P2)**: Independent after foundational phase.
- **US3 (P3)**: Independent after foundational phase.

### Contract-to-Story Mapping

- `GET /v1/regions/current` → **US1**
- `GET /v1/regions/hover` → **US2**
- `POST /v1/discovery/check`, `GET /v1/discovery/state` → **US3**
- `POST /v1/dev/deploy`, `POST /v1/dev/launch` → **Setup/Polish**

---

## Parallel Execution Examples

### Foundational Phase

- Execute T009 and T010 in parallel (catalog determinism + overflow tests).
- Execute T011 and T012 in parallel after T007/T008 (mod and CLI integration updates).

### User Story 1

- Execute T016 and T017 in parallel after T014/T015 (patch payload handling vs diagnostics updates).

### User Story 2

- Execute T021 and T022 in parallel after T020 (hover resolution vs UI rendering).

### User Story 3

- Execute T025 and T026 in parallel (state model and storage schema), then proceed to T027/T028.

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate minimap-only behavior with deterministic catalog names before moving on.

### Incremental Delivery

1. Deliver US1 (minimap label with catalog names).
2. Deliver US2 (full-map hover name label).
3. Deliver US3 (discovery banner + persistence with name model).
4. Finish Phase 6 docs and deployment validation.

### Team Parallelization

1. One developer completes foundational naming-contract work (Phase 2).
2. Then split by story owner:
   - Developer A: US1
   - Developer B: US2
   - Developer C: US3

---

## Notes

- All tasks follow required checklist format: `- [ ] T### [P] [US#] Description with file path`.
- `[P]` appears only on tasks that can run independently without blocking dependencies.
- No test-first mandate was specified in the feature spec; tests included here focus on deterministic catalog correctness and overflow behavior.
- Naming model for this feature is test-catalog based (500 literals), not GUID-visible output.
