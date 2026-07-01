# Spike 004 — shared-seam primitive + junction risk (fork B feasibility)

> **Status: VALIDATED** (2026-07-01) on 3 real seeds (ForTheWort, Astley, SolidHmm).
> Throwaway probe: `WorldZones.Cli sharedseamspike --seed <s>` (`src/WorldZones.Cli/SharedSeamSpike.cs`).
> This is the de-risk gate the design doc (`shared-border-primitive.md`) demanded before building B:
> *"the junction solve is the thing to spike before committing the solver."* It is now spiked.

## The question

Fork B = collapse the interior region border to **one shared arc per region-pair**, consumed by both
regions' fills AND the ink, so fill==ink **by construction** (no more three-times-derived weave, no ~16 m
separation). The design doc named the **junction** — where 3+ of these arcs meet at a point — as "the one
genuinely-unsolved risk." Three staged feasibility questions, ordered by risk, on the **real** world:

| # | Spike | Given / When / Then | Verdict |
|---|-------|---------------------|---------|
| 1 | decompose | Given the coarse seam graph, when split at junction nodes into per-pair arcs, then it's well-formed (arcs end at junctions; regions pair up) | ✓ PASS |
| 2 | junction integrity | Given arcs refined once with junction endpoints **pinned**, when checked at every real junction, then all incident arcs share the point (gap≈0) and don't cross | ✓ **PASS — the headline** |
| 3 | reassembly watertight | Given each region's refined arcs, when chained into its ring, then it closes, no self-intersection, across the whole world | ✓ PASS (with the known σ-ladder) |

## What the spike consumes (no new worldgen)

The substrate **already exists**: `RegionBoundaryExtractor` emits `graph.Segments` — every 64 m seam edge,
carrying **both** region keys (`KeyA`/`KeyB`, coast = `KeyB null`), deduplicated, on the `64·n+32` lattice.
A junction is just a lattice node where that seam graph branches (degree ≠ 2) or the region-pair changes.
So B is a **decomposition of data that's already computed**, not a re-seeding.

## Results (measured, 3 seeds)

| metric | ForTheWort | Astley | SolidHmm |
|---|--:|--:|--:|
| regions | 149 | 156 | 152 |
| coarse seam segments | 12 243 | 12 482 | 12 601 |
| **junction nodes** (deg≠2 or pair-change) | 148 | 167 | 146 |
|   · degree-3 | 124 | 138 | 124 |
|   · degree-4+ | 24 | 29 | 22 |
| shared arcs decomposed | 305 | 332 | 304 |
| **SPIKE 2: max endpoint gap at junctions** | **0.000000 m** | **0.000000 m** | **0.000000 m** |
| **SPIKE 2: stub crossings near junctions** | **0** | **0** | **0** |
| **SPIKE 3: regions reassembled watertight** | **149/149** | **156/156** | **152/152** |
| SPIKE 3: non-ambiguous infeasibility failures | **0** | **0** | **0** |

### Junction census correction (supersedes the 2026-06-30 note)
The earlier "1–4 triple points per world" came from a crude 2×2 grid-block census that only caught
land-land-land meetings. The **seam-graph** census is the honest one: **146–167 junction *nodes* per
world** (includes coast-meets-seam corners the block method missed). Still fully tractable — every one is
enumerable up front, and integrity holds at **all** of them. The `deg4+` count (22–29) means true 4-way
meetings DO exist and are common enough that the solver must handle them (not just deg-3).

## The headline: junctions meet at a point, for free

