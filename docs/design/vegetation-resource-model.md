# Vegetation & resource model (ore-node counts) — buildability verdict

> **Status:** Provisional / SCAFFOLD landed 2026-06-23. The headless skeleton
> (`src/WorldZones.WorldGen/VegetationModel.cs`) is real and tested; a numerically faithful
> ore-count sidecar is **blocked on data that does not exist on a headless box.** This doc records
> *exactly* what is portable, what is blocked, and why — so nobody re-derives the wall or, worse,
> fabricates configs to paper over it. Daniel's call to lock the approach.

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

1. **Extract the config catalogue** from a Valheim **client** install (AssetRipper / a BepInEx dump of
   `ZNetScene.m_prefabs` → each `ZoneVegetation`). One-time, client-gated, produces a JSON catalogue
   checked into `data/` (or kept external). This is the single thing standing between "scaffold" and
   "estimate."
2. **Quantify the over-count bias** once a client is available: walk a few zones, compare real node
   counts to `ModelZone` output, publish the correction factor per resource. The bias is *systematic*
   (we drop only rejections), so a per-prefab scalar may recover usable accuracy.
3. **(optional)** Port `GetForestFactor` if forest-gated flora matters to the sidecar.
4. **Wire into the gazetteer**: a `--vegetation <catalogue.json>` flag aggregating `ModelZone` over
   each region's zones, emitted as a **separate sidecar** keyed by `regionKey`, every field
   `source: modeled`. Never folded into the core (computed) gazetteer table.

## Why this is the right cut

The core gazetteer is 100% measured truth. Bolting on fabricated ore numbers would poison that
credibility for a substrate other mods build on. The scaffold lets the work resume the instant a
client-side extraction exists, with the honesty rails (modeled tag, over-count caveat, empty-default)
already in place.
