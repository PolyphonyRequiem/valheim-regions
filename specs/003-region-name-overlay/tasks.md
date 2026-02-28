# Tasks: Valheim Region Name Overlay

**Input**: Design documents from `/specs/003-region-name-overlay/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/region-overlay-api.yaml`, `quickstart.md`

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Initialize project structure for mod integration and developer workflow.

- [ ] T001 Create mod integration project file at `src/WorldZones.Mod.RegionOverlay/WorldZones.Mod.RegionOverlay.csproj`
- [ ] T002 Add plugin entrypoint scaffold at `src/WorldZones.Mod.RegionOverlay/RegionOverlayPlugin.cs`
- [ ] T003 Add new project to solution in `WorldZones.slnx`
- [ ] T004 [P] Add deploy script scaffold at `scripts/Deploy-RegionOverlayMod.ps1`
- [ ] T005 [P] Add rapid launch script scaffold at `scripts/Launch-Valheim-TestSession.ps1`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish shared contracts and dependency split that all user stories rely on.

**⚠️ CRITICAL**: No user story implementation starts until this phase is complete.

- [ ] T006 Create world data provider abstraction at `src/WorldZones.Regions/IWorldDataProvider.cs`
- [ ] T007 Create region lookup service contract at `src/WorldZones.Regions/IRegionLookupService.cs`
- [ ] T008 Implement deterministic region GUID naming service at `src/WorldZones.Regions/RegionGuidNameService.cs`
- [ ] T009 Refactor region pipeline to use provider abstraction in `src/WorldZones.Regions/ProtoRegionGenerator.cs`
- [ ] T010 [P] Implement CLI hand-spun worldgen provider adapter at `src/WorldZones.Cli/CliWorldDataProvider.cs`
- [ ] T011 Wire CLI flow to provider abstraction in `src/WorldZones.Cli/Program.cs`
- [ ] T012 Implement Valheim runtime provider at `src/WorldZones.Mod.RegionOverlay/Integration/ValheimWorldDataProvider.cs`
- [ ] T013 Implement shared region lookup service in `src/WorldZones.Regions/RegionLookupService.cs`
- [ ] T014 Add foundational dependency-split validation checklist to `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: Core abstractions and deterministic lookup are complete; user stories can proceed independently.

---

## Phase 3: User Story 1 - Minimap Region Visibility (Priority: P1) 🎯 MVP

**Goal**: Show current region GUID at the bottom of the minimap while minimap is visible.

**Independent Test**: Move through region boundaries in-game and verify minimap bottom label updates correctly and hides when minimap is hidden.

- [ ] T015 [US1] Add minimap label UI controller at `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [ ] T016 [P] [US1] Implement minimap patch hook in `src/WorldZones.Mod.RegionOverlay/Patches/MinimapUpdateBiomePatch.cs`
- [ ] T017 [P] [US1] Connect plugin lifecycle to minimap label controller in `src/WorldZones.Mod.RegionOverlay/RegionOverlayPlugin.cs`
- [ ] T018 [US1] Add minimap visibility and unresolved-region fallback handling in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [ ] T019 [US1] Add US1 validation steps and expected results to `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: User Story 1 is fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 - World Map Hover Region Context (Priority: P2)

**Goal**: Show hovered region GUID in top-left on the full map without breaking biome text behavior.

**Independent Test**: Open full map, hover explored locations, and verify top-left region GUID updates while vanilla biome text remains intact.

- [ ] T020 [US2] Extend map label UI support for hover mode in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [ ] T021 [P] [US2] Add hover-position region resolution logic in `src/WorldZones.Mod.RegionOverlay/Patches/MinimapUpdateBiomePatch.cs`
- [ ] T022 [P] [US2] Preserve biome text coexistence behavior in `src/WorldZones.Mod.RegionOverlay/Integration/MinimapLabelController.cs`
- [ ] T023 [US2] Add full-map hover unresolved/out-of-bounds handling in `src/WorldZones.Mod.RegionOverlay/Patches/MinimapUpdateBiomePatch.cs`
- [ ] T024 [US2] Add US2 validation steps and expected results to `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: User Stories 1 and 2 both work independently.

---

## Phase 5: User Story 3 - Region Discovery Feedback (Priority: P3)

**Goal**: Show one-time discovery banner per region per player and persist discovery state across sessions.

**Independent Test**: Enter undiscovered region once (banner appears), re-enter same region (no banner), restart and verify suppression persists.

- [ ] T025 [P] [US3] Implement discovery state model at `src/WorldZones.Mod.RegionOverlay/Persistence/DiscoveryState.cs`
- [ ] T026 [P] [US3] Implement local discovery store at `src/WorldZones.Mod.RegionOverlay/Persistence/DiscoveryStore.cs`
- [ ] T027 [US3] Implement discovery trigger patch in `src/WorldZones.Mod.RegionOverlay/Patches/PlayerUpdateBiomePatch.cs`
- [ ] T028 [US3] Integrate discovery check-and-record flow in `src/WorldZones.Mod.RegionOverlay/RegionOverlayPlugin.cs`
- [ ] T029 [US3] Add discovery state persistence/recovery handling in `src/WorldZones.Mod.RegionOverlay/Persistence/DiscoveryStore.cs`
- [ ] T030 [US3] Add US3 validation steps and expected results to `specs/003-region-name-overlay/quickstart.md`

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Complete deployment workflow, documentation, and integration checks across all stories.

- [ ] T031 Finalize deploy script implementation in `scripts/Deploy-RegionOverlayMod.ps1`
- [ ] T032 Finalize rapid launch script implementation in `scripts/Launch-Valheim-TestSession.ps1`
- [ ] T033 [P] Document mod setup and dependency expectations in `docs/development-environment.md`
- [ ] T034 [P] Document Valheim path and BepInEx deployment assumptions in `docs/valheim-path-setup.md`
- [ ] T035 Run end-to-end quickstart validation and record final notes in `specs/003-region-name-overlay/quickstart.md`

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

### User Story 1

- Execute T016 and T018 in parallel after T015 (patch logic vs fallback handling in separate responsibilities).

### User Story 2

- Execute T021 and T022 in parallel after T020 (hover resolution vs biome text coexistence handling).

### User Story 3

- Execute T025 and T026 in parallel (state model and storage implementation), then proceed to T027/T028.

---

## Implementation Strategy

### MVP First (US1)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate minimap-only behavior as MVP before moving on.

### Incremental Delivery

1. Deliver US1 (minimap label).
2. Deliver US2 (full-map hover label).
3. Deliver US3 (discovery banner + persistence).
4. Finish Phase 6 scripts and documentation.

### Team Parallelization

1. One developer completes foundational dependency split (Phase 2).
2. Then split by story owner:
   - Developer A: US1
   - Developer B: US2
   - Developer C: US3

---

## Notes

- All tasks follow the required checklist format: `- [ ] T### [P] [US#] Description with file path`.
- `[P]` is used only where work is parallelizable without blocking dependencies.
- Harmony patch surface remains intentionally minimal (`MinimapUpdateBiomePatch`, `PlayerUpdateBiomePatch`).
- Mod project must not reference `WorldZones.WorldGen` directly or indirectly.
