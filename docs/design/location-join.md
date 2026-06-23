# Location / POI sidecar — `.db` → region join

> **Status:** DESIGN, not built (2026-06-23). Specifies the locations sidecar for the gazetteer.
> Verified feasible: Niflheim's `.db` demonstrably contains the location instances. Daniel's call on
> scope/priority.

## What it is

A gazetteer **sidecar** answering "what landmarks are in region R" — crypt count, boss presence,
trader presence, runestones, tar pits, etc. Joins the core gazetteer on `regionKey`. Unlike the
modeled vegetation sidecar, **this data is real and ground-truth** — it's read, not estimated.

## Why locations are real (and ore isn't)

Valheim generates **locations eagerly** at world-create (`ZoneSystem.GenerateLocations`) and persists
every instance into the world `.db` — explored or not. So the whole map's locations already exist as
data. (Contrast `PlaceVegetation`/ore, which is lazy and absent until a player loads the zone — see
`vegetation-resource-model.md`.)

Confirmed by a raw string scan of `niflheim.db` (3.2 MB): ~1825 Ruin, 778 Crypt, 692 Runestone, 225
TarPit, 200 DrakeNest, 188 TrollCave, plus the bosses (Eikthyr, Bonemass, GDKing, GoblinKing,
Dragonqueen) and Vendor (Haldor) tokens. The data is there; it needs a proper decoder, not a regex.

## The build (when prioritized)

1. **Decode the `.db` ZoneSystem block.** The world `.db` is a ZPackage. The `ZoneSystem` section
   stores `m_locationInstances` — each a `(prefabNameHash, Vector3 position, ...)`. Decode to real
   `(locationName, worldX, worldZ)` triples. Prefab name hashes resolve via `GetStableHashCode`
   (already in the port) against a known location-prefab name list (from the decomp /
   `ZoneSystem.m_locations`).
   - ⚠️ The regex string-scan used for feasibility is **suggestive, not authoritative** — a real
     decoder reading the ZPackage structure is required before trusting counts/positions.
   - Format is `worldVersion`-dependent; pin against the actual save version (Niflheim = 200/37).
2. **Assign each location to a region.** For each decoded `(x, z)`, call the region lookup
   (`RegionLookupService` / the `regionIdGrid`) to get its `regionKey`. Pure point-in-region.
3. **Aggregate per region** → `locationCounts: {Crypt: 4, Runestone: 2, ...}`, `hasBoss`/`bossType`,
   `traderPresent`. Emit as `{seed}_locations.json` keyed by `regionKey`, `source: real-db`.

## Design notes

- **Input is a specific world save**, not the seed alone — locations come from the `.db`, so the
  sidecar is per-save. (The core gazetteer is per-seed/deterministic; locations need the generated
  world. Usually the same thing, but worth being precise: a `.db` from a different game version or a
  modded location set will differ.)
- **Naming corpus bonus.** Location presence is strong naming signal — "the region with the Tar Pits",
  "Bonemass's marsh", "Haldor's Crossing". Once built, feed `locationCounts`/`hasBoss` into the naming
  bench traits (`region-naming.md`) for landmark-derived names.
- **Keep it a sidecar.** Even though it's real, it stays out of the core (computed) table — it's a
  different source (save file vs port) with a different version-dependency. Consumers join on
  `regionKey`.

## Effort

Moderate. The ZPackage/ZoneSystem decode is the real work (binary format, version-sensitive); the
region assignment + aggregation is trivial given the existing lookup. No client needed — the `.db` is
on disk. Feasibility is proven; fidelity needs the proper decoder.
