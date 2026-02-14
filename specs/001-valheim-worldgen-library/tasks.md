# Tasks: Valheim World Generator Library

**Feature**: Synthetic Valheim world generator for testing region algorithms  
**Approach**: Test-Driven Development (TDD) with iterative delivery  
**Organization**: Tasks grouped by user story for independent implementation and testing

## Format: `- [ ] [ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Initialize C# library project structure and configure testing framework

- [ ] T001 Create solution file at `WorldZones.slnx` (.slnx format for VS 2026) with .NET Framework 4.7.2 target
- [ ] T002 Create core library project at `src/WorldZones.WorldGen/WorldZones.WorldGen.csproj` (Class Library, .NET Framework 4.7.2)
- [ ] T003 Create test project at `tests/WorldZones.WorldGen.Tests/WorldZones.WorldGen.Tests.csproj` (xUnit, .NET Framework 4.7.2)
- [ ] T004 [P] Download FastNoiseLite.cs from https://github.com/Auburn/FastNoiseLite and copy into `src/WorldZones.WorldGen/FastNoiseLite.cs`
- [ ] T005 [P] Add xUnit and xunit.runner.visualstudio NuGet packages to test project
- [ ] T006 [P] Create .gitignore for C# projects (bin/, obj/, .vs/)
- [ ] T007 [P] Create .editorconfig at repository root with code style rules from constitution (this. qualification, lowercase fields, readonly preferences)
- [ ] T008 Verify solution builds successfully with `dotnet build`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core utilities and math functions required by ALL user stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T008 Create BiomeType enum in `src/WorldZones.WorldGen/BiomeType.cs` with values from data-model.md (None=0, Meadows=1, BlackForest=2, Swamp=4, Mountain=8, Plains=16, Ocean=256, Mistlands=512, AshLands=1024, DeepNorth=2048)
- [ ] T009 [P] Create CoordinateRegion struct in `src/WorldZones.WorldGen/CoordinateRegion.cs` with fields (minX, minZ, maxX, maxZ) and properties (Width, Height)
- [ ] T010 [P] Create MathUtils static class in `src/WorldZones.WorldGen/MathUtils.cs` with Length, Lerp, LerpStep, SmoothStep, Clamp01, Abs methods
- [ ] T011 Write unit tests for MathUtils in `tests/WorldZones.WorldGen.Tests/MathUtilsTests.cs` (test Length, Lerp, Clamp01 edge cases)
- [ ] T012 Verify all foundational tests pass with `dotnet test`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Generate World from Seed (Priority: P1) 🎯 MVP

**Goal**: Accept a world seed and generate deterministic heightmap and biome data for any world coordinates

**Independent Test**: Generate world from seed "TestSeed123", query coordinates (0,0) and (1500,2000), verify heightmap and biome values are populated and deterministic across multiple runs

### TDD Tests for User Story 1 (Written FIRST, Must FAIL before implementation)

- [ ] T013 [P] [US1] Write GetBaseHeight contract test in `tests/WorldZones.WorldGen.Tests/GetBaseHeightTests.cs` - test that GetBaseHeight returns values in range [-2.0, 2.0] for origin (0,0)
- [ ] T014 [P] [US1] Write GetBaseHeight determinism test in `tests/WorldZones.WorldGen.Tests/GetBaseHeightTests.cs` - test same seed produces identical height at (500, 750) across 3 separate WorldGenerator instances
- [ ] T015 [P] [US1] Write GetBaseHeight Perlin integration test in `tests/WorldZones.WorldGen.Tests/GetBaseHeightTests.cs` - test that height varies smoothly between (0,0) and (1000,1000) using multiple sample points
- [ ] T016 [P] [US1] Write GetBiome origin test in `tests/WorldZones.WorldGen.Tests/GetBiomeTests.cs` - test that GetBiome(0,0) always returns BiomeType.Meadows for any seed
- [ ] T017 [P] [US1] Write GetBiome ocean test in `tests/WorldZones.WorldGen.Tests/GetBiomeTests.cs` - test that GetBiome(11000, 0) returns BiomeType.Ocean (beyond world edge at 10500 units)
- [ ] T018 [P] [US1] Write GetBiome determinism test in `tests/WorldZones.WorldGen.Tests/GetBiomeTests.cs` - test same seed produces identical biome at (3000, 4000) across multiple instances
- [ ] T019 [P] [US1] Write WorldGenerator constructor test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that constructor with seed "TestSeed123" initializes without errors
- [ ] T020 [P] [US1] Write WorldGenerator null seed test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that constructor throws ArgumentNullException for null seed
- [ ] T021 [P] [US1] Write WorldGenerator empty seed test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that constructor accepts empty string as valid seed
- [ ] T022 Run all tests - verify they FAIL (red phase of TDD) with `dotnet test`

