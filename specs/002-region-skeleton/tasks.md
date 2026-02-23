# Tasks: Region Skeleton v0

**Input**: Design documents from `/specs/002-region-skeleton/`
**Prerequisites**: plan.md, spec.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the WorldZones.Regions project, wire it into the solution, and establish the build/deploy pipeline to the Unity test project.

- [ ] T001 Create project `src/WorldZones.Regions/WorldZones.Regions.csproj` targeting net472, LangVersion 9.0, with ProjectReference to WorldZones.WorldGen and Reference to UnityEngine.CoreModule from `lib/`
- [ ] T002 Create test project `tests/WorldZones.Regions.Tests/WorldZones.Regions.Tests.csproj` with xUnit dependencies and ProjectReference to WorldZones.Regions
- [ ] T003 Verify both projects build via `dotnet build` with zero errors

**Checkpoint**: Projects compile. No logic yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core types and the zone grid that ALL user stories depend on. Must complete before any story work.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 [P] Create `DepthClass` enum (Land, Shallow, Deep) in `src/WorldZones.Regions/DepthClass.cs`
- [ ] T005 [P] Create `ZoneGrid` class in `src/WorldZones.Regions/ZoneGrid.cs` — a 2D grid of zones covering ±10,000m world radius at 64m resolution; stores DepthClass per zone; provides indexing by zone coordinate (Vector2i) and world-to-zone coordinate conversion matching `ZoneSystem` formula: `x = Floor((worldX + 32) / 64)`, `y = Floor((worldZ + 32) / 64)`
- [ ] T006 Create `ZoneClassifier` in `src/WorldZones.Regions/ZoneClassifier.cs` — samples `WorldGenerator.GetBaseHeight()` at zone center, classifies each zone as Land/Shallow/Deep based on configurable elevation thresholds (sea level ≈ 0.05, shallow threshold configurable)
- [ ] T007 Test `ZoneClassifier` in `tests/WorldZones.Regions.Tests/ZoneClassifierTests.cs` — verify classification of known-height zones: land above sea level, shallow between thresholds, deep below threshold; verify determinism (same seed → same grid)

**Checkpoint**: ZoneGrid populated with DepthClass for every zone. Foundation ready for component analysis.

---

## Phase 3: User Story 1 — Identify Land and Shelf Components (Priority: P1) 🎯 MVP

**Goal**: Connected-component labeling over the zone grid — land components (contiguous land zones) and shelf components (contiguous land∪shallow zones).

**Independent Test**: Generate a world, run component analysis, export land_components.png and shelf_components.png, visually verify islands get distinct labels and shelves bridge shallow gaps.

### Implementation for User Story 1

- [ ] T008 [P] [US1] Create `LandComponent` record/class in `src/WorldZones.Regions/LandComponent.cs` — holds component ID, list of zone coordinates, area (zone count × 64²), bounding box
- [ ] T009 [P] [US1] Create `ShelfComponent` record/class in `src/WorldZones.Regions/ShelfComponent.cs` — holds component ID, list of zone coordinates, area, bounding box, and list of contained LandComponent IDs
- [ ] T010 [US1] Implement `ComponentLabeler` in `src/WorldZones.Regions/ComponentLabeler.cs` — flood-fill connected-component analysis over ZoneGrid; produces List<LandComponent> (over DepthClass.Land zones using 4-neighbor adjacency) and List<ShelfComponent> (over DepthClass ∈ {Land, Shallow} using 4-neighbor adjacency); maps each ShelfComponent to its contained LandComponents
- [ ] T011 [US1] Test `ComponentLabeler` in `tests/WorldZones.Regions.Tests/ComponentLabelerTests.cs` — test with small synthetic grids: two islands separated by deep water get distinct land component IDs; land bridge merges into one component; shelf connects across shallow water; deep zones excluded from all components
- [ ] T012 [US1] Create basic `DebugOverlayExporter` in `src/WorldZones.Regions/DebugOverlayExporter.cs` — exports `land_components.png` (each land component a distinct color, deep/shallow transparent or gray) and `shelf_components.png` (each shelf component a distinct color); coordinate mapping matches Feature 001 biome map convention

**Checkpoint**: Land and shelf components identified. Overlays visually validate coastlines and islands.

---

## Phase 4: User Story 2 — Detect Archipelago Candidates (Priority: P2)

**Goal**: Flag shelf components that contain multiple small land components as archipelago candidates.

**Independent Test**: Generate a world with scattered islands, run detection, verify clusters are flagged while continents are not.

### Implementation for User Story 2

- [ ] T013 [P] [US2] Create `ArchipelagoCandidate` record/class in `src/WorldZones.Regions/ArchipelagoCandidate.cs` — holds candidate ID, parent ShelfComponent ID, list of LandComponent IDs, total land area, dominant land component share percentage
- [ ] T014 [US2] Implement `ArchipelagoDetector` in `src/WorldZones.Regions/ArchipelagoDetector.cs` — iterates ShelfComponents; flags as archipelago candidate if shelf contains ≥ N distinct land components AND no single land component exceeds X% of total land area in the shelf; N and X% are configurable parameters
- [ ] T015 [US2] Test `ArchipelagoDetector` in `tests/WorldZones.Regions.Tests/ArchipelagoDetectorTests.cs` — synthetic cases: shelf with 3 small islands → flagged; shelf with 1 dominant + 2 tiny → not flagged (dominant exceeds threshold); shelf with 1 land component → not flagged (below minimum count)
- [ ] T016 [US2] Add `archipelago_candidates.png` export to `DebugOverlayExporter` — archipelago member islands highlighted with shared color and bounding outline; non-archipelago components shown in neutral gray

