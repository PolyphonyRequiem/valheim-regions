# Region borders — the converged model (2026-06-22/23)

> **Status:** PROVISIONAL but settled in shape. Captured after a long design thread with Daniel.
> The *structure* below is agreed; the per-biome *tuning weights* are explicitly deferred to the ESP
> walk (you can't pick them from the top down — see "What's still gated"). Daniel gates all locks.
> Companion: `docs/design/region-identity.md` (RegionKey — the identity layer this rests on).

> ## ✅ IMPLEMENTATION STATUS (2026-06-24) — both border systems BUILT
>
> The levers below are now CODE, on branch `feat/region-render-seam-tier1`, all verified on real
> Niflheim (ForTheWort). Daniel's framing this session: regions need BOTH the routing fix AND the
> detail layer. They are **orthogonal** — one changes which zones a region owns, the other changes how
> the owned border is *drawn*. **🟢 WIRED INTO GENERATION (Daniel's explicit ask, 2026-06-24):**
> feature-aware borders are ON in BOTH the gazetteer CLI (`Gazetteer.Export`) and the overlay plugin
> (`RegionOverlayPlugin`) — so the dataset modders consume == the tessellation players walk. Measured in
> the emitted gazetteer: world border on-feature **32.6% → 55.8% (+23.2 pts)**.
>
> **ADDED THIS PASS:**
> - **Rivers as a cost term.** `WorldGenerator.GetRiverWeight` exposed (was private) → `IRiverSampler`
>   capability on the sampler seam → river cells become walls (cost 14, above biome-edge 12) in
>   `RegionCostFieldBuilder`. 10.4% of land zones sit on a river at 64 m (median width 38 m — NOT missed
>   at zone resolution). On-feature 53.1% → 55.8% with rivers on. In-game: the plugin reflects the game's
>   private `GetRiverWeight` (decomp-verified private) via `AccessTools`, degrades to biome/shore + logs a
>   warning if the signature changes (so a dataset/in-game mismatch is visible, not silent).
> - **Biome-seam contour-hug** (`RegionBoundaryRefiner.RefineBiomeSeams` + `ICategoryField` /
>   `BiomeCategoryField`). Region-vs-region borders hug the actual biome TRANSITION (categorical
>   bisection snap), same chain+despike+Chaikin pipeline as coastlines. Coast iso = 25 m (shelf midpoint).
> - **Jaggy fix** (`PolylineSmoother`): chaining (12k segments → continuous arcs) + Despike (kills
>   per-segment spurs) + Chaikin (hull-bounded corner-cutting — can't wander off the feature).
>
> **1. ROUTING — v3 cost field ported into the engine (`ProtoRegionGenerator.GenerateLand`).**
> The terrain-blind flat-cost BFS is now a cost-weighted multi-source **Dijkstra** (watershed). The
> cost field (biome-edge 12 / shore 8 / interior 1, the measured v3 winner) is computed in
> `WorldZones.Runtime` (`RegionCostFieldBuilder` — it reads biomes; Regions stays biome-blind) and
> handed down as an opaque `RegionCostField`. **A null field = byte-identical legacy BFS** → gated
> behind `RegionBuildOptions.UseFeatureAwareBorders` (default OFF), so zero regression (Regions 73/73,
> WorldGen 49/49 unchanged). Measured ON vs OFF on Niflheim: **world border on-feature 30.5% → 52.3%**
> (per-region wins up to 31%→77%). ⚠️ HONEST LIMIT: routing does NOT fix multi-biome-blob composition
> (avg dominant-biome 72.7%→73.0%, blended-region count 44→45 — basically flat). A region with one
> seed in the middle of four biomes stays a four-biome region no matter how its border routes. That is
> a SEEDING problem (see deferral below), not a routing one. Caveat held: the explorer measured ~73%
> at 8 m; at the engine's 64 m we get 52% — coarser, as predicted, still +21 pts.
>
> **2. CONTOUR-HUG — sub-64 m detail layer (`RegionBoundaryRefiner`, Tier-1 pure).** ADDITIVE: the
> coarse 64 m `RegionBoundaryGraph` (the deterministic substrate gameplay keys off) is UNCHANGED; this
> refines each coastline segment onto the real `GetHeight == 30` shoreline isoline (perpendicular
> march + bisection). Field supplied by Runtime (`HeightScalarField`) so Tier-1 stays game-free.
> Niflheim: 11,956 coast segments, 9,528 hugged (80%); the rest honestly left on the lattice where no
> shoreline is in reach (the "arbitrary firm line where no feature" degenerate case). Eye-validated:
> the 64 m staircase becomes the diagonal coast. ⚠️ KNOWN ARTIFACT: per-segment-independent snapping
> produces perpendicular **spurs** into the water at sharp concavities/inlets (adjacent points snap to
> different spots, the connector darts out). Two clean fixes (post-snap polyline smoothing, OR a single
> continuous isoline-contour march through the boundary band instead of per-segment) — both are
> refinements of a WORKING layer, and "how aggressive / does it read on the ground" is walk-gated. Only
> coastlines are refined so far; biome-seam contour-hug (region-vs-region) is the next field type.
>
> **🔭 THE SEEDING lever — PROTOTYPED + MEASURED (2026-06-25, branch `feat/region-render-seam-tier1`).**
> The multi-biome-blob oddity (Greater Nordadal = Mtn30/Mist30/Plns20/BFor18; Aesirvoll = 4-way blend)
> is caused by where SEEDS land, not how borders route between them — confirmed THREE times (raw-slope,
> symmetric v3, falsified home-biome-pin). This pass BUILT the seeding lever and measured it on real
> Niflheim. **Headline: it is the FIRST lever that moves composition at all** (the 3 routing attempts
> were dead flat at ~73% dominant-biome), **but the naive "more seeds" version buys purity mostly by
> MULTIPLYING region count — Daniel's judgment call on the fork.**
>
> Architecture (mirrors the cost field exactly, so Regions stays biome-blind): an opaque
> `SeedingField` (a per-zone biome-diversity `double[,]` + an `Aggressiveness` scalar + a `PlacementBias`
> scalar) is consumed by `ProtoRegionGenerator` — it never sees a biome, only numbers.
> `RegionSeedingFieldBuilder` (Runtime, has biomes) computes per-zone diversity = distinct LAND biomes
> in a 5×5 window, normalised by a 4-biome cap. Two levers, both gated behind
> `RegionBuildOptions.UseBiomeAwareSeeding` (default OFF → **byte-identical legacy seeding**, proven by
> the gazetteer regression guard + Regions 73/73 unchanged):
> 1. **Budget** — a diverse component's seed count scales `× (1 + Aggressiveness · meanDiversity)`, so a
>    biome-mixed landmass gets more seeds → splits into smaller, more-mono-biome regions.
> 2. **Placement bias** — among farthest-point candidates, discount junction cells (`score × (1 −
>    PlacementBias · weight)`) so the extra seeds prefer biome INTERIORS, giving each biome patch its
>    own seat. (NOTE: junction-SEEKING placement would make blobs worse — interiors is the right sign.)
>
> **Measured sweep on real Niflheim (ForTheWort), routing held ON throughout** (`seedlab` CLI command):
>
> | aggr | regions | meanDom% | blended<55% | on-feat% |
> |--:|--:|--:|--:|--:|
> | 0 (off) | 162 | 72.9% | 48 (29.6%) | 54.2% |
> | 0.5 | 166 | 73.3% | 45 (27.1%) | 56.5% |
> | 1.0 | 181 | 72.9% | 51 (28.2%) | 52.5% |
> | 2.0 | 238 | 74.3% | 55 (23.1%) | 57.5% |
> | 4.0 | 297 | 75.7% | 64 (21.5%) | 58.4% |
>
> Placement bias (budget aggr=2.0 held): bias 0→0.9 drops blended 55→50 at the SAME region count, +0.6pt
> meanDom — a small, free sharpening (no extra regions). **Honest read:** the lever WORKS (meanDom up,
> blended-fraction down, and on-feature% goes UP so routing isn't hurt) but the gain is modest and the
> dominant cost is region-count inflation (162→297 at aggr 4). The blended-region *fraction* falls from
> 30% to 21%; the absolute count barely moves because the map just gets busier. **This is a now-or-never
> pre-ship window (RegionKey renumbers on any seed change), but the right aggressiveness is partly an
> in-world-walk judgment** — does a 237-region Niflheim read as "more places" or "noise"? That's the
> client-gated decision. Eye-validated colorblind-safe (biome by LIGHTNESS, blended blobs by HATCH not
> hue): the hatched (blob) area thins under the lever but the map densifies. Lever code is gated + kept;
> the aggressiveness/bias defaults are a starting dial, NOT locked. See `seedlab` + `RegionSeedingFieldBuilder`.

> **🔭 DEFERRED (superseded above) — the SEEDING lever.** [Original deferral note kept for history; the lever is now built + measured, see the block immediately above.] The multi-biome-blob oddity
> (Greater Nordadal = Mtn30/Mist30/Plns20/BFor18; Aesirvoll = 4-way blend) is caused by where SEEDS
> land, not how borders route between them. Fix = biome-aware seed placement / region splitting (drop
> more seeds in biome-diverse components, or split a region that spans N biomes). The border-model already
> flags routing and seeding as orthogonal knobs ("splitting is a SEEDING problem"). Tracked as a
> follow-up; routing + contour-hug were this session's scope. Do NOT conflate it with the routing port —
> they fix different oddities.
>
> **🔴 FALSIFIED SPIKE (2026-06-24) — asymmetric "home-biome pin" does NOT fix blobs.** Daniel's #1
> idea: make crossing OUT of a region's seed (home) biome cost extra, so a region stays in its home
> biome. Built it (opaque category grid + `HomeExitPenalty` on the cost field, Regions stays biome-blind
> by only testing category equality), swept the penalty 0/4/8/16/32 on real Niflheim. **Negative result,
> reverted:**
>
> | penalty | on-feat% | avg dominant-biome% | blended<55% |
> |--:|--:|--:|--:|
> | 0 (off) | **52.3%** | 73.1% | 46/162 |
> | 4 | 40.7% | 72.9% | 46/162 |
> | 8 | 38.8% | 72.8% | 46/162 |
> | 16 | 39.1% | 72.9% | 44/162 |
> | 32 | 43.8% | 73.0% | 44/162 |
>
> Dominant-biome fraction is DEAD FLAT (~73%); blended count barely moves (46→44); on-feature% gets
> WORSE (52→39%, because the penalty competes with the biome-edge walls and drags borders OFF features).
> **Why it can't work:** pinning region A out of the Mountains doesn't purify anything when there's no
> Mountain-SEEDED region to claim that land — it gets assigned to whoever's adjusted cost is lowest
> anyway. Composition is set by WHERE THE SEEDS SIT; a routing penalty can't change that. This is the
> THIRD empirical confirmation that the blob problem is a SEEDING problem, not a routing one (after raw
> slope and the symmetric v3 both failing to move composition). Lesson held: it SOUNDED right, the seed
> falsified it — measure (on-feature% AND dominant-fraction), don't assume. Do not re-attempt a
> routing-side fix for composition; build the seeding lever.

## The one-line model

**Borders are always firm. Where a terrain feature exists, the firm edge HUGS it at sub-zone
resolution; where none exists, the edge is an honest arbitrary line. Ownership (owned region vs
unowned territory) is a separate axis from edge precision.**

## How we got here (corrections that shaped it)

The thread burned down several wrong framings. Recording them so we don't reintroduce them:

1. **"Soft/indeterminate borders" — KILLED.** I (Starbright) proposed swamp borders could be *fuzzy*
   (uncertain where the line sits). Daniel rejected it: **arbitrary ≠ fuzzy.** An algorithm picking a
   midline with no feature to follow still produces a *firm* line — it just isn't tracing anything.
   "Where is the edge" is always answerable to the meter. There is no low-confidence edge state.
2. **"Unowned ⇒ soft" — KILLED.** Conflated ownership with edge precision. Daniel's counterexample:
   **oceans** are vast *unowned* territory with perfectly *crisp* coasts. Unowned and firm-edged are
   fully compatible. So "not part of a named region" (ocean, wild frontier) is a real category that
   still has firm contour edges.
3. **Classification resolution vs presentation resolution — REFINED by Daniel.** I first said "keep
   classification at 64m, refine only at render time." Daniel pushed: boundary *zones themselves* can
   carry richer-than-64m ownership (contested, up to 4-way at a grid corner) — the sub-zone edge is
   **authoritative data**, not a render-time cosmetic lie. Most *gameplay* still keys off the firm
   interior ("am I firmly in A?"); the sub-zone richness serves the map + territorial layer.

## The two real axes (what survived)

**Axis 1 — edge precision (one treatment, with a degenerate fallback):**
- **Contour-hug (default):** the firm edge follows the real feature — coast, continental-shelf break,
  ridgeline, river, biome transition — at **sub-64m** resolution. Uses data the engine ALREADY
  computes (e.g. `DepthClass.Shallow` is literally the continental shelf; the classifier comment says
  so). Stair-stepping to 64m cell edges would *throw away* accuracy we already have.
- **Stair-step (degenerate only):** the blocky 64m edge. Honest ONLY where there is genuinely no
  feature (e.g. an open-meadow midline between two seeds). Not a choice we make on purpose elsewhere.

**Axis 2 — ownership (orthogonal to precision):**
- **Owned region** — a named region.
- **Unowned territory** — ocean, wild frontier, no-man's-land. Firm contour edges like everything else.
  Good for territorial gameplay: a frontier you contest *because* no one holds it.

**Contested** = a *boundary cell* that carries (a) a sub-zone edge line and (b) the set of RegionKeys
that touch it (2–4). This is the realization of Axis-1 contour-hug + Daniel's "authoritative sub-zone"
point. It requires stable identity → **this is why RegionKey (item 2) had to land first**: you cannot
persist a fractional / multi-owner claim if region identity renumbers under you.

## The mechanism → seam principle (the load-bearing abstraction)

The kind of border a biome has is a property of **how the generator places that biome** (read from the
decomp `WorldGenerator.GetBiome`). Three mechanisms → three seam types:

| Mechanism | Biomes | Border is… | Seam quality | Treatment |
|---|---|---|---|---|
| **Height-defined** | Mountain (`baseHeight>0.4`), Ocean (`≤0.02`), DeepNorth mtn caps | an **elevation contour / isoline** | crisp, walkable (treeline, shore) | strong contour-hug; cheap (just `GetHeight`) |
| **Noise-mask + radial ring** | Swamp, Plains, BlackForest, Mistlands *edge* | where a smooth Perlin field crosses a threshold | **no terrain feature at the edge** | hug the biome transition only; honest arbitrary where nothing else |
| **Relief-rich interior** | Mistlands, Mountains | n/a (interior, not edge) | **vivid** (spires, ravines) | sub-divide the *interior*, not the edge |

Per-biome notes worth keeping:
- **Mountain** = the clean case: border is the `0.4` height contour = the snowline, a real walkable
  edge. But mountains are **closed blobs**, not dividers — a mountain is an island of high ground
  *inside* another biome. Open fork (Daniel's call): is a mountain its own region, a landmark inside
  one, or a multi-region junction (peak = quad point)? Data makes all three cheap.
- **Swamp** = the honest hard case: `0.05–0.25` height clamp makes swamps the *flattest* land in the
  game, placed by a noise blob. **No feature at the edge.** Correct treatment is to hug the biome
  transition and accept an arbitrary-but-firm line elsewhere. (NOT fuzzy — see correction #1.)
- **Mistlands** = inverts the usual relationship: its *outer* border is an invisible noise contour
  (low feature value) but its *interior* relief is the most dramatic in the game (high sub-zone value).
  The biome where interior sub-division pays off most while the edge pays off least. Also the one that
  might eventually want **vertical** ownership (ravine floor vs spire top = different "places") —
  FLAG, do not solve; post-launch rabbit hole.

## The tuning knob: "exaggerated feature-hugging"

The cost-field weights are a dial, per feature. Mild → the edge gently prefers the contour. Cranked →
the edge will **detour** away from the geometric midline to reach the shelf/ridge/shore. This is how you
make a coast *read* as a coast. Tune crisp features (coast, ridge, river) hard; leave soft ones (forest
edge — a gradient over tens of meters) gentle so they only break ties.

## What stays where (architecture guard)

- **WorldZones core** stays the pure region brain: classification + ownership + the cost-field math,
  all at deterministic 64m + sub-zone edge geometry. No Unity, no render.
- **Sub-zone aesthetics / map rendering** belong to the *consuming* mod or the render layer (the ESP,
  the regional-map provider). The library computes the authoritative edge; the hands draw it.

## What's still gated on the ESP (cannot decide from the top down)

- **Per-biome exaggeration weights** — an eyeball call. Does a 64m staircase even *read* as wrong when
  you're standing in it, or does terrain noise hide it? Does a cranked coast detour feel right or
  contrived? You have to *walk it*.
- **Whether contested sub-zone edges are worth it for launch**, or firm-interior-only is enough.
- All of this needs the client rig (the ESP is a rendering plugin — CLIENT Unity assemblies).

## ESP — two layers (status 2026-06-23)

The "ESP" is two distinct things; keep them straight:

1. **Offline ESP** (`tools/borders-explorer/offline_esp.py`) — ✅ BUILT + VERIFIED. The decision
   *instrument*: composites hillshade + biomes + v3 regions + features + seeds + RegionKey names on real
   terrain. This is what we use to DEVELOP the algorithm (iterate the cost field, measure on-feature%).
   It is NOT the in-world view — it's the lab bench. Renders at ~75% on-feature on Case C; 5 named
   regions. Fully reproducible from the seed on this box.

2. **In-world ESP** (`src/WorldZones.Mod.RegionOverlay/Esp/RegionBorderEsp.cs`) — 🔴 SCAFFOLD,
   compile-unverified. The BepInEx `MonoBehaviour` that draws ground-projected border lines you walk up
   to (Unity `LineRenderer`, terrain-projected via `GetHeight`, hotkey toggle, zone-crossing refresh,
   pooled renderers). The region/border MATH it consumes is verified; the **geometry it would draw is
   proven** (Python mirror of `Rebuild()`: 100% of emitted segments sit on real borders, correct
   once-per-edge coverage, real terrain heights). But the **Unity rendering shell cannot be compiled or
   walked here** — needs the Windows CLIENT rig ($(ValheimModdedPath), client Unity assemblies). Do not
   claim it works until built against a client and seen on the ground. This is the gating dependency for
   every eyeball question above.

> The honest split: we have taken the border algorithm as far as it can go from the top down — v3 is
> measured, the offline ESP shows it, the in-world ESP geometry is proven. The REMAINING work (does it
> *feel* right, what weights, staircase-or-contour) is genuinely blocked on walking it, which is blocked
> on the client rig. That's the real long pole, exactly as the roadmap said.

## Demonstration + empirical findings (2026-06-23, seed HHcLC5acQt)

Sampled **real terrain from the verified port** (`tools/borders-explorer/`, 8m grid, 1536m windows)
at three scanned junctions, ran today's unweighted BFS vs proposed weighted-Dijkstra growth from the
identical seed set, rendered on hillshaded terrain. Three findings, including a load-bearing NEGATIVE
result:

### ✅ Finding 1 — the thesis holds where terrain is dramatic
Case C (mountain ring, 194m relief): the proposed border visibly **wraps around the feet of two peaks
and runs up the valley between them**, where today's BFS cuts a straight diagonal across the saddle.
Watershed routing is real on real terrain.

### ✅ Finding 2 — effect scales with relief, degrades gracefully on flats (as predicted)
Mean border-slope lift, proposed vs today: Case A (flat coast/swamp/plains, 54m) **+5%**, Case B
(mistlands/mountain, 165m) **+6%**, Case C (mountain, 194m) **+9%**. Where there's no feature, weighted
≈ unweighted — which is correct, not a failure. *Mechanism-determines-seam confirmed in data.*

### 🔴 Finding 3 (NEGATIVE, load-bearing) — raw slope is the WRONG cost term
The "exaggeration knob" does NOT behave like a clean dial. Sweeping the slope weight 0→3→6→12→25→50 on
Case C, border-slope wobbled and **plateaued at ~+10%** (0.485→0.515→0.530→0.499→0.544→0.535) — even
*regressed* at w=12, and 8× more weight barely moved it. **Why:** slope is a dense, noisy field — every
hillside has slope everywhere, so multiplying by it makes ALL land mildly expensive without carving out
the specific crest a border should follow. Dijkstra still mostly minimizes path length; slope can't make
it *commit* to a ridge because raw slope has no sharp "this is a ridgeline" signal.

### The revision this forces
**Crisp seams must be the load-bearing cost terms; raw slope is a weak tiebreaker, not the driver.**
The cost field must be built from *extracted features* (intensity ≠ the answer; extraction is):
- **Ridge skeleton** — height Laplacian (concave-down crests), thinned to a line, not a gradient.
- **Shoreline** — the Land/Shallow boundary — ALREADY crisp + classified. Was ignored in v1; shouldn't be.
- **Biome transition edges** — ALREADY crisp.
- **Valley/drainage skeleton** — Laplacian concave-up. The channels water runs in.

This is exactly the kind of thing only real terrain reveals: on paper "weighted Dijkstra + slope cost"
sounded sufficient; the seed falsified it. v2 cost field = crisp extracted features, slope demoted.

> Note: the v1 design over-weighted slope. The corrected hierarchy is
> **shore ≈ biome-edge ≈ ridge-skeleton ≫ raw slope.**

### Swamp features (RESOLVED 2026-06-23 — measured on real terrain)
Daniel's push: swamps may have *meaningful internal* features (I'd called them featureless). Tested
two hypotheses against the real seed; **both my guesses failed, Daniel's instinct was right but
redirected to the actual mechanism:**
- ❌ **Internal drainage channels** (Laplacian valleys restricted to swamp): valley-cover 4–10%,
  isolated 38–84% → **incoherent.** Swamps have no coherent internal channel structure at 8m.
- ❌ **Basin hypothesis** (swamp fills a depression): FALSE. Swamps measured *higher* than surrounding
  dry land in 2 of 3 patches (A +11m, B +2.7m), lower only in C. Swamps are noise-mask blobs placed
  wherever the Perlin field says — NOT topographic basins.
- ✅ **Swamp RIM (biome edge against dry land):** mean-neighbors 1.32–1.39, ~12% isolated → **LINE-LIKE.**
  The swamp's meaningful border feature is its own biome-transition rim — crisp and walkable. So a swamp
  border CAN feature-hug: not via internal water, but by following the biome edge it already has.

### 🔴 THE corrected cost-field model (measured, supersedes earlier slope/ridge framing)
Coherence test across all 3 patches (mean in-set neighbors; >1.3 & <25% isolated = LINE-LIKE):

| feature | A | B | C | verdict |
|---|---|---|---|---|
| **biome edge** | 2.72 / 0% | 2.64 / 0% | 2.57 / 0% | ✅ **LINE-LIKE everywhere — the load-bearing term** |
| shoreline | 1.11 / 17% | 1.16 / 14% | 1.00 / 20% | ~ borderline (crisp but sparse; it's a special biome edge) |
| ridge skeleton | 0.68 / 51% | 1.17 / 32% | 0.67 / 52% | ❌ too noisy at 8m to drive a border |
| valley skeleton | 0.45 / 67% | 1.13 / 29% | 0.56 / 60% | ❌ too noisy |

**Both my confident theories — v1 raw-slope AND v2 ridge/valley skeletons — are too noisy on real
terrain.** The ONE feature that is crisp and line-like in every patch is the **biome transition edge
itself.** So the grounded cost field is:

> **Primary: biome-transition edges** (proven crisp everywhere).
> **Secondary: shoreline** (the biome edge against water — crisp, a bit sparse).
> **Tertiary, tie-break only: slope/ridge** where it happens to be coherent (dramatic mountains, Case C).

This is *simpler* than the multi-feature cost field I kept proposing, and it's earned: every term
measured, both elegant theories (basins, ridge skeletons) falsified, the swamp special-cased into the
general rule. The seed taught us the answer none of us had on paper. **This is the v3 cost field to
prototype next.** Slope is demoted from "the driver" all the way to "occasional tie-break."

> Methodology note for future sessions: don't trust a feature because it sounds right (slope, ridges,
> channels, basins all *sounded* right). Extract it on the real seed, measure line-coherence
> (mean-neighbors + isolated-fraction), and only then weight it. The coherence metric in
> `feature_detect.py` is the gate.

Tools: `tools/borders-explorer/` (ExportPatch.cs, ScanWorld.cs, analyze_patch.py, feature_detect.py).
Heavy binaries/renders are NOT committed (regenerate from the seed); the tools are.

### ✅ v3 cost field BUILT + MEASURED WINNER (2026-06-23, `cost_v3.py` / `render_v3.py`)
Built the biome-edge-driven cost field and scored it by the RIGHT metric — **"what % of the resulting
border sits ON a real feature (biome edge / shore)?"** (the weak v1 proxy was mean-slope). Result:

| patch | BFS today | V1 slope | **V3 barrier** |
|---|---|---|---|
| A (flat coast/swamp) | 95.0% | 96.9% | 96.4% |
| B (mistlands/mtn) | 54.8% | 57.5% | **70.1%** |
| C (mountain ring) | 54.5% | 53.9% | **72.5%** |

**+16–18 points on feature-rich terrain; v1-slope did nothing (±3).** Flat patch ~95% for all (almost
everything there IS a feature). Visually confirmed: BFS cuts straight midlines through biome interiors;
**v3 traces forest/plains transitions, BlackForest patch outlines, and coastlines — clean, not tendrils.**

### 🔴 THE sign-flip lesson (load-bearing, nearly shipped backwards)
First v3 attempt made biome edges **CHEAP** ("attract the frontier onto edges"). It scored *worse than
BFS* (43%) — because cheap edges are **highways**: one region tendrils ALONG a biome boundary across the
whole map. The fix: features must be **EXPENSIVE TO CROSS (walls), not cheap (highways).** A wall stalls
each region's growth at the feature so the two regions **MEET** there. This is the original watershed
intuition (rivers = high cost) that I lost when switching from slope to biome-edges. **Rule: to make
borders fall on a feature, make the feature a barrier, never an attractor.** The data caught the inverted
sign — another "only real terrain reveals it" save.

> v3 cost field (the one to port into the engine when borders get rewritten):
> `cost(enter cell) = 12 if biome-edge, 8 if shore, 1 if interior` → multi-source Dijkstra from seeds.
> Tunable: the 12/8/1 ratios are the per-feature "exaggeration" weights. Slope can return as a *small*
> additive term for the dramatic-mountain tie-break, but it is NOT the driver.

## The swamp land-floor (2026-06-26) — rescuing swamp into regions

**Symptom (Daniel's walk):** swamp wasn't reliably showing up inside regions — patches of swamp had no
fill and no borders, as if the region system didn't see them.

**Root cause (measured, real Niflheim seed ForTheWort):** `ZoneClassifier` decides "is this zone land?"
purely by depth — one height sample at the zone centre, `terrainY ≥ 30` (the waterline) → Land, else
dropped to Shallow/Deep and excluded from every region. But **swamp terrain straddles the waterline**: its
measured height band is **24.8–33.8 m** (swamp is the soggy biome; Bonemass/Sunken-Crypt spawn at
"Altitude 0–2" above water, and it's full of standing-water pools). So the single centre-sample is a
coin-flip and **64.4% of swamp zones (1268 / 1969) get dropped** — holes punched straight through solid
swamp, not just trimmed at the edge. Swamp is uniquely wrecked because, unlike meadows/plains/black-forest
(which have dry interiors well above 30 m and only lose their offshore fringe), swamp has **no dry
interior** — the whole biome lives at the waterline.

**The fix — a swamp-gated land floor (`RegionBuildOptions.SwampLandFloorMeters`, default 22 m).** A zone is
Land if `height ≥ 30 OR (biome == Swamp AND height ≥ 22)`. Implemented as
`ZoneClassifier.ClassifyWithSwampFloor` (pure: takes height + biome funcs, no Unity), wired in
`WorldZonesRuntime.Build` (where the sampler is available; the topology layer stays biome-blind). A null
floor falls back byte-identical to the legacy depth-only classify.

**Why a swamp-gated floor and not the alternatives** (all three modelled + measured against real Niflheim,
rendered to PNG for eyeball review):
- **Multi-sample (centre + 4 corners, land if any ≥ 30):** heals most holes but inflates *every*
  coastline by up to a zone (it's an OR over all biomes), and still leaves a fringe — broad shallow blast
  radius. Rejected.
- **Biome-aware (land if biome ≠ Ocean):** fully heals swamp but pulls *all* sub-waterline coastal shelf
  of every biome into "land" (measured: BlackForest/Plains/Mistlands terrain tagged that biome reaches
  down to y=0), growing regions out over the shallows and risking island-merge via the shelf. Biggest
  blast radius. Rejected.
- **Swamp-gated floor (chosen):** because the rule is gated `biome == Swamp`, it is *incapable* of
  touching any other biome — **measured: 0 non-swamp zones change across the whole world.** The
  "special-case" IS the containment. Floor 22 m sits just below swamp's measured 24.8 m floor, so it fully
  heals swamp (**64.4% → 0% dropped at floor ≤ 22; 1.6% at the conservative 26**) without reaching into
  open water.

**Accepted consequence — RegionKey renumber.** Adding swamp land shifts seed placement + growth, so region
keys/names move (e.g. Niflheim spawn went `r.3.-10` Meadows → `r.3.11` BlackForest "the Highlands of
Jarnfjord"). Keys are seed-coordinate-derived, so any classification change does this — it's free
pre-public-playtest but invalidates persisted discovery state, so it must land **before** players have
discovery history. Accepted 2026-06-26 (pre-public-playtest).

**Guarantees held (tested, `SwampLandFloorTests` + `GazetteerCompositionTests`):** the
`SampledLandZones ≤ LandZones` fringe-leak invariant still holds (the rescue does not reopen the shallow-
fringe contamination bug); every below-30 m region contains swamp (0 non-swamp dragged down); null floor is
byte-identical to legacy. The fix only ever ADDS land (monotonic).