### Implementation for User Story 1 (Make tests GREEN)

- [ ] T023 [P] [US1] Implement seed hashing in `src/WorldZones.WorldGen/WorldGenerator.cs` - add GetStableHashCode method to compute deterministic hash from seed string
- [ ] T024 [P] [US1] Implement WorldGenerator constructor in `src/WorldZones.WorldGen/WorldGenerator.cs` - initialize offset0-4 and minMountainDistance from seed hash
- [ ] T025 [US1] Implement GetBaseHeight method in `src/WorldZones.WorldGen/WorldGenerator.cs` - use FastNoiseLite Perlin noise with parameters from WorldGenerator.cs (scale 0.002, octaves, etc.)
- [ ] T026 [US1] Implement GetHeight method in `src/WorldZones.WorldGen/WorldGenerator.cs` - call GetBaseHeight with coordinate transformation
- [ ] T027 [US1] Add WorldGenerator Seed property in `src/WorldZones.WorldGen/WorldGenerator.cs` - expose readonly seed string
- [ ] T028 Run GetBaseHeight tests - verify they now PASS (green phase) with `dotnet test --filter "FullyQualifiedName~GetBaseHeightTests"`
- [ ] T029 [US1] Implement GetBiome method in `src/WorldZones.WorldGen/WorldGenerator.cs` - port biome placement algorithm from WorldGenerator.cs (distance checks, noise thresholds, height ranges)
- [ ] T030 [US1] Add biome placement constants in `src/WorldZones.WorldGen/WorldGenerator.cs` - define SEA_LEVEL_THRESHOLD (0.02f), MOUNTAIN_HEIGHT_THRESHOLD (0.4f), distance/noise thresholds from data-model.md
- [ ] T031 [US1] Implement biome distance logic in GetBiome - calculate distance from origin using MathUtils.Length
- [ ] T032 [US1] Implement biome height logic in GetBiome - check sea level for Ocean, mountain threshold for Mountain biome
- [ ] T033 [US1] Implement biome noise-based placement in GetBiome - use FastNoiseLite for Swamp, Plains, Mistlands, BlackForest placement with noise thresholds
- [ ] T034 [US1] Implement GetBiome overload with custom oceanLevel and waterAlwaysOcean parameters in `src/WorldZones.WorldGen/WorldGenerator.cs`
- [ ] T035 Run all User Story 1 tests - verify they PASS with `dotnet test`
- [ ] T036 [US1] Add XML documentation comments to all public members in `src/WorldZones.WorldGen/WorldGenerator.cs` per contracts/WorldGenerator.md specification
- [ ] T037 [US1] Validate against reference seed "42" - manually verify GetBiome(0,0) returns Meadows, document known coordinates in test comments

**Checkpoint**: User Story 1 complete - can generate deterministic worlds and query biomes/heights. Test independently before proceeding.

---

## Phase 4: User Story 2 - Export Biome Maps for Validation (Priority: P2)

**Goal**: Export biome maps as PNG images for visual validation against online Valheim map generators

**Independent Test**: Generate world from seed "42", export 2000x2000 biome map centered at origin, manually compare resulting PNG against online tool at https://valheim-map.world/ for seed "42"

### TDD Tests for User Story 2 (Written FIRST)

- [ ] T038 [P] [US2] Write GetBiomeMap contract test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that GetBiomeMap returns correct dimensions (100x100) for region minX=-50, minZ=-50, maxX=50, maxZ=50
- [ ] T039 [P] [US2] Write GetBiomeMap data consistency test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that biome at array[50,50] matches GetBiome(0,0) for origin-centered region
- [ ] T040 [P] [US2] Write GetBiomeMap invalid region test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that ArgumentException is thrown when maxX <= minX
- [ ] T041 [P] [US2] Write GetBiomeMap determinism test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test same region returns identical 2D array across multiple calls
- [ ] T042 Run User Story 2 tests - verify they FAIL with `dotnet test --filter "FullyQualifiedName~GetBiomeMap"`

### Implementation for User Story 2 (Core Library)

- [ ] T043 [US2] Implement GetBiomeMap method in `src/WorldZones.WorldGen/WorldGenerator.cs` - iterate over CoordinateRegion, call GetBiome for each integer coordinate, return BiomeType[,] array
- [ ] T044 [US2] Add region validation in GetBiomeMap - throw ArgumentException if maxX <= minX or maxZ <= minZ
- [ ] T045 [US2] Add CoordinateRegion validation properties in `src/WorldZones.WorldGen/CoordinateRegion.cs` - IsValid property checking max > min
- [ ] T046 Run User Story 2 core tests - verify they PASS with `dotnet test --filter "FullyQualifiedName~GetBiomeMap"`

