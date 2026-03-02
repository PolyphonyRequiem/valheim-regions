# Quickstart: Inland Water Attribution

**Audience**: Developer implementing and validating Feature 004  
**Goal**: Validate inland-water region ownership with deterministic outputs and combined visual + in-game checks.

---

## 1) Prerequisites

- Repository checked out on branch `004-inland-water-attribution`.
- .NET SDK available for build/test.
- Existing sample PNG outputs available for baseline visual comparison.
- Modded Valheim setup available for in-game verification.

Validation sample set:
- Required known seed: `HHcLC5acQt`
- Optional additional seeds: include up to 2 reproducible seeds if available in your local workflow

Expected artifact minimum:
- 1 baseline PNG + 1 candidate PNG for the required seed.
- If optional seeds are available, include matching baseline/candidate PNG pairs for each.

---

## 2) Build and Run Tests

From repository root:

```powershell
dotnet test tests/WorldZones.Regions.Tests/WorldZones.Regions.Tests.csproj
dotnet build WorldZones.slnx
```

Expected:
- Region tests pass, including new deterministic inland-water attribution tests.
- Build succeeds across impacted projects.

---

## 3) Generate New PNG Samples

Generate candidate overlays with inland-water attribution enabled using existing export workflow (same seeds used for prior baseline artifacts).

Expected:
- New region overlays show enclosed lakes filled as part of owning region territory.
- Ocean-connected water remains excluded from region territory.

---

## 4) Visual Validation (Baseline vs Candidate PNG)

For each selected seed/sample set:

1. Open old baseline PNG and new candidate PNG side-by-side.
2. Confirm expected deltas are localized to inland water inclusion.
3. Confirm no unexpected land ownership drift.

Pass criteria:
- Inland lakes incorporated into region fills.
- Coast/ocean boundaries remain consistent with baseline expectations.
- No obvious topological regressions (missing regions, broken ownership seams).

Recording template (append one row per seed):

| Seed | Baseline PNG | Candidate PNG | Pass/Fail | Reviewer | Notes |
|------|--------------|---------------|-----------|----------|-------|
| HHcLC5acQt |  |  |  |  |  |
| OptionalSeed_1 |  |  |  |  |  |
| OptionalSeed_2 |  |  |  |  |  |

---

## 5) In-Game Validation

Run Valheim with the updated plugin build and inspect lake-inclusive areas that are available in the selected validation seed(s).

Checklist:
- Map/overlay behavior reflects lakes as part of the surrounding region territory.
- Region transitions near lake edges are deterministic and stable.
- No visible regressions in general region lookup responsiveness.

Pass criteria:
- Lakes are properly incorporated as region-owned territory.
- No false attribution of clearly ocean-connected water.

Recording template (append one row per session):

| Session ID | Seed | Area Checked | Lakes Incorporated | Ocean False-Positive Check | Pass/Fail | Reviewer | Notes |
|------------|------|--------------|--------------------|----------------------------|-----------|----------|-------|
|  | HHcLC5acQt |  |  |  |  |  |  |
|  | OptionalSeed_1 |  |  |  |  |  |  |
|  | OptionalSeed_2 |  |  |  |  |  |  |

Sign-off authority:
- Visual validation requires one designated reviewer sign-off.
- In-game validation requires one designated reviewer sign-off.
- Final sign-off is recorded in this document by completing all rows with reviewer identity and pass/fail status.

---

## 6) Determinism Spot Check

Repeat generation for the same seed/config and compare outputs.

Expected:
- Same ownership outcomes and inland-water attribution statistics across runs.

Protocol:
1. Run 5 repeated generations per validation seed with identical configuration.
2. Compare ownership grids byte-for-byte or via deterministic hash of full grid contents.
3. Record any mismatch as failure with attached run IDs.

Recording template:

| Seed | Run Count | Comparison Method | Mismatch Count | Pass/Fail | Reviewer | Notes |
|------|-----------|-------------------|----------------|-----------|----------|-------|
| HHcLC5acQt | 5 |  |  |  |  |  |
| OptionalSeed_1 | 5 |  |  |  |  |  |
| OptionalSeed_2 | 5 |  |  |  |  |  |

Performance verification:
- Attribution-enabled runtime must be ≤ 1.5x baseline runtime.
- Additional runtime must be ≤ 250 ms at default world radius.

Performance recording template:

| Seed | Baseline Runtime (ms) | Attribution Runtime (ms) | Ratio | Delta (ms) | Pass/Fail | Reviewer |
|------|------------------------|--------------------------|-------|------------|-----------|----------|
| HHcLC5acQt |  |  |  |  |  |  |
| OptionalSeed_1 |  |  |  |  |  |  |
| OptionalSeed_2 |  |  |  |  |  |  |

---

## 7) Scope Guardrails

- Do not include boundary geometry reform (polyline/spline/LOD boundary rendering) in this feature.
- Do not change land-seeded proto-generation semantics.
- Keep attribution logic in `WorldZones.Regions`; integration layers should remain thin consumers.