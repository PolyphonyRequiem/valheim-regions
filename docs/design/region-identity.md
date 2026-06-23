# Region identity: RegionKey (coordinate-derived)

> **Status:** Locked 2026-06-22 (Daniel). Implementing now. No migration path — nothing is in
> production, so a clean format change beats a migrator. Migration planning is explicitly deferred
> (see "Future work" below) — to start "before too long," not now.

## The problem (grounded in code)

A region's identity today is **its seed's index in the `seeds` list** (`ProtoRegionGenerator.cs`:
`regionIdGrid[seed] = i`). The player-facing **name** is a deterministic hash of that integer
(`RegionGuidNameService.CreateDeterministicName(worldId, regionId)` → 500-name catalog). So the
chain is **seed-list position → int ID → name.**

That index is a product of seed *placement order*, *count*, and *merge order*. Every change on the
roadmap perturbs it:
- weighted-Dijkstra borders (the backtrack) → different seed handling → list shifts
- Valheim 1.0 / Deep North → different components → list shifts
- authored seeds (L3) → list is entirely different

When the list shifts, every region silently **renumbers** → names change → any persisted region
reference points at the wrong land. `DiscoveryState` persists region references, so this is
catastrophic *after* anything ships. (Today it's safe only because nothing is in production.)

## The decision: identity = place, not list-position

A region's durable identity becomes a **`RegionKey` derived from a seed's zone coordinate**, not its
list index. The integer ID stays as an **internal** BFS/grid scratch label (the `int[,] regionIdGrid`
is unchanged); `RegionKey` is layered on top only at the **naming / lookup / persistence** boundary.

A zone coordinate `(zoneX, zoneZ)` is stable under list reordering, seed-count changes elsewhere, and
merge-order changes — exactly the perturbations we know are coming.

### The merge case → Option B (lowest-coordinate-keyed) — LOCKED

When two regions merge (`MergeTinyRegions`: a tiny region is absorbed into its longest-border
neighbor), multiple seed coordinates collapse into one region. **The region's identity is the
minimum seed coordinate among ALL seeds that ended up in it**, by a fixed total ordering
(`zoneX` then `zoneZ`).

- **Why B over A (survivor-keyed):** B is stable even if the *merge order or rule* changes, as long
  as the same seeds end up clustered together — which is the whole point, because the border rewrite
  *will* change which zones merge. A (absorbing-seed-wins) would still reshuffle identities when merge
  behavior changes. Daniel locked B for exactly this reason.

### RegionKey format

Canonical string key from the min seed zone coordinate, e.g. `r.<zoneX>.<zoneZ>` (signed). Stable,
human-debuggable, world-independent (the worldId is already a separate axis in naming + persistence).
Names derive from the key instead of the int index — same catalog + mix function, fed the coordinate.

## What this does NOT solve (honesty guard)

RegionKey makes **identity** survive list/merge churn. It does **not** make **territory** immortal:
if a *border moves* while the seed stays put, the key is stable but the land it covers changed
("same region, now includes the lake"). Extent-stability is a **separate, later** contract piece
(roadmap risk #3), not claimed here.

## Future work (deferred, not now)

- **Migration framework.** Once anything ships to players, format changes need a migrator. Start
  planning "before too long" (Daniel, 2026-06-22). The persisted document already carries
  `schemaVersion` (currently 2) — the lever exists. Not built now because nothing is in production.
- **Evocative names.** The current 500-name catalog is **placeholder** (Daniel). Future direction:
  more descriptive, varied, evocative names — and a coordinate-derived key *enables* spatially-aware
  naming (latitude/biome/landmark-informed) since identity now carries place. Stable identity is the
  prerequisite for names that are meant to *stick* in a player's memory.