### Implementation for User Story 2 (CLI Tool for PNG Export)

- [ ] T047 Create CLI project at `src/WorldZones.WorldGen.Cli/WorldZones.WorldGen.Cli.csproj` (Console App, .NET Framework 4.7.2)
- [ ] T048 Add project reference from CLI to WorldGen library in `src/WorldZones.WorldGen.Cli/WorldZones.WorldGen.Cli.csproj`
- [ ] T049 Add ImageSharp 1.x NuGet package to CLI project
- [ ] T050 [P] [US2] Create BiomeMapExporter class in `src/WorldZones.WorldGen.Cli/BiomeMapExporter.cs` with ExportPng method accepting WorldGenerator, CoordinateRegion, outputPath
- [ ] T051 [P] [US2] Implement biome-to-color mapping in `src/WorldZones.WorldGen.Cli/BiomeMapExporter.cs` - define color constants (Meadows=Green, Ocean=Blue, Mountain=White, etc.)
- [ ] T052 [US2] Implement PNG generation in BiomeMapExporter.ExportPng - call GetBiomeMap, convert BiomeType array to ImageSharp Image<Rgb24>, save to file
- [ ] T053 [US2] Implement CLI argument parsing in `src/WorldZones.WorldGen.Cli/Program.cs` - parse --seed, --region (minX,minZ,maxX,maxZ), --output flags
- [ ] T054 [US2] Implement CLI main logic in Program.cs - initialize WorldGenerator from seed, create CoordinateRegion from args, call BiomeMapExporter.ExportPng
- [ ] T055 [US2] Add error handling in CLI - catch exceptions, display user-friendly error messages
- [ ] T056 Test CLI tool manually - run `dotnet run --project src/WorldZones.WorldGen.Cli -- --seed "42" --region -1000,-1000,1000,1000 --output test.png`, verify PNG created
- [ ] T057 [US2] Validate exported map visually - compare test.png from seed "42" against https://valheim-map.world/ reference map
- [ ] T058 [US2] Document CLI usage in README.md at repository root - add usage examples, parameters, color legend

**Checkpoint**: User Story 2 complete - can export and visually validate biome maps. Both US1 and US2 should work independently.

---

## Phase 5: User Story 3 - Query Terrain Data for Algorithm Testing (Priority: P3)

**Goal**: Provide convenient batch query methods for height and biome data over rectangular regions to support region algorithm testing

**Independent Test**: Generate world, query 100x100 region, verify returned 10,000 height values match individual GetHeight calls and are consistent with GetBiomeMap results

### TDD Tests for User Story 3 (Written FIRST)

- [ ] T059 [P] [US3] Write GetHeightMap contract test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that GetHeightMap returns correct dimensions (100x100) for region
- [ ] T060 [P] [US3] Write GetHeightMap data consistency test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test that height at array[50,50] matches GetHeight(0,0) for origin-centered region
- [ ] T061 [P] [US3] Write GetHeightMap range validation test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test all returned heights are in range [-2.0, 2.0]
- [ ] T062 [P] [US3] Write ocean height validation test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test coordinates where GetBiome returns Ocean have height < 0.02
- [ ] T063 [P] [US3] Write mountain height validation test in `tests/WorldZones.WorldGen.Tests/WorldGeneratorTests.cs` - test coordinates where GetBiome returns Mountain have height > 0.4
- [ ] T064 [P] [US3] Write batch query performance test in `tests/WorldZones.WorldGen.Tests/PerformanceTests.cs` - test that GetBiomeMap and GetHeightMap for 1000x1000 region complete in <2 seconds
- [ ] T065 Run User Story 3 tests - verify they FAIL with `dotnet test --filter "FullyQualifiedName~GetHeightMap|PerformanceTests"`

### Implementation for User Story 3

- [ ] T066 [US3] Implement GetHeightMap method in `src/WorldZones.WorldGen/WorldGenerator.cs` - iterate over CoordinateRegion, call GetHeight for each coordinate, return float[,] array
- [ ] T067 [US3] Add region validation in GetHeightMap - reuse validation logic from GetBiomeMap
- [ ] T068 [US3] Optimize batch queries if needed - profile GetBiomeMap and GetHeightMap, optimize Perlin noise calls if performance target not met
- [ ] T069 Run User Story 3 tests - verify they PASS with `dotnet test --filter "FullyQualifiedName~GetHeightMap|PerformanceTests"`
- [ ] T070 [US3] Add XML documentation for GetHeightMap in `src/WorldZones.WorldGen/WorldGenerator.cs`
- [ ] T071 [US3] Update contracts/WorldGenerator.md with GetHeightMap API specification
- [ ] T072 [US3] Add GetHeightMap usage example to quickstart.md

