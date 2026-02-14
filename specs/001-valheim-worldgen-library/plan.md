# Implementation Plan: Valheim World Generator Library

**Branch**: `001-valheim-worldgen-library` | **Date**: 2026-02-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-valheim-worldgen-library/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Create a pure C# library that ports Valheim's world generation algorithms (heightmap and biome calculation) from decompiled source code at `C:\Users\dangreen\projects\valheim\disassembly\0.221.10\`. The library must operate without Unity or game runtime dependencies, accept world seeds for deterministic generation, query terrain height and biome data for any world coordinates, and export biome maps as PNG images for visual validation against online tools. This enables realistic testing of future region generation algorithms in isolation from the game environment, adhering to Library-First Architecture and TDD principles from the project constitution.

## Technical Context

**Language/Version**: C# 7.3 (Unity 2019 compatibility for Valheim)  
**Primary Dependencies**: 
- System.Drawing or ImageSharp for PNG export (NEEDS CLARIFICATION - which library for .NET Framework 4.7.2?)
- Perlin noise implementation (NEEDS CLARIFICATION - port DUtils.PerlinNoise or use existing library?)
- No Unity/BepInEx dependencies (core library must be pure C#)

**Storage**: Not applicable (in-memory world generation only)  
**Testing**: NUnit 3.x or xUnit (NEEDS CLARIFICATION - project preference?)  
**Target Platform**: .NET Framework 4.7.2 (Valheim/Unity 2019 compatibility)  
**Project Type**: Single library project with optional CLI tool for PNG export  
**Performance Goals**: 
- Full world biome map generation (<10000x10000 units) completes in <1 minute
- Individual coordinate queries complete in <1ms
- Support batch queries for rectangular regions

**Constraints**: 
- Must match Valheim's exact world generation algorithm (ported from decompiled source)
- Deterministic output (same seed = identical results, 100% consistency)
- No Unity math types (Vector3, Mathf) - must use standard .NET types or custom implementations
- C# 7.3 language features only (Unity 2019 limitation)

**Scale/Scope**: 
- Support world coordinates within ±10000 units (typical playable area)
- Handle 9 biome types (Meadows, BlackForest, Swamp, Mountain, Plains, Ocean, Mistlands, Ashlands, DeepNorth)
- Export biome maps up to 5000x5000 world units per image
- Library size <1MB, minimal external dependencies

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Library-First Architecture
- ✅ **PASS**: Core worldgen logic (heightmap, biome calculation, Perlin noise) will be implemented in `WorldZones.WorldGen` library with ZERO Unity/BepInEx dependencies
- ✅ **PASS**: Optional PNG export utility can be separate CLI tool or included in library using System.Drawing/ImageSharp (no Unity dependencies)
- ✅ **PASS**: All core algorithms testable without game runtime or Unity editor
- ✅ **POST-DESIGN VERIFICATION**: Architecture documented in plan.md shows clean separation - core library has no external dependencies

### II. Hybrid Testing Strategy (NON-NEGOTIABLE)
- ✅ **PASS - TDD Required**: Core generation algorithms (GetBiome, GetBaseHeight, Perlin noise) MUST use Test-Driven Development
  - Tests written first using known seed values and expected biome/height outputs
  - User approves test specifications before implementation
  - Red-Green-Refactor workflow enforced
- ✅ **PASS - Pragmatic**: PNG export functionality MAY be tested after implementation (file I/O, image encoding)
- ✅ **PASS - API Coverage**: All public query methods (GetBiome, GetHeight, batch queries) MUST have contract tests
- ✅ **POST-DESIGN VERIFICATION**: Contract tests defined in contracts/WorldGenerator.md covering all public APIs

### III. Stable Public API
- ✅ **PASS**: Initial version 0.1.0 (pre-release)
- ✅ **PASS**: Public API designed for stability:
  - `WorldGenerator(string seed)` constructor
  - `GetBiome(float x, float z)` query method
  - `GetHeight(float x, float z)` query method
  - `ExportBiomeMap(RectangleRegion, string outputPath)` optional export
- ✅ **PASS**: Breaking changes will follow deprecation policy once version reaches 1.0.0
- ✅ **POST-DESIGN VERIFICATION**: API contract documented with versioning policy, performance contracts, and breaking change policy

### IV. Simplicity Bias
- ✅ **PASS**: Direct port of Valheim algorithms - no custom frameworks or abstractions
- ✅ **PASS**: Single library project structure (no repository patterns, DI, or service layers)
- ✅ **PASS**: Straightforward Perlin noise implementation (either port DUtils or use vetted library)
- ✅ **PASS**: PNG export uses standard library (System.Drawing) or minimal dependency (ImageSharp)
- ✅ **POST-DESIGN VERIFICATION**: Research.md confirms custom Perlin implementation (0 dependencies), ImageSharp only in CLI tool, no unnecessary abstractions

### V. Clear Contracts
- ✅ **PASS**: All public APIs MUST have XML documentation
- ✅ **PASS**: Thread safety documented (core generation is deterministic and thread-safe per instance)
- ✅ **PASS**: Performance contracts specified (coordinate queries <1ms, batch generation <1min for full map)
- ✅ **PASS**: Document seed format, coordinate system, and biome enum values
- ✅ **POST-DESIGN VERIFICATION**: contracts/WorldGenerator.md provides complete API documentation including thread safety, performance, error handling, and examples

### VI. Iterative Development Process (NON-NEGOTIABLE)
- ✅ **PASS**: Planned iterations:
  1. Investigation: Research Perlin noise libraries, PNG export options, Unity math type replacements (✓ COMPLETED in research.md)
  2. Iteration 1: Implement Perlin noise and GetBaseHeight (TDD with known values)
  3. Iteration 2: Implement GetBiome algorithm (TDD with reference maps)
  4. Iteration 3: Add batch query support and validation
  5. Iteration 4: Implement PNG export utility
- ✅ **PASS**: Each iteration delivers testable increment with user approval before proceeding
- ✅ **POST-DESIGN VERIFICATION**: Iteration plan documented, research phase completed, ready for implementation iterations

**GATE STATUS**: ✅ **APPROVED - Phase 1 Complete, Ready for Phase 2 (Task Generation)**

**POST-DESIGN SUMMARY**:
- ✅ All design artifacts generated (data-model.md, contracts/, quickstart.md)
- ✅ Research completed with concrete technology decisions
- ✅ Zero constitution violations
- ✅ Library-first architecture maintained (core = 0 dependencies)
- ✅ API contracts fully documented
- ✅ Agent context updated with technology stack
- ✅ Ready to proceed to `/speckit.tasks` command for implementation task generation

## Project Structure

### Documentation (this feature)

```text
specs/001-valheim-worldgen-library/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── WorldGenerator.md  # Public API contract documentation
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── WorldZones.WorldGen/           # Core world generation library (pure C#)
│   ├── WorldGenerator.cs          # Main API - world seed initialization, biome/height queries
│   ├── PerlinNoise.cs            # Perlin noise implementation (ported from DUtils or adapted)
│   ├── BiomeType.cs              # Enum for 9 biome types
│   ├── MathUtils.cs              # Replacement for Unity Mathf/Vector math (Length, Lerp, etc.)
│   └── WorldZones.WorldGen.csproj
│
├── WorldZones.WorldGen.Cli/       # Optional PNG export command-line tool
│   ├── Program.cs                # CLI entry point
│   ├── BiomeMapExporter.cs       # PNG generation logic
│   └── WorldZones.WorldGen.Cli.csproj
│
└── WorldZones.sln                # Solution file

tests/
├── WorldZones.WorldGen.Tests/
│   ├── PerlinNoiseTests.cs       # TDD tests for noise generation
│   ├── GetBaseHeightTests.cs    # TDD tests for heightmap algorithm
│   ├── GetBiomeTests.cs          # TDD tests for biome placement algorithm
│   ├── WorldGeneratorTests.cs   # Contract tests for public API
│   └── WorldZones.WorldGen.Tests.csproj
│
└── WorldZones.WorldGen.Cli.Tests/  # Optional CLI tests (pragmatic approach)
    ├── BiomeMapExporterTests.cs
    └── WorldZones.WorldGen.Cli.Tests.csproj
```

**Structure Decision**: Single library project structure following Library-First Architecture principle. Core worldgen logic in `WorldZones.WorldGen` (pure C#, no Unity dependencies). Optional CLI tool in separate project for PNG export functionality. Test projects mirror source structure with dedicated test assemblies. No integration/contract subdirectories needed - all tests are unit/contract tests since library has no external dependencies.

## Complexity Tracking

> **No constitution violations** - This feature aligns with all constitutional principles. No justifications required.
