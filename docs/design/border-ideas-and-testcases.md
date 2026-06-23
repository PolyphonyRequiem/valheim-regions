# Region borders — ideas backlog & test-case catalog

> Generated 2026-06-23 during the borders investigation. Grounded in measured results (see
> `region-borders.md`), not blue-sky. Each idea tagged with status + how to test it. This is a
> *backlog*, explicitly provisional — Daniel gates what graduates to design-locked.

## A. Cost-field ideas (the v3 family)

| # | Idea | Status | How to test |
|---|---|---|---|
| A1 | **Biome-edge barrier cost** (`12/8/1` edge/shore/interior) | ✅ WINS (+18pt) | `cost_v3.py` — done |
| A2 | **Per-feature weight tuning** — different barrier heights per biome pair (e.g. Mountain↔anything = very high wall; Meadows↔BlackForest = low) | 🟡 untested | extend cost_v3: weight by `(biomeA,biomeB)` pair; measure on-feature% + subjective render |
| A3 | **Slope as additive tie-break** — `cost += k·slope` ON TOP of biome-edge barrier, only to break ties in dramatic terrain (mountains) | 🟡 untested | add to v3; check Case C improves without hurting flat A |
| A4 | **Shore as its own tier** — shoreline is a *softer* wall than biome-edge (8 vs 12); is that right, or should coast be the HARDEST wall (it's the most visually definitive)? | 🔴 open | sweep shore weight 4→20, eyeball which coast-hugging reads best |
| A5 | **Distance-ring awareness** — Valheim biomes live in radial bands; a border crossing a *ring* boundary (Meadows→BlackForest at r≈600) is more "real" than within-ring noise wobble | 🔴 idea | add radial-distance gradient as a weak barrier term |
| A6 | **Anisotropic cost** — cheaper to travel ALONG a detected edge than across it (true watershed) | 🔴 idea, risky | needs edge-direction estimate; may reintroduce tendrils — test carefully |

## B. Seed-placement ideas (separate lever from growth — don't conflate)

Even a perfect cost field can't fix two seeds dropped in one valley. Seed placement is its own axis.

| # | Idea | Status | How to test |
|---|---|---|---|
| B1 | **Feature-aware seeding** — bias seed placement AWAY from feature lines (seeds belong in biome *interiors*, not on coasts/ridges) | 🔴 idea | measure: do current random seeds ever land on edge cells? if yes, that's a bug source |
| B2 | **One-seed-per-biome-blob minimum** — guarantee each contiguous biome patch ≥N zones gets its own seed (so a biome is never split mid-blob by a border from outside) | 🟡 promising | count biome-blobs vs seeds on real patches; measure "borders cutting through a single biome" |
| B3 | **Authored seeds (L3 vision)** — designer places seeds as intent, terrain grown to fit | 🔵 post-launch | the `seedRng` seam already supports injected seeds |

## C. Contested / sub-zone ideas (the boundary-cell richness)

| # | Idea | Status | How to test |
|---|---|---|---|
| C1 | **Boundary cell carries the touching RegionKeys** (2–4) | 🟡 designed, not built | engine: emit per-edge-cell the set of adjacent region IDs→keys |
| C2 | **Sub-zone edge geometry** — within a boundary cell, store the line (from the feature that routed it) | 🔴 idea | only matters if ESP walk shows the 64m staircase reads as wrong |
| C3 | **Unowned territory as first-class** (ocean, wild frontier) — firm contour edge, no owner | 🟡 designed | DepthClass.Deep already = unowned candidate; tag + render |

## D. Test-case catalog (real seed HHcLC5acQt junctions, from ScanWorld)

Curated windows that stress different mechanisms. Re-export with `ExportPatch`.

| tag | world (x,z) | what it stresses | key biomes | relief |
|---|---|---|---|---|
| **A** | -4112,624 | flat featureless seam (control) | Ocean/Plains/Swamp/BF/Meadows | 54m |
| **B** | -2960,4592 | relief-rich + mistlands interior | Plains/Ocean/Mistlands/Swamp/Mtn | 165m |
| **C** | 1776,-2320 | dramatic mountains (watershed showcase) | Ocean/BF/Plains/Mtn/Meadows | 194m |
| D* | 496,1904 | **needed:** pure coastline + mountain, near spawn | Ocean/BF/Meadows/Swamp/Mtn | 160m |
| E* | -6032,240 | **needed:** highest-relief mixed (198m) edge case | Mtn/BF/Mistlands/Ocean/Plains | 198m |
| F* | -912,3952 | **needed:** swamp-heavy, low relief (swamp-rim test) | Meadows/Plains/Swamp/BF/Ocean | 35m |

(* = scanned, not yet exported/analyzed — next-session candidates.)

### Test scenarios to run on each patch
1. **on-feature %** — BFS vs v1 vs v3 (the headline metric). `cost_v3.py`.
2. **biome-split count** — how often a border cuts through the middle of a single biome blob (lower = better). NOT YET BUILT — good next metric.
3. **region area balance** — does the rule produce wildly uneven regions? (v3 spread was OK, ~5300.)
4. **stability under seed jitter** — move seeds ±2 cells; does the border move a lot (fragile) or little (robust)? NOT YET BUILT.
5. **eyeball** — render + look. The ultimate gate, but offline-render is a proxy for the real ESP walk.

## E. Open questions (genuinely unresolved)

- **Q1:** Is 64m classification fine enough, or do we need the sub-zone edge geometry (C2)? — *ESP-gated.*
- **Q2:** Should Mountain be its own region, a landmark, or a multi-region junction? — design fork, Daniel's.
- **Q3:** Mistlands vertical ownership (ravine floor vs spire top = different places?) — flag, post-launch.
- **Q4:** Do biome-edge barriers ever produce a WORSE *gameplay* feel than honest midlines, even if more "correct"? — only the in-world walk answers this.
- **Q5:** How many seeds per patch is right? (tonight used k=4 arbitrarily.) — tied to B2.

## F. The metric gap (build next)

Tonight proved the methodology: **measure before believing.** Metrics we have: on-feature%, coherence,
slope-lift, area-balance, **biome-split (built 2026-06-23)**. Metrics we still NEED:
- **seed-jitter stability** — move seeds ±2 cells; does the border move a lot (fragile) or little
  (robust)? ~30 lines on the harness. NOT YET BUILT.

### ✅ biome-split metric result (2026-06-23) — orthogonality finding
Measured "how many biome blobs are owned by >1 region" (a region cutting a biome in half):

| patch | BFS split-excess | V3 split-excess |
|---|---|---|
| A | 2 | 2 |
| B | 8 | 9 |
| C | 8 | 9 |

**v3 is TIED with BFS (±1) on biome-split — NOT better.** Important: this means cost-field and
seed-count are **orthogonal knobs.** v3 makes borders *sit on* edges (+18pt on-feature) but does NOT
reduce how often a region *spans* two biomes — because with only k=4 seeds across a 6-biome patch, a
region MUST span biomes; there aren't enough regions. **Split-reduction is a SEED-PLACEMENT problem
(idea B2: one-seed-per-biome-blob), not a cost-field problem.** Don't expect the cost field to fix it.
This is exactly why B (seeding) is filed as a separate lever from A (growth) — confirmed empirically.
