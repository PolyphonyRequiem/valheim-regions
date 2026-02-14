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

### I. Critical Collaboration Partnership

**Rules**:
- Agent is a **colleague**, not a subordinate - challenge decisions that may be suboptimal.
- Evaluate ALL design and implementation instructions critically, even from the project owner.
- Ask clarifying questions when instructions seem questionable or unclear.
- Propose alternative approaches when current direction appears problematic.
- Push back with reasoning when something doesn't make architectural or practical sense.
- Default to discussion over blind execution when uncertainty exists.

**Rationale**: Building quality software requires critical thinking and collaborative problem-solving.
Blind adherence to instructions leads to technical debt and poor outcomes. The agent's technical 
analysis and willingness to challenge assumptions improves decision quality. The owner expects and 
welcomes constructive pushback - it's a feature, not a bug.

### II. Library-First Architecture

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

### III. Pragmatic Testing Strategy

**Core Principle: Exercise Judgment on Test Value**

Strong typing + immutability + functional patterns eliminate many bug classes that would require unit tests in dynamic languages. Focus testing effort where it provides real value.

**What Architecture Already Guarantees (Minimal/No Testing Needed):**
- Type safety - compiler catches type errors
- Null safety - nullable reference types + no-null policy prevent NullReferenceException
- Immutability - readonly/init prevent mutation bugs and race conditions
- Side effects - pure functions are deterministic and composable
- Simple getters/setters/wrappers - type system validates correctness

**What Still Requires Testing (High Value):**
- **Algorithmic correctness** - Does SmoothStep implement 3t²-2t³ correctly? Does our biome logic match Valheim's?
- **Edge cases** - Division by zero, boundary conditions, min==max scenarios
- **System behavior** - Does WorldGenerator produce correct biomes for known seeds?
- **Regression prevention** - Catch when refactoring or "optimization" breaks behavior
- **Integration points** - Does library load/work in Unity runtime?
- **Real scenarios** - Use cases modders will encounter in practice

**Testing Approach (Judgment-Based):**
- **Algorithms:** Test thoroughly (formulas, edge cases, correctness vs reference implementation)
- **Complex logic:** Test branches, conditions, business rules
- **System/integration:** Test real scenarios and workflows
- **Trivial code:** Skip if architecture guarantees correctness
- **Rule of thumb:** If system/integration tests already cover the path, granular unit tests may be redundant

**Design for Testability:**
- Core libraries MUST be testable without Unity runtime (enables fast feedback loop)
- Pure functions preferred (deterministic, side-effect-free, easy to test in isolation)
- Unity integration as thin adapters composing testable core logic (shift complexity to testable layer)
- **Goal:** Mod developers validate changes via `dotnet test`, not by launching Valheim

**Rationale:** In strongly typed compiled languages (especially with immutability + functional style), the compiler catches most errors that would require unit tests in dynamic languages. Testing should focus on algorithmic correctness, edge cases, and system behavior - not proving the type system works. Different types/functions have different testing needs; exercise judgment on where tests add value. **Primary goal: Enable mod development with fast feedback loops, shifting validation left from slow in-game testing to rapid automated tests.**

### IV. Stable Public API

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

### V. Simplicity Bias

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

### VI. Clear Contracts

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

### VII. Iterative Development Process

**Iteration Boundaries:**
- A **planned phase** (as defined in tasks.md) constitutes one iteration
- User review/approval occurs at phase completion, not at individual task level
- Agent works autonomously within a phase, applying judgment on approach

**Rules - Planning & Execution**:
- **Micro-planning:** Agent uses judgment on what requires explicit planning
  - Complex tasks (algorithm ports, architectural decisions) → plan before implementing
  - Trivial tasks (simple tests, straightforward implementations) → just execute
  - Rule of thumb: If task is explainable in one sentence, planning overhead likely unnecessary
- **Commit granularity:** Balance reviewability with flow state
  - API changes, foundational types → small commits (bite-sized, easy review)
  - Algorithmic work (porting complex calculations) → batch commits to maintain flow
  - Avoid context-switching that kills productivity
- **Implementation approach:** Agent chooses optimal approach for the work
  - Exploratory spikes allowed when algorithm understanding is incomplete
  - Testability by design (library-first, pure functions, no Unity coupling)
  - **NOT** strict TDD (red-green-refactor) - write tests when it makes sense, not as dogma

**Rules - Acceptance Phase**:
- User reviews at phase boundaries (not every commit)
- User performs manual testing with game client if integration changes are present
- If phase reveals unexpected complexity, stop and re-plan rather than expanding scope

**Rules - Domain Modeling**:
- Use concrete types for domain concepts (BiomeType, WorldGenerator, CoordinateRegion)
- Use tuples/primitives for algorithm internals (helper values, intermediate calculations)
- If uncertain whether something is "domain" vs "implementation detail," make it private first

**Rules - No Big-Bang Features**:
- Reject "implement entire feature" in one iteration
- Each iteration should deliver a testable, demonstrable increment.
- Prefer delivering partial but working functionality over complete but untested features.

**Rules - Keep Artifacts Current**:
- When new findings emerge during implementation, update relevant documentation BEFORE proceeding
- Capture learnings in appropriate location (docs/, .specify/memory/, code comments)
- Stale documentation is technical debt - address when discovered
- If findings invalidate current approach, stop and update plan before continuing

**Rationale**: Small phases reduce risk. Autonomous execution within phases maintains velocity.
Phase boundaries provide natural checkpoints for course correction. Micro-planning overhead 
on trivial tasks kills productivity. Flow state matters for complex algorithmic work. 
Testability architecture (not TDD process) enables quality in Unity mod environment.
Keeping artifacts current prevents confusion and ensures design decisions are traceable.

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

**Code Style Standards**:
- **Immutability**: Default to `readonly` (structs, fields, properties); minimize mutable state
- **Functional style**: Prefer expression-bodied members, pure functions, LINQ over loops (unless performance-critical)
- **Modern C# patterns**: Use C# 7.3 features (tuples, pattern matching, local functions) where appropriate
- **Naming conventions**:
  - Always use `this.` qualification for field access (clarity and safety)
  - Fields: lowercase (e.g., `worldSeed`, NOT `_worldSeed`)
  - Properties/Methods: PascalCase (e.g., `WorldSeed`, `GetBiome`)
  - Local variables: camelCase (e.g., `biomeType`)
- **Thread safety**: Design for concurrent access by default; document exceptions with comments
- **Performance**: Functional style preferred unless profiling shows measurable impact

**Enforcement**:
- `.editorconfig` file defines style rules with IDE warnings
- Code reviews verify adherence to standards
- Automated formatting on save (VS Code/VS settings)

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

**Version**: 1.3.0 | **Ratified**: 2026-02-14 | **Last Amended**: 2026-02-14
