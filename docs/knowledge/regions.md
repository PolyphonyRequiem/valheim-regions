# Regions — the region pipeline

> How `WorldZones.Regions` turns terrain into named regions. Grounded against the source
> (`wzq.py type <T>`). The honest weak point — terrain-blind borders — is documented in
> full in §borders, because every gameplay ambition (tiers, territory, providers) raises
> the stakes on it.

## The pipeline (post-gen stage — the part that always runs)

Everything operates on a **64×64m zone grid** (`ZoneGrid`, `ZoneSize=64`, matching
Valheim's `ZoneSystem.c_ZoneSize`). The flow, in order:

```
1. Classify        ZoneClassifier.Classify       → each zone: Land / Shallow / Deep
2. Components       ComponentLabeler              → connected Land components; Land∪Shallow shelf components
3. Archipelago      ArchipelagoDetector           → flag island clusters (metadata only)
4. Seed             PlaceSeeds (inside GenerateLand) → deterministic seed zones per qualifying component
5. Grow borders     multi-source BFS (GenerateLand) → every land zone → nearest seed (THE BORDER STEP)
6. Merge            MergeTinyRegions              → fold sub-threshold regions into longest-border neighbor
7. Shallow fringe   ExpandRegionsIntoAdjacentShallowZones → 1-zone shallow lip per region
8. Inland water     InlandWaterAttributor         → enclosed lakes → owning region (ocean stays unowned)
9. Result           ProtoRegionResult             → regionIdGrid + areas + metadata
```

Entry point: **`ProtoRegionGenerator.GenerateLand(...)`** (L88) — a pure static function,
grid in / `regionIdGrid` out, deterministic from `seedRng`. This is *the* seam (see
contract-and-invariants.md).

## Classification — `ZoneClassifier` (L52/L90)

Depth-only, biome-ignored:
- `terrainY ≥ WaterLevel (30m)` → **Land**
- `terrainY ≥ WaterLevel − ShelfMaxDepth (10m)` → **Shallow** (continental shelf)
- else → **Deep**

Two overloads: a `Func<float,float,float>` height sampler (for synthetic test terrain) and
an `IWorldDataProvider` (real world). Same logic; the provider form is what the mod uses.

## Components & archipelago

- **`ComponentLabeler`** — connected-components. Land components (land-adjacency only) and
  shelf components (Land∪Shallow). Shelf components = "island coherence realms."
- **`ArchipelagoDetector`** — flags a shelf as an archipelago candidate if it holds ≥N land
  components with no single dominant one. **Metadata only — creates no regions** (v0).
- **Minor islets** — land components below `MinComponentZonesForProto` (default **12**
  zones) are tracked but get no region.

## Seeding — `PlaceSeeds` (L501)

Per qualifying component: `seedCount = max(1, componentZones / targetZonesPerRegion)`.
Seeds placed by `System.Random(seedRng)` over the component's land zones. **This is where
"authored intent" would later be injected** — today seeds are random-within-land; the
zones→world ("lower") direction means *seeds come first as design intent* and terrain is
grown to honor them (the Civ "tectonic seeds" model). The seam already speaks in seeds.

## 🔴 Borders — `GenerateLand` BFS (L88) — THE WEAK LAYER

**Unweighted, land-only, 4-neighbor multi-source BFS.** Seeds enqueue first; each zone is
claimed by whichever seed's flood-fill reaches it first. `Neighbors = {(1,0),(-1,0),(0,-1),(0,1)}`.

```
while queue: cur = dequeue
  for each 4-neighbor in-bounds, unassigned, Land:
      assign to cur's region; enqueue
```

**The finding:** a `grep river|ridge|slope|feature|watershed` across the entire Regions
project returns **nothing**. Borders fall on the **geometric midline between seeds** — they
have *no relationship* to rivers, ridgelines, or coastlines. Rivers exist (worldgen.md) but
only carve *height*; the border step is **terrain-blind**.

- This was a **deliberate v0 placeholder.** Spec 002 FR-006: land-only BFS, "weighted
  Dijkstra with shallow traversal **deferred to a future iteration**." Feature-aware borders
  (ridges/rivers/cliffs) were explicitly Out of Scope.
- It **calcified**: specs 003 (names) and 004 (inland water) built *on top of* the
  placeholder instead of replacing it; 004's research doc twice rejected "rework proto
  generation" as out-of-scope. Three layers now sit on the v0 borders.
- **Why it now matters:** when borders were flavor labels, arbitrary was fine. The moment a
  border becomes a *territory edge* or a *map-provider extent* (the SBPR vision), "arbitrary
  flood-fill line" becomes a gameplay problem. Spec 002's own success criterion —
  "*visually align with human intuition on real maps*" — is the one terrain-blind BFS can't
  satisfy.

**The fix lives behind one function.** Because `GenerateLand` is pure and isolated, the
border algorithm is swappable without touching anything downstream (see
contract-and-invariants.md §swap-points). Options: weighted Dijkstra (rivers/coasts/ridges
become cheap seams), or the cheap hybrid (bias BFS to stop at water + high-slope zones —
data already computed). **Decide after the ESP shows real borders in-world.**

## Merge & fringe

- **`MergeTinyRegions`** (L379) — iterative: regions below `minRegionZones` (default 6) fold
  into their **longest-border neighbor** until stable. Tie-breaks deterministic.
- **`ExpandRegionsIntoAdjacentShallowZones`** (L313) — gives each region a 1-zone shallow lip
  so coastlines aren't raw land edges.

## Inland water — `InlandWaterAttributor` (L23, spec 004)

A **post-pass** (doesn't touch land assignment):
1. `InlandWaterConnectivityCategorizer` flood-fills from map boundary → ocean-connected vs
   enclosed (inland) water.
2. Inland bodies attributed to a neighboring region (deterministic tie-break).
3. Ocean-connected water stays **unassigned**. Unreachable inland bodies → safe-fail metric.

## Consumer surface (what a mod sees)

- **`IWorldDataProvider`** — `{ WorldId, WaterLevel, GetTerrainHeight(x,z) }`. The consumer
  supplies terrain; the library classifies it. (`ValheimWorldDataProvider` wraps the game.)
- **`IRegionLookupService.ResolveCurrent(x,z)`** → `RegionLookupResult { HasRegion,
  RegionId, RegionName, ResolutionReason }`. The "what region am I in" query.
- **`RegionLookupService`** holds the live `ZoneGrid` + `regionIdGrid` — i.e. the **full
  tessellation lives in client memory** (`RegionOverlayPlugin.cs:183-209`). This is why the
  ESP overlay is near-zero new compute: the data is already there.
- **`RegionNameCatalog`** (spec 003) — 500 deterministic names; `RegionGuidNameService`
  resolves id→name stably per world.

## The mod — `WorldZones.Mod.RegionOverlay`

Thin BepInEx layer. Computes the tessellation at world load, patches `Minimap.UpdateBiome`
to show the current region name + a one-time discovery banner, persists discovery state.
⚠️ **Client-only**: references client Unity modules (`UnityEngine.dll`, IMGUI) via
`$(ValheimModdedPath)` → a *modded client* install. Does **not** build against the
server-only assemblies on a headless/server box. The ESP plugin inherits this constraint.

## Quick index

```bash
wzq.py type ProtoRegionGenerator   # the boundary engine
wzq.py method GenerateLand         # the pipeline entry (L88)
wzq.py method Classify             # zone classification
wzq.py type InlandWaterAttributor  # spec-004 post-pass
wzq.py type RegionLookupService    # the consumer query surface
```