**SPIKE 2 is the risk the doc feared, and it is solved trivially by construction.** Refine each shared arc
ONCE with its two junction endpoints **pinned** (endpoints don't move), and across all 145–163 real
deg≥3 junctions per world: **gap = 0.000000 m and zero stub crossings.** Because every arc that touches a
junction is pinned to that exact lattice point, they cannot drift apart and cannot leave a gap. The "hard"
part of B was never hard — it was a consequence of picking the right primitive (pin at junctions).

Rendered proof (one real 3-way junction, ForTheWort — a coast∩coast∩seam corner): three refined arcs
converge on the one point, leave in three clean directions, no tangle. `/tmp/wz_bspike/junction_3way.png`
→ `_review/junctionB_3way.png`.

## The one real subtlety: multi-wedge junctions need a solver (not a gap-fill)

12 regions on ForTheWort have a junction where **the same region owns two wedges** (its border pinches to a
point and continues — e.g. an hourglass). There, "which arc-end pairs with which" is genuinely ambiguous to
naive greedy chaining. This is **the solver's defined job** — pick the pairing by turning-angle / interior
consistency — NOT evidence the primitive fails. With arcs made watertight (below), even plain greedy
chaining reassembles all 12 correctly on all 3 seeds; the solver just makes it *provably* right rather than
lucky. Its scope is now precise: order the arc-ends by angle at each of the ≤29 multi-wedge junctions/world.

## The other subtlety: arc self-intersection is the SAME already-shipped problem

First run leaked **5** non-ambiguous self-intersections. Diagnosis (instrumented): **all 5 were a single σ=30
refined arc crossing ITSELF** — zero were arc-vs-arc collisions, zero were my stitching. This is the exact
class `RegionRingRefiner` already handles today via its **watertight ladder** (σ self-intersects → roll back
to despiked → roll back to raw). Applying that same ladder to the arc refiner → **5 → 0** on all seeds. So
B inherits the existing, proven guard; it introduces no new self-intersection risk.

## Probe limitation (NOT a B risk — do not over-read)

"Winding preserved" reads 41–49/149 in the probe. That is **not** a topology failure — every ring closes and
none self-cross. It's my probe's naive arc-direction bookkeeping during greedy reassembly (it doesn't track
CCW-outer/CW-hole orientation). The **real** build derives winding from the extractor's directed-edge
convention (interior-on-left), which already gets it right for every ring shipped today. The probe just
doesn't replicate that pass. Flagged so the next session doesn't chase a phantom.

## Recommendation for the real build (B, staged)

The primitive is validated; here's the shape the spike proved out:

1. **`SharedSeam` decomposition (Tier-1).** Split `graph.Segments` at junction nodes into per-pair arcs.
   Data model: `SharedSeam { KeyA, KeyB, Node n0, n1, coarse polyline }`. ~300 arcs/world. (Spike proved
   well-formed.)
2. **Refine each seam ONCE** — snap-to-flip + σ=30, junction endpoints pinned, **through the existing
   watertight ladder** (self-int → despiked → raw). One refinement, cached. (Spike proved gap=0, self-int
   handled.)
3. **Junction solver (the only genuinely-new code).** At each multi-wedge junction (≤29/world, all
   enumerable), order incident arc-ends by angle so each region's two wedges pair unambiguously. Deterministic
   (angle sort). This is small and bounded — NOT the open-ended risk the doc feared.
4. **Consumers read the shared seam:** region fill ring = chain its seams (replaces the independent
   `RegionRingRefiner` per-region refinement); ink = the same seam. fill==ink by construction — the ~16 m
   weave is gone.
5. **Membership unchanged.** `RegionAt`→`ResolveCurrent`→coarse 64 m grid (verified in source). B, like A, is
   a render-layer change; it does not touch spawns/territory/gazetteer.

**Fork A relationship:** A (σ the fill ring, built this session, staged) and B are not redundant — A smooths
the three independent curves; B collapses them to one. If B lands, A's closed-Gaussian `SmoothGaussianClosed`
is reused *inside* the shared-seam refine (step 2), so A is not wasted — it's a component of B. Ship A now for
the immediate "regions stop stepping" win; B is the deeper "fill and ink agree" fix on top.

## Provenance
`WorldZones.Cli sharedseamspike --seed {ForTheWort,Astley,SolidHmm}`. Throwaway diagnostic (2026-07-01),
consumes existing extractor output, not part of the shipped surface. Junction render: `_review/junctionB_3way.png`.
