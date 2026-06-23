# Region gazetteer dataset — schema & architecture

> **Status:** Core shipped 2026-06-23 (PR #8, `Gazetteer.cs`). This doc specifies the dataset, the
> three-source architecture, and the measured-vs-modeled discipline that keeps it trustworthy as a
> substrate. The naming layer it carries is **provisional** (see `region-naming.md`).

## What it is

A **gazetteer** — a geographic dictionary of a world's regions. One record per region, answering the
four questions every gazetteer answers: *where is it · what's it like · what's there · what's it
called.* Built by running the **production region pipeline** headlessly and aggregating per region.

Command: `WorldZones.Cli gazetteer --seed <seed> [--inland-water] [--output <dir>]`
Emits `{seed}_gazetteer.json` (structured) and `{seed}_gazetteer.tsv` (flat, one row/region).

## The three-source architecture (the load-bearing idea)

"Resource data per region" is **not one source** — it is three, with very different reliability, and
the dataset keeps them in separate layers so the measured core is never contaminated by estimates.

| Source | Reliability | Where from | In core gazetteer? |
|---|---|---|---|
| **Terrain character** (biome, elevation, coast, relief, area, neighbours) | ✅ measured | verified worldgen port (`GetHeight`/`GetBiome`) | **YES** — this is the core |
| **Locations / POIs** (crypts, runestones, bosses, traders) | ✅ real (ground-truth) | the world `.db` (eagerly generated at world-create) | **NO** — sidecar, joins on `regionKey` (see `location-join.md`) |
| **Ore / vegetation node counts** | 🟡 modeled (estimate) | `PlaceVegetation` port + extracted configs | **NO** — sidecar, `source: modeled` (see `vegetation-resource-model.md`) |

**Rule: the core table is measured-only.** Locations and ore join *onto* it as tagged sidecars. A
consumer that wants only trustworthy geography reads the core; one that accepts estimates opts into
the modeled sidecar explicitly. This is what lets other mods build on the substrate without inheriting
our guesses as if they were facts.

## Core record schema (per region)

```jsonc
{
  "regionKey": "r.-7.97",           // DURABLE identity (coord-derived). THE join key.
  "name": "Greater Nordadal",        // deterministic; PROVISIONAL naming layer (see region-naming.md)
  "transientId": 0,                  // internal BFS label — UNSTABLE, never key off it
  "identityCoord": {"x": -7, "z": 97},
  "seedZone": {"x": -7, "z": 97},
  "centroidMeters": {"x": -623.1, "z": 6265.7},
  "boundsZones": {"minX": -23, "minZ": 82, "maxX": 5, "maxZ": 113},
  "areaZones": 435, "landZones": 405, "inlandWaterZones": 30, "areaKm2": 1.78,
  "isCoastal": true,
  "dominantBiome": "Mistlands",
  "biomeComposition": {"Mistlands": 0.335, "Mountain": 0.271, ...},  // fractions, sum≈1
  "elevationMeters": {"min": 13.5, "mean": 76.6, "max": 277.3, "relief": 263.8},
  "highestPeakMeters": {"x": -832, "z": 6848, "height": 277.3},
  "neighborKeys": ["r.-32.115", "r.11.92", "r.7.73"]   // region adjacency graph
}
```

### Query classes the schema serves
- **address / join** → `regionKey` (durable), `name`, `worldId` (provenance), `centroid`, `bounds`
- **geometry / territory** → `areaKm2`, `isCoastal`, `neighborKeys` (the adjacency graph), bounds AABB
- **character / naming corpus** → `dominantBiome`, `biomeComposition`, elevation stats, `highestPeak`
- **what's there** → (sidecars) locations + modeled ore, both on `regionKey`

## Provenance header (non-optional for a substrate)

```jsonc
"provenance": {
  "schemaVersion": 1,
  "seed": "ForTheWort", "worldId": "ForTheWort",
  "inlandWaterAttribution": true,
  "portCommit": "98a7aca",          // WHICH terrain port produced this
  "valueSource": "computed",         // core is always measured
  "zoneSizeMeters": 64, "targetZonesPerRegion": 200,
  "generatedUtc": "..."
}
```

`portCommit` is critical: when Valheim 1.0 / Deep North churns the worldgen assemblies, a consumer
**must** know which terrain a row matches, or it fails the exact silent-divergence way the roadmap
flagged (every client wrong identically). The header makes terrain-version mismatch detectable.

## Identity discipline (cross-ref `region-identity.md`)

- `regionKey` = coordinate-derived, survives seed-list churn (Option B: min absorbed seed coord).
- `transientId` is emitted for debugging but **documented unstable** — it's the BFS list index and
  renumbers on any seed change. Consumers MUST key off `regionKey`. The schema makes the trap explicit
  rather than hiding it.

## Verified

Seed `ForTheWort` (Niflheim): 162 regions, 26 505 land zones. Neighbour graph integrity-checked —
**0 dangling keys, 0 asymmetric edges, unique RegionKeys, every region has land.** Builds + runs
headless on Linux (net8 multitarget), 0 warnings.

## Not yet built (tracked)
- **Location sidecar** — `location-join.md` (decode `.db` ZoneSystem → assign to regions).
- **Vegetation sidecar** — `vegetation-resource-model.md` (modeled, config-extraction-gated).
- **Naming lock + C# port** — `region-naming.md` (currently a Python enrichment bench).
