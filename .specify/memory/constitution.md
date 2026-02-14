<!--
SYNC IMPACT REPORT - Constitution v1.0.0
════════════════════════════════════════════════════════════════════════════════
VERSION CHANGE: Initial version → 1.0.0
BUMP RATIONALE: Initial constitution ratification for WorldZones project

PRINCIPLES ESTABLISHED:
  ✓ I. Library-First Architecture (new)
  ✓ II. Hybrid Testing Strategy (new)
  ✓ III. Stable Public API (new)
  ✓ IV. Simplicity Bias (new)
  ✓ V. Clear Contracts (new)

SECTIONS ADDED:
  ✓ Technology Stack
  ✓ Quality Gates
  ✓ Governance

TEMPLATE SYNC STATUS:
  ✅ plan-template.md - Constitution Check section validated
  ✅ spec-template.md - Requirements alignment validated
  ✅ tasks-template.md - Task categorization validated
  ⚠️  Command templates - Pending verification in .specify/templates/commands/

FOLLOW-UP ITEMS:
  • Verify command template files exist and update if needed
  • Establish initial test coverage baseline metrics
  • Document offline test execution procedures in project README

SUGGESTED COMMIT MESSAGE:
  docs: ratify WorldZones constitution v1.0.0 (initial governance framework)

════════════════════════════════════════════════════════════════════════════════
-->

# WorldZones Constitution

**Project**: WorldZones - Foundational library mod for Valheim that dynamically divides the game
map into named regions for use by other mods.

## Core Principles

### I. Library-First Architecture

**Rules**:
- Core logic (region generation, naming algorithms, geography processing) MUST be implemented
  as standalone C# libraries with ZERO Unity or BepInEx dependencies.
- Unity-dependent code (biome detection, map integration, game hooks) MUST be isolated in
  dedicated adapter/integration layers.
- The BepInEx plugin acts as a thin integration layer that composes core libraries and game
  integration adapters.
- No core library may reference UnityEngine, BepInEx, or Valheim game assemblies.
- Core libraries MUST be testable without game runtime or Unity editor.

**Rationale**: Downstream modders depend on this library. Clean separation enables fast unit
testing of complex algorithms, reduces coupling to game updates, and allows other projects to
reuse core logic without Unity baggage.

### II. Hybrid Testing Strategy (NON-NEGOTIABLE)

**Rules - TDD (Strict) for Core Logic**:
- All region generation algorithms, naming systems, and geography processing MUST be developed
  using Test-Driven Development.
- Tests MUST be written first, user-approved, confirmed to fail, then implemented (Red-Green-Refactor).
- No implementation of core logic without prior failing tests.
- Core logic test suite MUST run without game runtime, Unity, or BepInEx.

**Rules - Pragmatic Testing for Game Integration**:
- Unity hooks, biome detection, and map system integration require pragmatic testing approach.
- Integration tests MAY be written after implementation when runtime dependencies make TDD impractical.
- All integration code MUST have test coverage, but timing of test writing is flexible.
- Document integration test execution requirements (runtime dependencies, manual steps).

**Rules - Public API Coverage**:
- Every public API exposed to downstream mods MUST have test coverage (contract tests).
- API behavior, error handling, and edge cases MUST be verified.
- Breaking changes MUST be caught by failing contract tests.

**Rationale**: High-quality core algorithms are critical for downstream stability. TDD ensures
correctness. Pragmatic approach for game integration avoids productivity loss from mocking
Unity/BepInEx. Public API tests protect downstream modders from breaking changes.

### III. Stable Public API

**Rules - Semantic Versioning**:
- Version format: MAJOR.MINOR.PATCH (e.g., 2.1.3).
- MAJOR: Breaking changes to public APIs, removed functionality, incompatible behavior changes.
- MINOR: New features, new public APIs, backward-compatible enhancements.
- PATCH: Bug fixes, performance improvements, internal refactoring (no API changes).

