# WorldZones — Re-orientation (2026-06-15)

> You stepped away from this in early March and asked "where were we and what were we
> trying to achieve." This is the grounded reconstruction, read from the specs + code,
> not memory. Status as of the `main` HEAD (last commit `4fca9eb`, 2026-03-02).

## The one-sentence purpose

**A foundational library mod that deterministically divides a Valheim world into named
regions, so other mods (and players) can reason about "what place am I in."** It is a
*substrate* — SBPR/Trailborne and others consume it; it is not a gameplay mod itself.
(Constitution: "Library-First.")

## What you were actually building (the 4-spec arc)

| Spec | Intent | Status |
|---|---|---|
| **001 worldgen-library** | Pure-C# port of Valheim's biome+height gen so region algos can be tested offline against real terrain | ✅ **bit-exact** vs the game assembly (`7b34f19`). Solid. |
| **002 region-skeleton** | The geographic model: classify zones, find landmasses, partition into proto-regions | ✅ built, ⚠️ **borders are the placeholder** (see below) |
| **003 region-name-overlay** | Player-facing payoff: show current region name on minimap + discovery banner | ✅ built (500-name catalog, deterministic) |
| **004 inland-water-attribution** | Lakes/enclosed water belong to a region; ocean stays unowned | ✅ built as a post-pass |

## The model in one breath

Everything runs on **64×64m zones** (matching Valheim's `ZoneSystem.c_ZoneSize`).
Each zone → `Land / Shallow / Deep`. Connected land = a **Land Component**. Land∪Shallow
= a **Shelf Component** (island coherence). Small land = **Minor Islet** (ignored).
Qualifying components get **seed zones**, and regions grow from seeds by **BFS flood-fill**.

## 🔴 The thing that was bugging you — found it

You remembered "a whole thing that traced features and rivers, not sure it worked." Here's
the truth:

- **Rivers exist only in the terrain layer.** `WorldGenerator.AddRivers()` folds rivers
  into *height*. Faithful port, works fine.
- **Region borders are terrain-BLIND.** `ProtoRegionGenerator` grows regions by **unweighted,
  land-only, 4-neighbor BFS** from seed zones (`ProtoRegion.cs:6` "unweighted BFS";
  `ProtoRegionGenerator.cs:187` "land-only in v0"). A grep for `river|ridge|slope|feature|
  watershed` across the entire Regions project returns **nothing**.

So borders fall on a **geometric midline between seeds** — they have *no relationship* to
rivers, ridgelines, or coastlines. **There is no feature-tracer.** What you remember as
"the river-tracing thing" was an *intention that spec 002 explicitly deferred* ("feature-aware
borders: ridges, rivers, cliffs" → Out of Scope), never built. You weren't unhappy with a
broken tracer — you were unhappy that **the borders are arbitrary flood-fill lines**, and the
fix was a deferred idea, not real code.

The spec even names the lever: FR-006 says v0 is land-only BFS "**weighted Dijkstra with
shallow traversal deferred to a future iteration**." That future iteration is the backtrack.

## Why this kept not getting fixed (the pattern)

The 004 research doc shows the habit: when adding inland water, you **rejected** "rework proto
generation" and "full region model rewrite" as out-of-scope, and bolted on a post-pass instead.
Reasonable each time — but four features deep, the *foundational* boundary model is still the v0
placeholder. The borders never got the rewrite because every feature preserved-and-worked-around
them. That's why it nags.

## The success criteria you wrote for yourself (002) — and how borders measure up

- "Results visually align with **human intuition** on real maps" ← **the failing one**
- "Mainland regions do not balloon along coasts"
- "Archipelagos remain coherent"
- "Ashlands / Deep North naturally isolate"

The engine satisfies the *topological* criteria. The **"human intuition"** one is exactly what
terrain-blind BFS can't satisfy — a border that cuts straight across a meadow instead of
following the river reads as wrong to a human, even though it's a valid partition.

## What's solid vs what's the weak layer

- ✅ **Engine is real:** bit-exact worldgen, deterministic classification, working inland-water
  attribution, name overlay, full tessellation computed **client-side in memory** at world load.
- ⚠️ **Boundaries are the v0 placeholder:** unweighted geometric flood-fill, terrain-blind.

## The decision in front of you ("possibly backtrack")

1. **Backtrack the boundary model** — replace unweighted BFS with **cost-weighted Dijkstra**:
   make rivers / coastlines / high-slope ridges *cheap seams* so borders prefer to fall along
   real terrain features. This is the "feature-tracing" you imagined. New spec (005). Real work,
   well-isolated. **The actual fix.**
2. **Ship terrain-blind borders** — they're deterministic and correct, just aesthetically
   arbitrary. OK if region *identity* matters more than border *placement* for launch.
3. **Cheap hybrid** — bias BFS to stop at water + high-slope deltas (data already computed).
   ~80% of the visual win, ~20% of the work.

## 🔧 Prerequisite for deciding: the "regions ESP" debug tool

You can't judge borders from a top-down PNG — you have to **see them where you walk**. The good
news: the client **already holds the full `regionIdGrid` + classified `ZoneGrid` + rivers** in
memory (`RegionOverlayPlugin.cs:183-209`). An in-world debug overlay (toggle layers: borders,
classification, seeds, rivers, inland water) is **almost pure rendering, near-zero new compute.**
The money shot: **rivers + borders overlaid** — walk around and *see* they don't correlate.

→ This is the natural **first card** for the regions lane. It unblocks the backtrack judgment.

## Open questions for Daniel

1. ESP render path: **world-space ground lines** (real ESP, judge borders by walking) vs
   **map overlay** (faster, but fights SBPR's no-map ethos; it's dev-only so defensible)?
2. ESP home: new `WorldZones.Mod.DevTools` plugin (matches your own `mod-ideas.md` proposal)
   vs a flag in the existing RegionOverlay mod?
3. Backtrack scope: option 1 / 2 / 3 above — decide *after* the ESP shows you the real borders.
4. Board: spin up a `worldzones` kanban board with the ESP as card #1, or keep it conversational
   until direction is locked?
