# Negotiated vector boundaries + terrain-scale smoothing (2026-06-30)

> **Status:** DESIGN CAPTURE of a long, productive thread (session "Shared Region Border Seam Fix",
> 2026-06-30). The *model* below is agreed and, where noted, **measured on a real seed**. Two pieces
> are being implemented now (the σ smoother promotion); the larger membership/solver/persistence work
> is **sequenced into staged cards, not started** — it wants this written spec first. Daniel gates all
> locks and every merge.
>
> Companions this rests on / extends:
> - `docs/design/region-borders.md` — the border model (mechanism→seam, cost field, routing vs seeding).
> - `docs/design/region-render-seam.md` §"DECISION 2026-06-29" — the refined ring is authoritative
>   bounds, persisted at world creation, point-in-polygon membership. **This doc is the mechanism that
>   decision needs**: *how* the seam gets onto features (negotiation) and *how* the curve is smoothed
>   (σ-in-metres), plus the one genuinely-unsolved risk (junctions).
> - `docs/design/region-identity.md` — `RegionKey`, the stable key any persisted boundary is keyed by.

## Why this thread happened

Daniel's two walk complaints on the Atlas build drove it: interior region seams read as **(1) blocky**
(the 64 m zone staircase) and **(2) "wiggly" / the colour jumps back and forth across the line**. The
session ran both to root cause, then generalised the fix into a coherent boundary model. The blockiness
is not a render bug — it is a *storage* fact (per-zone 64 m membership). Fixing it properly means the
boundary stops being a raster of 64 m cells and becomes a **vector curve on the real terrain feature**,
with membership answered by point-in-polygon. That reframing dissolves the "what resolution?" question
that had circled for weeks.

## The converged model (six points — the load-bearing summary)

1. **Root cause is storage, not render.** Today's blockiness is per-zone (64 m) membership — a coarse
   ownership grid, not a render artifact. *(grounded in the lookup code: `RegionIdGrid[gy,gx]`.)*
