# Feature Specification: Valheim World Generator Library

**Feature Branch**: `001-valheim-worldgen-library`  
**Created**: 2025-01-22  
**Status**: Draft  
**Input**: User description: "Create a synthetic Valheim world generator that replicates the game's biome and heightmap generation system for testing and prototyping region algorithms. The generator should accept a world seed, calculate base height using Perlin noise, determine biomes using the game's procedural algorithm, and export biome maps as PNG images for visual validation against existing online tools. This is a pure C# library component (no Unity dependencies) that ports the core worldgen logic from the decompiled Valheim source code to enable realistic testing of future region generation algorithms."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate World from Seed (Priority: P1)

A developer working on region algorithms needs to generate a synthetic Valheim world using a specific seed to test their region generation logic against realistic biome distributions and terrain data.

**Why this priority**: This is the core capability - without the ability to generate worlds from seeds, the library provides no value. This enables all other functionality and represents the minimum viable product.

**Independent Test**: Can be fully tested by providing a known seed value (e.g., "TestSeed123"), generating the world data, and verifying that heightmap and biome data structures are populated with valid values for any requested world coordinates.

**Acceptance Scenarios**:

1. **Given** a valid world seed string, **When** developer initializes the world generator with that seed, **Then** the generator produces deterministic heightmap values for any queried world coordinates
2. **Given** the same seed used twice, **When** developer queries the same world coordinates in both instances, **Then** both queries return identical height and biome values
3. **Given** different seed strings, **When** developer generates worlds from each seed, **Then** each world produces unique biome distributions and terrain patterns
4. **Given** a world generator instance, **When** developer queries coordinates at world boundaries (e.g., ±10000 units), **Then** the system returns valid height and biome data without errors

---

### User Story 2 - Export Biome Maps for Validation (Priority: P2)

A developer needs to visually verify that the synthetic world generation matches Valheim's actual behavior by exporting biome maps as PNG images and comparing them against known reference maps from existing online Valheim world generators.

**Why this priority**: Visual validation is critical for verifying correctness, but the library can provide value through programmatic access alone. This enables confidence in the implementation before using it for algorithm testing.

**Independent Test**: Can be fully tested by generating a world from a known seed (e.g., "42"), exporting a biome map for a defined region (e.g., 2000x2000 world units centered at origin), and manually comparing the resulting PNG against reference maps from online tools for the same seed.

**Acceptance Scenarios**:

1. **Given** a generated world and a coordinate region, **When** developer exports a biome map, **Then** the system produces a PNG image where each pixel represents a biome type using distinct colors
2. **Given** a world generated from seed "TestWorld", **When** developer exports a biome map and compares it to online Valheim map generators using the same seed, **Then** biome boundaries and distributions visually match the reference maps
3. **Given** a request to export a large region (e.g., 5000x5000 units), **When** the export completes, **Then** the resulting image maintains spatial accuracy with consistent pixel-to-world-unit scaling
4. **Given** multiple export requests for overlapping regions, **When** developer examines the exported images, **Then** overlapping areas show identical biome patterns

---

### User Story 3 - Query Terrain Data for Algorithm Testing (Priority: P3)

A developer testing region generation algorithms needs to query height and biome information for specific world coordinates or rectangular areas to validate that their algorithms produce sensible results given realistic terrain data.

**Why this priority**: This enables the primary use case (testing region algorithms) but depends on the basic world generation being functional. It provides convenient access patterns optimized for algorithm testing workflows.

**Independent Test**: Can be fully tested by generating a world, querying height and biome data for individual coordinates and rectangular regions, and verifying that returned data is consistent with the underlying world generation (e.g., querying a 100x100 region returns 10,000 height values matching individual coordinate queries).

**Acceptance Scenarios**:

1. **Given** a generated world, **When** developer queries height at specific coordinates (x, z), **Then** the system returns a numeric height value representing terrain elevation
2. **Given** a generated world, **When** developer queries biome at specific coordinates, **Then** the system returns a biome type identifier (e.g., Meadows, BlackForest, Ocean)
3. **Given** a rectangular region defined by coordinate bounds, **When** developer requests batch data for that region, **Then** the system returns height and biome data for all coordinates within the region
4. **Given** coordinates in Ocean biomes, **When** developer queries height values, **Then** returned values are below sea level threshold
5. **Given** coordinates in Mountain biomes, **When** developer queries height values, **Then** returned values are above mountain elevation threshold

---

### Edge Cases

