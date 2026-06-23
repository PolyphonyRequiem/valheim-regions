# Vegetation & resource model (ore-node counts) — buildability verdict

> **Status:** ✅ BUILT 2026-06-23. Wall 2 (the missing config catalogue) is DOWN — extracted via
> AssetRipper from a Valheim client install (`tools/vegetation/`, 98 configs / 8 ore checked into
> `data/`). The `--vegetation` flag on the gazetteer now emits a real per-region ore/flora sidecar
> (`{seed}_vegetation.json`, Niflheim: 152 regions, 117 with ore, 27,564 modeled ore nodes), fully
> deterministic, every value `source: modeled`. Wall 1 (mesh/physics rejection filters) remains —
> so counts are a documented UPPER-BIAS estimate, not exact node counts. The headless skeleton
> (`src/WorldZones.WorldGen/VegetationModel.cs`) is unchanged; it now receives a real catalogue
> instead of an empty one. Daniel's call to lock the approach.

## What we wanted

A gazetteer **sidecar** answering "how many copper / tin / silver nodes are in region R" (and flora
counts), tagged `source: modeled`, joining the region gazetteer on `regionKey`. This is the
"vegetation stuff" follow-up named when the core gazetteer shipped.

## Ground truth: how Valheim places ore (decomp)

Ore and flora are **not locations** (those are eager, in the `.db`). They are `ZoneSystem.PlaceVegetation`
(`assembly_valheim.decompiled.cs:97032`), placed **lazily** when a player first loads a zone. The
algorithm, per zone, per `ZoneVegetation` config:

1. Seed the RNG **byte-exact**: `InitState(worldSeed + zoneId.x*4271 + zoneId.y*9187 + prefab.GetStableHashCode())`
2. Roll a target count (`m_min..m_max`, or a probability gate if `m_max < 1`)
3. Scatter N group-anchors in a `2*(32 - groupRadius)` m box around the zone centre; each anchor
   spawns `[m_groupSizeMin..m_groupSizeMax]` instances within `m_groupRadius`
4. **Filter** each candidate against a long list of predicates (biome, altitude, distance-from-centre,
   tilt vs ground normal, vegetation mask, ocean depth, terrain delta, forest factor, blocked,
   clear-area), then instantiate.

It is **deterministic** — same world + zone → same nodes, on every client. That determinism is the
*only* reason a headless reproduction is conceivable at all.

## The verdict: three layers, two walls

| Layer | Headless? | Notes |
|---|---|---|
| **RNG seeding + scatter draw-order** | ✅ portable | port has `UnityRandom` (bit-exact) + `GetStableHashCode`. Implemented in `VegetationModel.ModelZone`, RNG draws consumed in game order so a count is *possible*. |
| **Cheap filters** (count roll, altitude band, distance-from-centre, biome) | ✅ portable | need only height+biome, which the verified port HAS. Implemented. |
| **Mesh/physics filters** (vegetation mask, ocean depth, terrain delta, tilt-vs-normal, `IsBlocked`, `GetGroundData`, clear-area) | ❌ **WALL 1** | need the built terrain **mesh + collider world** + physics raycasts. No headless equivalent. A headless reproduction therefore **over-counts** — it cannot reject placements the game rejects here. Documented, bounded bias. |
| **`GetForestFactor`** (`m_inForest`) | 🟡 portable-but-parked | a `WorldGenerator` static the port didn't bring over. Cheap to add when configs make it matter. |
| **The `m_vegetation` CONFIG catalogue** | ❌ **WALL 2** | which prefabs exist, their min/max/group/biome/altitude — lives in Unity **serialized assets** (`ZNetScene`/prefabs), NOT in any DLL and NOT on a headless box. Verified: nothing extractable here. Copper's actual `m_min/m_max/...` is unknown to us. |

**Wall 2 is the hard one.** Without the real config catalogue, any count is structurally honest but
numerically empty/fabricated. We do **not** invent config numbers — that would manufacture a dataset
that looks authoritative and is fiction. So `ModelZone` returns **empty** on an empty catalogue, by
design (`EmptyCatalogue_ProducesNoCounts_TheHonestDefault`).

## What landed now (the honest seam)

`VegetationModel` (in `WorldZones.WorldGen`, tests in `VegetationModelTests`, **41/41 green**):

- `VegetationConfig` — the headless-relevant subset of `ZoneVegetation`, **populated from an external
  extracted catalogue, never hand-authored**. Carries `IsResource` so the sidecar can split ore from flora.
- `ModelZone(worldSeed, zoneX, zoneY, catalogue, height, biomeAt)` — deterministic, RNG-exact scatter
  using the verified port's `GetHeight`/`GetBiome`, cheap filters only, every output tagged modeled.
- Honest defaults: empty/null catalogue → empty result. Biome mismatch → no placements. Determinism
  and zone-keyed variation both tested.

It is **the seam, not the answer**: a future caller that supplies a real catalogue gets a
deterministic (over-biased) estimate; until then it produces nothing rather than lying.

## To make it real (the unblock path)

1. ~~**Extract the config catalogue**~~ ✅ DONE. `tools/vegetation/parse_vegetation.py` parses an
   AssetRipper export of the client's `ZoneSystem.m_vegetation` into `data/valheim_vegetation_catalogue.json`
   (98 configs, 8 ore). See `tools/vegetation/README.md` for the AssetRipper-on-Prime workflow.
2. **Quantify the over-count bias** once a client is available: walk a few zones, compare real node
   counts to `ModelZone` output, publish the correction factor per resource. The bias is *systematic*
   (we drop only rejections), so a per-prefab scalar may recover usable accuracy. ← STILL OPEN (Wall 1).
3. **(optional)** Port `GetForestFactor` if forest-gated flora matters to the sidecar. ← still parked.
4. ~~**Wire into the gazetteer**~~ ✅ DONE. `--vegetation <catalogue.json>` aggregates `ModelZone` over
   each region's zones into `{seed}_vegetation.json`, keyed by `regionKey`, every value `source: modeled`.
   Kept as a SEPARATE sidecar — never folded into the core (computed) gazetteer table.

## Why this is the right cut

The core gazetteer is 100% measured truth. Bolting on fabricated ore numbers would poison that
credibility for a substrate other mods build on. The scaffold lets the work resume the instant a
client-side extraction exists, with the honesty rails (modeled tag, over-count caveat, empty-default)
already in place.