**Checkpoint**: Archipelago candidates detected and visualized. Metadata only — no regions created.

---

## Phase 5: User Story 3 — Generate Proto-Regions (Priority: P3)

**Goal**: Partition qualifying land zones into proto-regions via per-component seeded BFS. Small land components become minor islets (metadata only).

**Independent Test**: Generate a world, run proto-region generation, verify every qualifying land zone assigned to exactly one region, no region crosses deep water, minor islets tracked, export proto_seeds.png and proto_regions.png.

### Implementation for User Story 3

- [x] T017 [P] [US3] Create `ProtoRegion` class in `src/WorldZones.Regions/ProtoRegion.cs` — holds region ID, seed coordinate (Vector2i), area (zone count, settable for merge updates)
- [x] T017a [P] [US3] Create `MinorIslet` class in `src/WorldZones.Regions/MinorIslet.cs` — holds LandComponentId and AreaZones for components below proto-region threshold
- [x] T018 [US3] ~~WeightedZoneDistance~~ **Deferred** — v0 uses land-only BFS (cost=1 uniform) instead of Dijkstra with configurable shallow cost. Weighted distance planned for future iteration when shallow traversal is introduced.
- [x] T019 [US3] Implement `ProtoRegionGenerator` in `src/WorldZones.Regions/ProtoRegionGenerator.cs` — per-component seeding: partitions land components into qualifying (≥ MinComponentZonesForProto) vs minor islets (< threshold); distributes seeds per component via farthest-point heuristic; runs multi-source BFS land-only; merges tiny regions (< minRegionZones) into longest-border neighbor; produces ProtoRegionResult with regions, seeds, stats, and minor islet metadata
- [x] T020 [US3] Test `ProtoRegionGenerator` in `tests/WorldZones.Regions.Tests/ProtoRegionGeneratorTests.cs` — 16 tests: all qualifying land zones assigned; deep/shallow zones excluded; two-island separation; determinism; contiguity; minor islet classification; merge behavior; stats consistency; custom thresholds
- [x] T021 [US3] Add `proto_seeds.png` and `proto_regions.png` export to `LandComponentExporter` (Unity Editor) — seeds as red dots on muted land; regions as solid color fills per region

**Checkpoint**: Proto-regions generated and visualized. 164 regions, 993 minor islets. All invariants hold.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Integration, validation, and cleanup across all stories.

- [ ] T022 [P] Add XML documentation comments to all public types and methods in `src/WorldZones.Regions/`
- [ ] T023 [P] Update Unity `.asmdef` at `tests/unity/Assets/Tests/WorldZones.WorldGen.Tests.Unity.asmdef` to add `WorldZones.Regions.dll` to `precompiledReferences`
- [ ] T024 Update build scripts (`scripts/Run-UnityTestSuite.ps1`, `scripts/Export-BiomeMap.ps1`) to also build and copy `WorldZones.Regions.dll` to `tests/unity/Assets/Plugins/`
- [ ] T025 Run full-world integration test with known seed (e.g., "HHcLC5acQt") — generate all 4 overlay PNGs, visually validate against biome map: coastlines align, archipelagos plausible, territories don't cross oceans, Ashlands/Deep North naturally isolate
- [ ] T026 Verify determinism — run twice with same seed, diff all outputs, confirm identical

**Checkpoint**: Feature complete. All overlays exported, all invariants verified, deterministic.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — produces components needed by US2 and US3
- **US2 (Phase 4)**: Depends on Phase 3 (needs ShelfComponents with LandComponent mappings)
- **US3 (Phase 5)**: Depends on Phase 3 (needs component data for seed placement and boundary enforcement)
- **Polish (Phase 6)**: Depends on Phases 3–5

### Within Each Phase

- Tasks marked [P] can run in parallel (different files, no dependencies)
- Models/types before algorithms
- Algorithms before tests
- Core implementation before overlay export

### Parallel Opportunities per Phase

**Phase 2**: T004 and T005 in parallel (enum + grid are independent files)
**Phase 3**: T008 and T009 in parallel (LandComponent + ShelfComponent are independent types)
**Phase 4**: T013 can start before T014 (type before algorithm)
**Phase 5**: T017 can start before T018 (type before algorithm)
**Phase 6**: T022 and T023 in parallel (docs + asmdef are independent)

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (zone grid + classifier)
3. Complete Phase 3: User Story 1 (land + shelf components + overlays)
4. **STOP and VALIDATE**: Inspect land_components.png and shelf_components.png
5. If plausible → proceed to US2 and US3

### Incremental Delivery

1. Setup + Foundational → Zone grid with DepthClass
2. Add US1 → Component overlays (MVP!)
3. Add US2 → Archipelago detection overlays
4. Add US3 → Proto-territory overlays
5. Polish → Full integration validation + determinism check

---

## Notes

- All zone coordinates use Valheim's `ZoneSystem` convention: `Vector2i` with origin at world center, 64m spacing
- Elevation thresholds for DepthClass are configurable but start with Valheim defaults (sea level ≈ 0.05 base height)
- Overlay PNGs must use same coordinate-to-pixel mapping as Feature 001's biome map exports
- No tests are strictly TDD (red-green-refactor); tests are written alongside implementation per constitution III
- US2 and US3 both depend on US1 — they cannot be parallelized across stories, but tasks within each story can be parallelized