**Rules - Deprecation Policy**:
- Breaking changes MUST be preceded by deprecation warnings in a prior MINOR version.
- Deprecated APIs MUST remain functional for at least one MINOR version cycle.
- Deprecation notices MUST include migration guidance and alternative APIs.
- Mark deprecated members with `[Obsolete("Migration message", error: false)]`.

**Rules - Changelog Requirements**:
- Every version MUST have a changelog entry in `CHANGELOG.md`.
- Breaking changes MUST be prominently documented with migration examples.
- New APIs MUST include usage examples.
- Bug fixes MUST reference issue numbers if applicable.

**Rationale**: Downstream modders build production mods on this library. Unstable APIs or
surprise breaking changes erode trust and waste downstream development time. Clear versioning
and migration paths are professional courtesy and project hygiene.

### IV. Simplicity Bias

**Rules**:
- YAGNI (You Aren't Gonna Need It) principle is strictly enforced for all non-Unity components.
- Reject premature abstraction, generic frameworks, or speculative features.
- Prefer concrete implementations over abstract patterns unless proven need exists.
- Code complexity MUST be justified in plan.md "Complexity Tracking" section.
- Every layer of abstraction must answer: "What concrete problem does this solve today?"

**Rules - Simplicity Checks**:
- No repository patterns, service layers, or dependency injection frameworks unless justified.
- No reflection-based configuration systems unless required by BepInEx conventions.
- No custom serialization formats; prefer built-in .NET serialization or JSON.
- Avoid inheritance hierarchies deeper than 2 levels without compelling rationale.

**Rationale**: This is a library mod, not an enterprise application. Simple code is debuggable
code. Downstream modders may read this source to understand behavior. Every abstraction layer
increases cognitive load and maintenance burden.

### V. Clear Contracts

**Rules - Public API Documentation**:
- Every public class, method, and property MUST have XML documentation comments.
- Document expected inputs, outputs, exceptions, thread safety, and performance characteristics.
- Complex algorithms MUST include summary explanation and references (if based on published work).
- Public APIs MUST specify whether they are safe to call from background threads.

**Rules - Thread Safety**:
- Document thread safety guarantees explicitly: thread-safe, main-thread-only, or unspecified.
- Core libraries should be thread-safe unless documented otherwise.
- Unity integration layer calls MUST execute on main thread (document this requirement).

**Rules - Performance Contracts**:
- APIs with performance implications MUST document O(n) complexity or expected latency.
- Region generation MUST complete in <1 second for typical map sizes (document map size assumptions).
- No unbounded operations (e.g., infinite loops, unlimited allocations) in public APIs.

**Rationale**: Downstream modders integrate this library blind (no debugger access to our code).
Clear documentation prevents misuse. Thread safety clarity prevents race conditions in multiplayer.
Performance contracts enable modders to make informed decisions about when to call APIs.

### VI. Iterative Development Process (NON-NEGOTIABLE)

**Rules - Iteration Planning**:
- Work MUST be broken into small, planned iterations (single day or less of implementation).
- Each iteration begins with investigation phase: research, prototyping, exploring approaches.
- Agent MUST propose "next iteration" with clear justification before implementation begins.
- User steers and approves iteration plan before any implementation work.
- No multi-day feature branches without explicit approval.

**Rules - Implementation Phase**:
- Implement the SIMPLEST version of the approved iteration (no scope creep).
- Keep changes minimal and focused on the agreed iteration goal.
- Demonstrate tests are appropriate and passing before presenting for review.
- Changes MUST be easy to review (small diffs, clear intent).

**Rules - Acceptance Phase**:
- User reviews implementation for alignment with approved iteration plan.
- User performs manual testing with game client if integration changes are present.
- Only after user acceptance does work proceed to next iteration or merge.
- If iteration reveals unexpected complexity, stop and re-plan rather than expanding scope.

**Rules - No Big-Bang Features**:
- Reject "implement entire feature" tasks in favor of incremental delivery.
- Each iteration should deliver a testable, demonstrable increment.
- Prefer delivering partial but working functionality over complete but untested features.

**Rationale**: Small iterations reduce risk for a foundational library mod. Downstream modders 
depend on stability. Mistakes in big-bang features are costly (wasted downstream dev time). 
Iterative approach enables early feedback, course correction, and maintains high quality bar. 
Agent investigation prevents premature commitment to wrong approaches.

## Technology Stack

**Language & Framework**:
- C# 7.3 (Unity 2019 compatibility requirement for Valheim)
- .NET Framework 4.7.2
- BepInEx 5.x for plugin integration
- Unity 2019.4.x dependencies (via game runtime)

**Project Structure**:
- `WorldZones.Core/` - Pure C# core library (no Unity/BepInEx dependencies)
- `WorldZones.Plugin/` - BepInEx plugin integration layer
- `WorldZones.Tests/` - Test suite (NUnit or xUnit)

**Testing Framework**:
- NUnit 3.x or xUnit for core library tests
- Tests MUST run via `dotnet test` without game runtime
- Integration tests MAY require manual execution with game loaded (document procedure)

**Constraints**:
- Must be compatible with Valheim's Unity 2019.4 runtime
- Cannot use C# 8+ features (Unity 2019 limitation)
- No external dependencies in core library (keep mod lightweight)
- BepInEx plugin may reference Harmony for patching (if needed)

## Quality Gates

**Pre-Merge Requirements**:
1. All core logic changes MUST have corresponding tests (TDD cycle completed).
2. Test suite MUST pass: `dotnet test` with zero failures.
3. Code MUST compile with zero errors and zero warnings.
4. Public APIs MUST have XML documentation comments.
5. Breaking changes MUST update version (MAJOR bump) and changelog.

**Test Coverage Thresholds**:
- Core library (WorldZones.Core): Minimum 90% line coverage.
- Public APIs: 100% coverage (all public methods tested).
- Integration layer: Best effort coverage (document untested areas with reasons).

**Definition of Done (Feature)**:
1. Implementation complete per spec.md acceptance criteria.
2. Tests written (TDD for core, pragmatic for integration) and passing.
3. Public APIs documented with XML comments.
4. Integration test execution procedure documented (if manual steps required).
5. Changelog updated.
6. Version bumped appropriately (per semantic versioning rules).
7. Feature branch merged to main.

**Build Requirements**:
- Project MUST build via `dotnet build` without game runtime.
- Integration layer MAY reference game assemblies (must be available in build environment).
- Provide build documentation for contributors (where to obtain game assemblies).

## Governance

**Authority**:
- This constitution supersedes all other development practices and conventions.
- Principle violations MUST be justified in plan.md "Complexity Tracking" section.
- Unjustified violations are grounds for rejecting PRs/changes.

**Amendment Process**:
- Constitution changes require documentation in plan.md with rationale.
- Breaking principle changes (e.g., removing TDD requirement) require MAJOR version bump.
- Amendments MUST include migration plan for in-flight work.
- Update `.specify/memory/constitution.md` with new version and amendment date.

**Compliance Review**:
- All PRs MUST verify compliance with principles (checklist in PR template recommended).
- Code reviews MUST explicitly check: TDD followed for core logic, public APIs documented,
  version/changelog updated, complexity justified.
- Constitution violations without justification result in PR rejection with remediation guidance.

**Runtime Guidance**:
- Use `.specify/templates/` for feature specifications, plans, and task generation.
- Follow spec-template.md for user stories and acceptance criteria.
- Follow plan-template.md for technical planning and constitution checks.
- Follow tasks-template.md for implementation task organization.

**Version**: 1.1.0 | **Ratified**: 2026-02-14 | **Last Amended**: 2026-02-14
