# Contract & Invariants — the start-over insurance

> The fear driving this whole effort: *"build on the borders, get them wrong, have to start
> over."* This doc is the cure. It pins **what must stay true** (invariants), **what is
> swappable** (the seams), and **what the contract is** between the library and its
> consumers — so exploration upstream stays cheap and "doing borders wrong" costs one
> module rewrite, not a project restart.
>
> Authored 2026-06-15. This doc asserts **direction**, not just description — it captures
> the staged-pipeline architecture locked in conversation that day. Daniel gates changes to it.

## The core insight

You don't prevent "doing it wrong" by choosing the right border algorithm the first time.
You prevent it by making the choice **cheap to reverse**: lock the *contract* (the shape of
a region + the interface that produces it), and the border algorithm becomes a swappable
module behind it. Everything downstream depends on the contract, never on the algorithm.

## Invariants — DO NOT BREAK (any algorithm swap must preserve these)

1. **Determinism.** Same seed + same parameters ⇒ byte-identical `regionIdGrid`, on every
   client, every run. This is non-negotiable: Valheim computes per-client and they must
   agree. Every RNG use is seeded (`Random(seedRng)`); no wall-clock, no unordered-dict
   iteration that escapes to output, no parallelism that reorders.
2. **64m zone grid.** All region logic is zone-quantized to `ZoneGrid.ZoneSize = 64`,
   aligned to Valheim's `ZoneSystem.c_ZoneSize`. Sub-zone precision is not a thing regions
   promise.
3. **Post-gen always runs.** Even an authored/pre-gen world must be re-read by the post-gen
   stage to produce the *actual* `regionIdGrid` from the *actual* terrain. Pre-gen declares
   intent; post-gen reads reality. Never let "we authored it" skip the read.
4. **Every land zone in a qualifying component → exactly one region.** No gaps, no overlaps,
   among seeded components. Minor islets (<12 zones) are intentionally unassigned (metadata).
5. **Regions never cross Deep water** (v0). Shallow may be a 1-zone fringe; Deep is
   impassable to growth. (Weighted shallow traversal is a deferred future, not a break.)
6. **Library stays Unity-runtime-free.** Core libs (WorldGen, Regions) may reference
   UnityEngine.CoreModule at *build* time for math parity, but MUST NOT depend on BepInEx,
   Harmony, or game assemblies. Gameplay integration lives only in the mod layer.

## The contract (what consumers depend on)

These are the surfaces other mods build against. Changing them is **expensive** (breaks
consumers) — this is the real foundation, far more than the border algorithm.

- **`IWorldDataProvider`** = `{ string WorldId; float WaterLevel; float GetTerrainHeight(x,z); }`
  — the consumer's terrain input. Stable.
- **`IRegionLookupService.ResolveCurrent(x,z)` → `RegionLookupResult`** — the "what region
  am I in" query. `RegionLookupResult = { bool HasRegion; int? RegionId; string RegionName;
  RegionResolutionReason }`. Stable.
- **`ProtoRegionGenerator.GenerateLand(...)` → `ProtoRegionResult` + `out int[,] regionIdGrid`**
  — the production seam. Pure, deterministic. The *shape* (grid in → regionIdGrid out) is
  the contract; the *algorithm inside* is swappable.
- **Region identity** = a stable `int RegionId` per world, name via `RegionNameCatalog` /
  `RegionGuidNameService`. Consumers key meaning off `RegionId`.

> **Meaning is the consumer's, not the library's.** WorldZones provides the *noun* (a named,
> bounded, queryable partition). Ownership systems, tiers, guild maps, providers — those are
> *verbs* consumers conjugate. The library must stay meaning-agnostic; that's the whole
> Library-First bet, and it's what lets Niflheim be consumer #1 without the library having
> to know about Niflheim.

## Swap-points — where you CAN change things freely

Because the contract is the grid-in/grid-out shape, these are all behind it and safe to
rewrite without touching consumers:

| Swap-point | Today (v0) | Future without breaking anything |
|---|---|---|
| **Border growth** (`GenerateLand` BFS body) | unweighted land-only BFS | weighted Dijkstra; terrain-cost (river/coast/ridge seams); cheap hybrid (stop at water + high-slope) |
| **Seed placement** (`PlaceSeeds`) | random-within-component | authored intent ("lower"); poisson-disc; tier-targeted |
| **Classification** (`ZoneClassifier`) | depth-only (land/shallow/deep) | + biome-variant signal (if Valheim 1.0 exposes queryable intra-biome data) |
| **Merge policy** (`MergeTinyRegions`) | longest-border neighbor | tier-aware merge; coastline-aware |
| **Upstream stages** | none (vanilla terrain) | pre-gen intent, worldgen-guided-by-intent (Level 3) — *added in front of* post-gen, never replacing it |

**The rule:** anything that still emits a deterministic `regionIdGrid` honoring the
invariants is a legal swap. The ESP (see below) is how you *judge* a swap, not part of the
contract.

## The ambition ladder (context for what's a swap vs. a new stage)

```
Level 1  Overlay   read vanilla terrain → regions          ← HAVE IT (this is WorldZones)
Level 2  Steer     score/select vanilla seeds for a good   ← the cheap unlock; needs an IR
                   progression story; don't change terrain     (lift + compare in a loop)
Level 3  Replace   author terrain to satisfy region intent ← post-launch moonshot; needs "lower"
```
- **Level 2 (seed selection)** is the high-value, low-risk play: `lift(seed) → IR`,
  `compare(IR, target)` in a loop, pick the best vanilla seed. Network-safe, 1.0-safe.
- The **IR** (serializable Region Intent model) is the shared representation both directions
  speak: `lift(world)→IR` (analysis, ~80% built) and `lower(IR)→world` (synthesis, future).
  A **DSL** is a *deferred front-end* that compiles to the IR — earn it only when raw IR
  authoring hurts; not a first artifact.
- **Don't design the IR's fields yet** — that wants the ESP first, so fields are chosen
  against regions actually walked, not imagined.

## The ESP (regions debug overlay) — the instrument, not the contract

A dev-only BepInEx overlay drawing region layers into the 3D world (ground-projected lines).
Layers: borders, zone classification, seeds, rivers, inland water, tier (future). The
rivers+borders overlay is the diagnostic that makes terrain-blindness *visible*. Near-zero
new compute (tessellation already in client memory). **Decision: ground-projected lines.**
Blocker: needs a client build/test environment (the dev box's server assemblies lack
client Unity modules).

## Known gaps to close before "shippable"

1. **Borders** are the v0 placeholder (this doc's whole subject). Fix via a swap-point.
2. **Empirical validation deleted** (`aea19e0` removed the PNG-vs-real-map ground-truth
   tests). Only structural validation remains (102 tests, all green, but none assert "looks
   like the real Valheim map"). Re-establish before ship.
3. **Thunderstore packaging** — manifest/icon/README + in-game validation on a real client.