**Checkpoint**: All 3 user stories complete - library provides full world generation, export, and batch query capabilities

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and quality improvements across all features

- [ ] T073 [P] Create README.md at repository root - overview, installation, quick start, links to docs
- [ ] T074 [P] Create LICENSES.md documenting FastNoiseLite MIT license and ImageSharp Apache 2.0 license
- [ ] T075 [P] Add code comments for complex algorithms in WorldGenerator.cs - document biome placement logic, noise parameters
- [ ] T076 Validate quickstart.md examples - run each code example, verify outputs match documentation
- [ ] T077 [P] Add example project at `examples/BasicUsage/` demonstrating WorldGenerator usage from quickstart.md
- [ ] T078 Run full test suite - verify all tests pass with `dotnet test`
- [ ] T079 Build release configuration - verify clean build with `dotnet build -c Release`
- [ ] T080 [P] Add performance benchmarks in `tests/WorldZones.WorldGen.Tests/BenchmarkTests.cs` - measure and document actual timings for 1000x1000, 5000x5000, 10000x10000 regions
- [ ] T081 [P] Security review - verify no seed injection vulnerabilities, no path traversal in CLI tool
- [ ] T082 Create NuGet package specification at `src/WorldZones.WorldGen/WorldZones.WorldGen.nuspec` with metadata (version 0.1.0, description, dependencies)
- [ ] T083 Final validation against reference maps - test seeds "42", "TestWorld", verify biome boundaries match online generators within ±2 world units

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on T001-T007 (Setup) completion - BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on T008-T012 (Foundational) completion
  - User stories can proceed in parallel once foundation is ready
  - Or sequentially in priority order: US1 (MVP) → US2 → US3
- **Polish (Phase 6)**: Depends on completing desired user stories (minimally US1, ideally US1+US2+US3)

### User Story Dependencies

- **User Story 1 (P1)**: Can start after T012 (Foundational complete) - No dependencies on other stories - THIS IS THE MVP
- **User Story 2 (P2)**: Can start after T012 (Foundational complete) - Depends on US1's WorldGenerator and GetBiomeMap but US2 can be tested independently
- **User Story 3 (P3)**: Can start after T012 (Foundational complete) - Depends on US1's WorldGenerator but adds independent batch query functionality

### Within Each User Story (TDD Workflow)

1. **Write tests FIRST** - All tests must FAIL initially (Red phase)
2. **Implement code** - Make tests PASS (Green phase)
3. **Verify independently** - Each story should work standalone
4. **Document** - Update XML docs and quickstart examples

### Parallel Opportunities

**Setup Phase (T001-T007)**:
- T004 (FastNoiseLite download) can run parallel with T002-T003
- T005-T006 (NuGet, .gitignore) can run in parallel

**Foundational Phase (T008-T012)**:
- T009-T010 (CoordinateRegion, MathUtils) can run in parallel with T008 (BiomeType enum)

**User Story 1 Tests (T013-T021)**:
- All test file creation tasks can run in parallel (different files)

**User Story 1 Implementation**:
- T023-T024 (seed hashing, constructor) can run in parallel

**User Story 2 Tests (T038-T041)**:
- All test methods can run in parallel (same file, different test methods)

**User Story 2 CLI Implementation (T050-T051)**:
- BiomeMapExporter and color mapping can be developed in parallel

**User Story 3 Tests (T059-T065)**:
- All test methods can run in parallel

**Polish Phase (T073-T082)**:
- T073-T075 (documentation) can run in parallel
- T078-T081 (testing, benchmarks, security) can run in parallel

---

## Parallel Example: User Story 1 TDD

```bash
# Launch all test files in parallel (FAIL phase):
Task T013: Write GetBaseHeightTests.cs contract test
Task T014: Write GetBaseHeightTests.cs determinism test  
Task T015: Write GetBaseHeightTests.cs integration test
Task T016: Write GetBiomeTests.cs origin test
Task T017: Write GetBiomeTests.cs ocean test
Task T018: Write GetBiomeTests.cs determinism test
Task T019: Write WorldGeneratorTests.cs constructor test
Task T020: Write WorldGeneratorTests.cs null seed test
Task T021: Write WorldGeneratorTests.cs empty seed test

# Then verify all FAIL with T022

# Launch implementation tasks in parallel where possible:
Task T023: Implement seed hashing (independent)
Task T024: Implement constructor (depends on T023)
```

