# Quickstart: Inland Water Attribution

**Audience**: Developer implementing and validating Feature 004  
**Goal**: Validate inland-water region ownership with deterministic outputs and combined visual + in-game checks.

---

## 1) Prerequisites

- Repository checked out on branch `004-inland-water-attribution`.
- .NET SDK available for build/test.
- Existing sample PNG outputs available for baseline visual comparison.
- Modded Valheim setup available for in-game verification.

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

---

## 5) In-Game Validation

Run Valheim with the updated plugin build and inspect known lake-heavy areas.

Checklist:
- Map/overlay behavior reflects lakes as part of the surrounding region territory.
- Region transitions near lake edges are deterministic and stable.
- No visible regressions in general region lookup responsiveness.

Pass criteria:
- Lakes are properly incorporated as region-owned territory.
- No false attribution of clearly ocean-connected water.

---

## 6) Determinism Spot Check

Repeat generation for the same seed/config and compare outputs.

Expected:
- Same ownership outcomes and inland-water attribution statistics across runs.

---

## 7) Scope Guardrails

- Do not include boundary geometry reform (polyline/spline/LOD boundary rendering) in this feature.
- Do not change land-seeded proto-generation semantics.
- Keep attribution logic in `WorldZones.Regions`; integration layers should remain thin consumers.