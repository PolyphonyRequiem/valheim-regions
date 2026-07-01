# HANDOFF — region border work (fork A shipped-staged, fork B validated) — 2026-07-01

> Written for the next session (post machine-migration). Everything here is **pushed to
> `origin/feat/region-render-seam-tier1`** — `git clone` + `git checkout feat/region-render-seam-tier1`
> and you have it all. Nothing load-bearing lives only in a working tree.

## The one-line state
Interior region borders read as **blocky (stair-step)** and **fill/ink disagree (~16 m weave)**. Two
independent fixes are in flight: **A = smooth the fill ring** (built, tested, *staged off*), **B = one
shared border curve for fill + ink** (spiked + VALIDATED, *not built*). Neither is live yet — both are
gated on Daniel's eyeball, same pattern as every other border lever.

## What's committed + pushed (safe across the migration)

| commit | what |
|---|---|
| `bc98754` | **Fork A** — `PolylineSmoother.SmoothGaussianClosed` + `ResampleUniformClosed`; `RingRefineOptions.SmoothingSigmaMeters` (default 0 = byte-identical Chaikin) wired into `RegionRingRefiner` inside the watertight ladder. +9 tests → **73 total, 0 warnings**. |
| `b185fb8` | Throwaway CLI probes (`SharedSeamSpike`, `RingSigmaViz`, etc.) + design docs (`spike-004-shared-seam-primitive.md`, `region-boundary-negotiation.md`). |

Prior shipped work (already on origin before this session): the authoritative refined ring, fine fill
clipped to the 30 m waterline, swamp floor 27.5, min-region floor. HEAD before this session = `bdb7a92`.

## Fork A — DONE, staged OFF (the immediate "regions stop stepping" win)
- **Code:** `src/WorldZones.Runtime/Geometry/PolylineSmoother.cs` (`SmoothGaussianClosed`),
  `RegionRingRefiner.cs` (`SmoothingSigmaMeters` option + ladder selection).
- **Why closed-loop:** a fill ring is a CLOSED loop; the open ink smoother pins endpoints and would kink
  at the closure seam. The closed variant wraps the Gaussian window mod n, pins nothing.
- **Measured (ForTheWort):** self-int rollbacks **28→1** (Gaussian is *cleaner* than per-ring Chaikin);
  jaggedness ~80–97 m → ~0.02 m on 147/149 big rings.
- **Honest cost:** fill-ring area moves median 0.13%, but 6 small rings ≥5% (max 12%) — σ rounds off
  single-zone teeth. This is **cosmetic only**: membership = coarse 64 m grid (`RegionAt` →
  `ResolveCurrent`, VERIFIED in source), NOT the refined ring. Ring feeds only the map paint.
- **THE FLIP (one line, when Daniel says go):** in `RegionOverlayPlugin.cs` ~line 523, the live
  `RefinedRegionBoundary.Build(mainGraph, ringKeyToLabel, ringRidAt, ringCoastField, ringSeamField)` call
  passes default options → σ=0. To ship A: pass `new RingRefineOptions { SmoothingSigmaMeters = 30.0 }`.
  Then net472 build + deploy to Prime walk-client (recipe: skill `valheim-worldzones-development`
  → `references/build-deploy-and-fix-recovery.md`).
- **Render for the eyeball gate (regenerate anytime):**
  `dotnet <cli> ringsigmaviz --seed ForTheWort --output /tmp/wz_sigma` then the focus render on the
  worst-stepped region (label 112). Was shown; Daniel liked it. **Gate still open — he hasn't confirmed
  the flip.**

## Fork B — VALIDATED spike, NOT built (the deeper "fill == ink by construction" fix)
Full verdict: **`docs/design/spike-004-shared-seam-primitive.md`**. Re-run: `dotnet <cli> sharedseamspike
--seed {ForTheWort,Astley,SolidHmm}`.
- **The feared risk (junctions) is solved by construction:** refine each shared arc once with junction
  endpoints PINNED → **gap 0.000000 m, zero crossings** across all 145–163 deg≥3 junctions/world.
- **Reassembly:** 149/156/152 regions watertight on the 3 seeds. The 5 first-run self-ints were all
  single-arc σ self-intersection = the SAME class `RegionRingRefiner`'s ladder already rolls back
  (applied it → 5→0). No new self-int risk.
- **Junction census (corrected):** ~146–167 junction NODES/world (not the "1–4" crude land-only count),
  deg4+ ≈ 22–29 — real 4-ways exist, the solver must handle them.
- **Substrate already exists:** `graph.Segments` carries every seam with both region keys on the 64 m
  lattice. B is a decomposition of computed data, no new worldgen.

### B build shape (from the spike, + the simplification I was about to try)
5-stage plan is in spike-004. **The refinement I want to attempt first** (was mid-thought when migration
came up): skip the explicit junction "solver" entirely — reassemble each region's ring by walking its
**coarse ring** (from `RegionBoundaryExtractor`, already closed + wound correctly + multi-wedge-resolved)
and *substituting* the refined shared seam for each coarse run. That collapses the junction solver into
"follow coarse-ring order," making B smaller and strictly safer. Fall back to the angle-solver only if a
test shows it doesn't hold. **A is NOT wasted by B:** `SmoothGaussianClosed` is reused inside B's seam
refine. Ship A now; B on top.

### B todo (was in-progress, not started)
1. `SharedSeam` + `SharedSeamSet` (Tier-1): decompose `graph.Segments` at junctions, refine each seam
   ONCE (snap + σ, pinned, through the watertight ladder). Tests: coverage, shared endpoints, determinism,
   watertight.
2. `SharedSeamBoundary`: reassemble rings via coarse-ring substitution (off-by-default flag). Tests:
   149/149 watertight, winding preserved, fill-vs-ink separation ~0.
3. Headless render: fill ring overlaid with ink, proving SAME curve (**blue/orange + labels — Daniel is
   red/green colorblind**).
4. Full suite green → then the plugin wiring flip (gated on eyeball).

## Two open decisions for Daniel (nothing is committed live)
1. **Flip A on now?** One line + a walk-client deploy. Immediate stepping fix.
2. **Build B?** Validated, ~2–3 bricks of Tier-1. The real root fix.
   (My recommendation: flip A now for the quick win, build B on top since A is a component of it.)

## Re-grounding on the new box (verify, don't trust this doc)
```bash
cd <repo>/valheim-regions && git checkout feat/region-render-seam-tier1 && git pull
dotnet build tests/WorldZones.Runtime.Tests/WorldZones.Runtime.Tests.csproj -c Release   # 0 warn
dotnet test  tests/WorldZones.Runtime.Tests/WorldZones.Runtime.Tests.csproj -c Release   # 73/73
dotnet build src/WorldZones.Cli/WorldZones.Cli.csproj -c Release                          # CLI compiles
```
Skill `valheim-worldzones-development` (+ `references/shared-border-primitive.md`) carries the durable
design state and is updated through this session.

## Pitfalls carried forward
- **Compile-verified ≠ playable.** No GPU Valheim client on the workbench. Headless proofs (tests, PNGs,
  measured %) prove math/data; whether it reads right on the ground is Daniel's walk — the one true gate.
- **Renders: blue/orange + text labels, never red/green** (Daniel is red/green colorblind).
- **Don't crop the LONGEST seam** for a "does it step" render (may already hug a feature → understates);
  crop the **worst-stepped** region (max per-vertex midpoint-deviation).