- What happens when querying coordinates at extreme distances from world origin (e.g., ±50000 units)?
- How does the system handle invalid or empty seed strings?
- What occurs when export dimensions would produce extremely large PNG files (e.g., 20000x20000 pixels)?
- How are biome transitions represented when a pixel boundary falls between two biomes?
- What happens when querying a rectangular region that extends beyond typical playable world bounds?
- How does the generator handle seed strings with special characters or non-ASCII text?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST accept a world seed as input and use it to initialize the procedural generation algorithms
- **FR-002**: System MUST calculate terrain height values for any world coordinates using Perlin noise-based generation matching Valheim's heightmap algorithm
- **FR-003**: System MUST determine biome type for any world coordinates using the procedural biome placement algorithm from Valheim
- **FR-004**: System MUST provide deterministic output (same seed always produces identical results)
- **FR-005**: System MUST support querying height values for individual world coordinates
- **FR-006**: System MUST support querying biome types for individual world coordinates
- **FR-007**: System MUST support querying height and biome data for rectangular coordinate regions
- **FR-008**: System MUST export biome maps as PNG images for specified coordinate regions
- **FR-009**: System MUST use distinct, visually identifiable colors for each biome type in exported images
- **FR-010**: System MUST operate as a standalone library without dependencies on Unity or game-specific frameworks
- **FR-011**: System MUST replicate the core worldgen logic from Valheim's procedural generation algorithms
- **FR-012**: System MUST handle world coordinates within Valheim's typical playable area (approximately ±10000 units from origin)
- **FR-013**: System MUST identify all major biome types present in Valheim (Meadows, BlackForest, Swamp, Mountain, Plains, Ocean, Mistlands, Ashlands, DeepNorth)
- **FR-014**: System MUST calculate biome placement based on distance from world origin and elevation, matching Valheim's progression pattern
- **FR-015**: Exported PNG images MUST maintain accurate spatial mapping where pixel positions correspond to world coordinates at a consistent scale

### Key Entities

- **World Seed**: String identifier used to initialize random number generators for deterministic world generation; same seed produces identical worlds
- **World Coordinates**: Two-dimensional (x, z) position in the game world coordinate system measured in world units
- **Heightmap**: Continuous elevation data across the world surface represented as numeric height values at any coordinate
- **Biome**: Named environmental zone type (e.g., Meadows, Mountain, Ocean) determined by distance from origin, elevation, and procedural placement rules
- **Biome Map**: Two-dimensional spatial representation showing biome distribution across a world region, exportable as color-coded image
- **Coordinate Region**: Rectangular area defined by minimum and maximum (x, z) coordinates used for batch queries and exports

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Full world biome map generation completes in under 1 minute on standard development hardware
- **SC-002**: Biome maps exported from the library are visually acceptable to the project owner when compared against reference maps from existing online Valheim world generators using identical seeds
- **SC-003**: System generates identical output for the same seed across multiple executions with 100% consistency
- **SC-004**: Library successfully generates valid terrain data for coordinates within ±10000 units of world origin without errors or crashes
- **SC-005**: Exported biome maps are immediately interpretable with distinct colors for each biome type, allowing visual identification without reference documentation

## Assumptions

- The decompiled Valheim source code provides sufficient detail to replicate the core worldgen algorithms accurately
- Perlin noise implementation in standard C# libraries is compatible with Valheim's noise generation approach
- World coordinates beyond ±10000 units are not required for region algorithm testing scenarios
- PNG image format is sufficient for biome map visualization (no need for other image formats)
- Developers using this library have access to existing online Valheim world generators for validation purposes
- The library focuses on terrain and biome generation; other world features (structures, spawn points, resource locations) are out of scope
- Standard sea level and elevation thresholds from Valheim are well-documented or can be determined from decompiled source
- Color mapping for biomes can use arbitrary distinct colors; exact color matching to the game's rendering is not required
- C# standard libraries or readily available NuGet packages provide sufficient math primitives for Perlin noise and vector calculations; Unity-specific math types can be replicated or substituted if needed
- The library will be consumed programmatically and includes a non-interactive command-line tool for generating biome maps; interactive UI is not part of this feature

## Dependencies

- Access to decompiled Valheim source code or comprehensive documentation of the worldgen algorithms
- C# development environment with PNG image generation capability
- Existing online Valheim world generators available for validation testing
- Understanding of Perlin noise parameters and scaling used in Valheim's terrain generation

## Out of Scope

- Generation of game structures (dungeons, villages, bosses, altars)
- Resource and enemy spawn point generation
- Vegetation placement algorithms
- Real-time rendering or 3D visualization of generated worlds
- Integration with Unity game engine
- Modification or patching of the actual Valheim game
- Multiplayer world synchronization
- Save file generation or import/export
- Performance optimization for real-time game usage (library is for testing/prototyping only)
- Biome-specific feature generation (terrain details, rock formations, etc.)