2. **Three requirements the boundary must satisfy** (Daniel's, verbatim intent): **consistent/aligned**
   (the fill, the coast fade, and the ink all agree — today they are three private refinements of one
   coarse truth and agree only by coincidence), **organic** (reads like a drawn map border, not a
   staircase), **geographically grounded** (sits on a real feature; never an invented curve).
3. **Sub-zone membership is forced, not optional.** Which side of a seam you are on drives spawns,
   spawn-eligibility, and PvP/territory rules; the 64 m lookup is too coarse to honour it near a
   contested seam. *(grounded in the membership lookup.)*
4. **Negotiated boundaries are worth building** — measured, not asserted. See §Negotiation below:
   on seed **Astley**, 67% of adjacent region pairs gain ≥10 pts of border-on-feature at ≤20% churn,
   at a **median 1.4% territory cost**. The idea pays for itself.
5. **The boundary is a vector curve, not a raster.** It traces the continuous feature contour;
   membership = point-in-(refined-)polygon; the "classification resolution" question dissolves because
   there is no cell grid to pick a resolution for. *(proven this session via the contour trace.)*
6. **Smoothing lives on the traced contour, at a terrain scale (σ), pinned at junctions.** Not a count
   of Chaikin passes (a grid-flavoured proxy) — a Gaussian low-pass along arc length, **σ in metres of
   real terrain**, so the knob means the same thing across seeds. Chosen default **σ = 30 m** (§Smoothing).

That is a real architecture and it is bigger than one card: it touches membership, the lookup, the
refiner, and persistence (key renumber). Hence: **spec first (this doc), then staged build.**

## Negotiation — the settlement pass (MEASURED feasible; solver NOT yet built)

**Model (Daniel's framing).** A protoregion boundary is an **opening bid**. A *settlement* pass then
relocates each `A|B` seam onto the best nearby geographic feature (river / biome-edge / shore — the
same "walls" the router already uses), **trading whole zones** (bounded by a size metric, ~≤20% of a
region) to do it, and running a **straight decreed line only where no feature is in reach**. This is the
dual of the cost-field router: the router grows regions until they *meet* at a wall; negotiation lets an
already-met seam *walk to* a nearby wall it missed, paying territory for the alignment.

**Feasibility probe (built + measured this session — `NegotiateProbe`, throwaway CLI).** Per adjacent
region pair on the real seed: freeze each region's core (>K zones from the seam, K=4 ≈ 256 m/side), take
the contestable **band** in between, and re-partition it with a deterministic **s-t min-cut (Dinic
max-flow)** whose edge capacities make it *cheap to cut between two feature cells* (1) and *expensive
across open ground* (12), with a small **stay-bias** (6) standing in for the size metric so a cell only
flips when a feature-aligned cut genuinely beats staying put. Cores anchor source/sink (∞) so they never
move. Then measure new-vs-current border-on-feature% and % churn.

**Verdict on seed Astley (39 adjacent land-land pairs, ≥3 seam edges):**

| metric | value |
|---|--:|
| mean border on-feature — current | **67.5%** |
| mean border on-feature — relocated | **89.8%** |
| mean gain | **+22.3 pts** |
| pairs "worthwhile" (gain ≥10 pts **and** churn ≤20%) | **26 / 39 = 67%** |
| pairs with big gain (≥10 pts, any churn) | 26 / 39 = 67% |
| pairs ~no gain (<5 pts — a straight decreed line already wins) | 13 / 39 = 33% |
| **median churn among the worthwhile pairs** | **1.4%** |

Read: two-thirds of seams have a real feature to walk to and get there for almost no territory (median
1.4% of a region's land moves); the other third have nothing nearby and should stay a straight line —
which the min-cut already does (the stay-bias holds them). The lever is **real and cheap**, exactly the
"cede only for a good boundary" rule Daniel wanted.

**Honest scope of the probe (do not over-read it):**
- It scores **pairs independently**, so **junction interactions are NOT modelled** (see §Junctions —
  the real risk). The production solver must be a **global, multi-pair, deterministic** solve.
- **Ridgelines are NOT in the feature set** — no clean extractor exists (measured too noisy at 8 m in
  the border work). Snappable features = river / biome-edge / shore, the proven-crisp ones.
- The probe is a **feasibility number, not the algorithm.** It proves worth-building; it is not the
  settlement pass.

## Smoothing — the σ-in-metres dial (DECIDED: σ = 30 m default, tunable)

**Problem.** The contour trace proved the boundary is a continuous curve on the real biome edge, but the
raw edge is Perlin-noisy ("hairy"). The fix is a low-pass **at the scale of real terrain, not the scale
of the grid.**

**The knob.** A **Gaussian low-pass along the curve's arc length, σ in metres**: each point is averaged
with its neighbours weighted by a Gaussian of width σ, after uniform arc-length resampling so σ is a true
metric distance. Physical meaning: wiggles smaller than ~σ m are smoothed away (pixel/Perlin noise);
features bigger than ~σ m survive (real headlands). **Junction endpoints are pinned** so adjacent borders
still meet. σ = 0 → identity. This is strictly better than "N Chaikin passes": Chaikin's effect depends on
vertex spacing (a grid artifact); σ is physical and seed-portable.

**The bracket Daniel eyeballed** (same Askaadal↔Blackhold seam, rendered over the biome backdrop so
over-smoothing shows as the line departing the colour transition — the banned "invented curve"):
- **σ = 10 m** — cleaner than the raw hairy trace but still keeps small jitters (grey peninsulas poking
  into purple survive as wiggles). Slightly too faithful to noise.
- **σ = 20 m** — smooth, organic, reads like a hand-drawn border, and still sits *on* the biome
  transition. Real headlands survive; pixel noise gone.
- **σ = 40 m** — over-rounded: the line starts **cutting across** the colour boundary, bulging into the
  wrong biome. This is the banned invented curve — smooth but no longer telling the truth.

So the dial brackets cleanly: **noise below ~10, truth lost above ~40.** Daniel's chosen default is
**σ = 30 m** — a touch more organic than the ~20 sweet spot, comfortably short of the ~40 that starts
lying. It is an **authorable knob**, walk-tunable per the same in-world-gate as every other border
parameter; 30 is the shipped starting value, not a hard lock.

> **Naming note to avoid confusion:** σ = 30 m (a smoothing *width*) is unrelated to the 30 m *waterline*
> (`HeightScalarField.SeaLevel`). Coincidental number, different axis.

**Ordering (matches the 2026-06-29 ring decision):** refine-to-contour defines the curve; **despike**
kills per-segment snap spurs; **then** the σ smoother is the LAST stage. The authoritative bound is the
*smoothed* curve ("smoothed-real," not "exactly-on-terrain") — the right trade when the fill/fade/ink
agreeing matters more than hugging every pixel.

## The one genuinely-unsolved risk — JUNCTIONS

Everything above composes cleanly **except** where 3–4 of these curves meet at a point. A junction must
be solved so the meeting curves share **one point with no gap and no overlap**, **deterministically**
(same seed ⇒ same junction), and stay consistent when each incident seam is independently negotiated +
smoothed. This is the piece with real risk:
- Independent per-pair negotiation can pull two seams that share a junction in ways that don't agree at
  the point.
- Per-vertex smoothing must keep the junction pinned for **all** incident arcs simultaneously.
- A `RegionRing` is a single closed loop so per-vertex refinement cannot open a gap *within one ring*;
  the hazard is **self-intersection** and **cross-ring disagreement at shared junctions**.

**This is the thing to spike before committing the solver.** Nothing else here is unknown; the junction
solve is.

## Ridgelines — still a gap (flagged, not solved)

The feature set is coast / river / biome-edge only. Ridgelines would make mountain borders read as
snowline/crest, but **no ridge extractor exists** — raw slope and Laplacian ridge/valley skeletons both
measured too noisy on real terrain (see `region-borders.md` Finding 3 + the coherence table). Until
someone builds a ridge extractor that passes the line-coherence gate (`feature_detect.py`), ridges are
out of the negotiation feature set. Do not re-attempt raw slope — it is a thrice-falsified dead end.

## Architecture guard (where each piece lives)

- **Tier-1 pure (`WorldZones.Runtime`, net472;net8.0, headless-tested):** the σ smoother, the ring
  refinement, the negotiation solver's geometry + graph math, point-in-polygon + Shoelace area. **No
  Unity.** Everything with an algorithm stays here, under the net8 test net.
- **Runtime (has the sampler/biomes):** builds the opaque fields (`HeightScalarField`,
  `BiomeCategoryField`) the Tier-1 math consumes — same pattern as the cost field and seeding field, so
  the topology layer stays biome-blind.
- **Consuming mod / render layer (Tier 2/3):** draws the authoritative curve; never computes it.
- **Every new lever is gated behind an option that defaults to the byte-identical legacy path** — the
  established regression-guard pattern (`UseFeatureAwareBorders`, `UseBiomeAwareSeeding`,
  `SwampLandFloorMeters` all do this). The σ smoother follows it: `SmoothingSigmaMeters = 0` ⇒ current
  Chaikin behaviour, zero regression.

## Staged card sequence (proposed — Daniel sequences/gates)

1. **σ smoother → Tier-1 + tests (IN PROGRESS this session).** Promote the proven arc-length Gaussian
   from the throwaway CLI into `PolylineSmoother.SmoothGaussian`, headless-tested; wire an off-by-default
   `SmoothingSigmaMeters` option into the refiner. Render the real refiner output at σ=30 for Daniel's
   confirm. **Isolated, proven, zero-regression — the safe first brick.**
2. **Flip the smoothing default to σ=30** in the dataset + overlay geometry, after Daniel eyeballs the
   wired render. One-line change once confirmed.
3. **Junction spike (the risk).** A throwaway probe that solves one real 3–4-way junction
   deterministically and renders it; pass/fail = no gap/overlap, stable under re-run. **Do this before
   the solver.**
4. **Negotiation solver (global, deterministic).** Promote the min-cut feasibility into a real
   multi-pair settlement pass with the size-metric bound; measured against the probe's numbers.
5. **Vector membership + persistence.** Move `RegionAt` / area / gazetteer onto point-in-(refined-)polygon
   (needs a spatial index for per-frame cost) + Shoelace area; persist refined rings at world creation.
   **This renumbers `RegionKey`** (seed-derived) — must land before any public playtest has discovery
   history. Region *identity* / determination and the <200-region guard are UNCHANGED; this is a
   boundary-representation upgrade, not a re-seeding.

## Honesty bar (carried into every card)

**Compile-verified ≠ playable.** This box has no GPU Valheim client. Headless proofs (tests, PNG renders,
measured %s) prove the math and the dataset; whether σ=30 *reads right on the ground*, whether negotiated
seams *feel* like real borders, and whether junctions hold — those are Daniel's in-world walk, the one
true gate. Nobody upgrades "reasoned" or "rendered" to "verified-in-world."

## Provenance of the measured numbers

- Negotiation feasibility: `NegotiateProbe` on seed **Astley**, `/tmp/negotiate/Astley_negotiate.csv`
  (39 pairs). Deterministic (integer-cap Dinic, fixed node order). Regenerate:
  `WorldZones.Cli negotiateprobe --seed Astley --output /tmp/negotiate`.
- σ bracket render: `SmoothScale` on the same seed, Askaadal↔Blackhold seam, σ ∈ {10,20,40}.
- Both CLIs are **throwaway diagnostics** (2026-06-30) — the measurements are durable (captured here);
  the throwaway code is not part of the shipped surface.