---

## Implementation Strategy

### Iteration 1: MVP (User Story 1 Only) 🎯

**Goal**: Minimum viable library that can generate worlds and query biomes

1. Complete Phase 1: Setup (T001-T007) → ~30 minutes
2. Complete Phase 2: Foundational (T008-T012) → ~1 hour
3. Complete Phase 3: User Story 1 (T013-T037) → ~4-6 hours (TDD approach)
4. **STOP and VALIDATE**: 
   - Run all tests: `dotnet test`
   - Query known coordinates from seed "42"
   - Verify determinism across multiple runs
5. **MVP READY**: Library can generate worlds programmatically

**Time Estimate**: 1 day of focused development

---

### Iteration 2: Visual Validation (Add User Story 2)

**Goal**: Add PNG export to validate correctness against online tools

1. Start from completed MVP (US1)
2. Complete Phase 4: User Story 2 (T038-T058) → ~3-4 hours
3. **VALIDATE**:
   - Export map from seed "42"
   - Compare against https://valheim-map.world/
   - Verify biome boundaries match
4. **DELIVERABLE**: Library + CLI tool for visual validation

**Time Estimate**: Half day of development

---

### Iteration 3: Batch Queries (Add User Story 3)

**Goal**: Optimize for region algorithm testing workflows

1. Start from MVP + US2
2. Complete Phase 5: User Story 3 (T059-T072) → ~2-3 hours
3. **VALIDATE**:
   - Query 100x100 regions
   - Verify consistency with single-coordinate queries
   - Measure performance benchmarks
4. **DELIVERABLE**: Complete library with all planned features

**Time Estimate**: Half day of development

---

### Iteration 4: Polish & Release

**Goal**: Production-ready library with documentation

1. Complete Phase 6: Polish (T073-T083) → ~2-3 hours
2. **VALIDATE**: Run through quickstart.md as new user
3. **DELIVERABLE**: v0.1.0 release ready for NuGet

**Time Estimate**: Half day of documentation and validation

---

**Total Implementation Time**: 2.5-3 days for complete feature (all 3 user stories + polish)

---

## Validation Checklist

After each user story completion:

- [ ] All tests for that story PASS (`dotnet test`)
- [ ] Story can be tested independently without other stories
- [ ] Public APIs have XML documentation
- [ ] Code matches constitution principles (Library-First, TDD, Simplicity)
- [ ] Performance targets met (query <1ms, batch <1min for 10000x10000)
- [ ] Determinism verified (same seed → same output)

After MVP (User Story 1):
- [ ] Can generate world from any seed
- [ ] GetBiome and GetHeight return valid values
- [ ] Origin (0,0) always returns Meadows
- [ ] Same seed produces identical results across runs

After User Story 2:
- [ ] Can export PNG biome maps
- [ ] Exported maps visually match online tools for seed "42"
- [ ] Color coding is distinct and interpretable

After User Story 3:
- [ ] Can query height and biome data for rectangular regions
- [ ] Batch queries consistent with single-coordinate queries
- [ ] Performance acceptable for 1000x1000+ regions

---

## Notes

- **[P] marker**: Tasks on different files with no dependencies - can run in parallel
- **[Story] label**: Maps task to user story for traceability (US1/US2/US3)
- **TDD approach**: Tests written FIRST, must FAIL, then implement to make them PASS
- **FastNoiseLite**: Single .cs file copied into source - zero NuGet dependencies for core library
- **ImageSharp**: Only in CLI tool project, isolated from core library
- **File paths**: All paths are absolute based on repository root `C:\Users\dangreen\projects\valheim\worldzones`
- **Checkpoints**: Stop after each user story to validate independently before proceeding
- **MVP-first**: User Story 1 alone provides valuable functionality for programmatic world generation
- **Incremental value**: Each story adds capability without breaking previous stories

---

## Success Criteria (from spec.md)

- [ ] **SC-001**: Full world biome map generation completes in under 1 minute (test with T080 benchmarks)
- [ ] **SC-002**: Biome maps visually match online reference maps (validate with T057, T083)
- [ ] **SC-003**: 100% deterministic output for same seed (test with T014, T018, T041)
- [ ] **SC-004**: No errors for coordinates within ±10000 units (test throughout US1)
- [ ] **SC-005**: Exported biome maps have distinct, interpretable colors (validate with T057)
