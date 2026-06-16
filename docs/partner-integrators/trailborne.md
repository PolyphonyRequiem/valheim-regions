# Partner Integrator — Trailborne (SBPR)

> **Audience:** anyone wiring WorldZones regions into Trailborne's map system (and the
> mirror-image note for the Trailborne side). **Status: integration is UNSPECIFIED —
> deferred, not designed.** This doc records the considerations and the known hazards so the
> integration starts from honest constraints, not a hand-wave. Substrate-agnostic vision
> lives in [`../vision/territorial-substrate.md`](../vision/territorial-substrate.md); this
> doc is the consumer-specific seam.

---

## The shape of the integration

Trailborne already has a **map system** (local maps: bind a provider, render through a
viewer). The idea: a **"regional map" is another *provider type*** in that system — same
binding, same viewer, **different data source**. WorldZones supplies the region
tessellation + names; Trailborne renders it through machinery it already owns.

That's the appeal: no new viewer, no new UI — a new provider behind an existing seam.

## 🔴 The hazard that makes this non-trivial — two different persistence models

This is the review's headline flag, and it's the thing to design **first**, because the two
repos persist world-knowledge differently:

| | Trailborne local maps | WorldZones regions |
|---|---|---|
| **What it is** | a per-artifact survey | a global, deterministic tessellation |
| **Persistence** | `m_customData` on the item artifact (local-only, carried, **not networked** as map state) | global / **computed** from world seed (every client derives the same thing) |
| **Identity** | the map item instance | `RegionId` (today: secretly the seed's array index — see below) |
| **Authority** | the holder's artifact | the world's seed |

A Trailborne map is **a thing you carry**; a WorldZones region is **a fact about the world
everyone computes identically**. Binding them means deciding: does a "regional map" carry a
*snapshot* of region data (artifact-style, local), or does it *recompute* regions live
(global, seed-derived)? Those have different networking, different staleness, different save
behavior. **This interface must be written down before the integration is real** — it is the
one-paragraph spec that doesn't exist yet.

## 🔴 The identity leak the consumer must not depend on

`ProtoRegionGenerator.cs` currently sets `regionIdGrid[seed] = i` — **a region's ID is its
seed's index in the seeds list.** Any change to seed count or order silently renumbers every
region. **Do not let a Trailborne map (or a claim, or a guild holding) persist a raw
`RegionId` until this is decoupled** — a persisted map could point at the wrong land after
any border-algorithm change. This is substrate work (tracked in
[`../knowledge/contract-and-invariants.md`](../knowledge/contract-and-invariants.md)), but
it's a hard precondition for *any* persistent consumer binding, so it's called out here too.

## 🔴 Stable ID ≠ stable territory

The contract guarantees `RegionId` is stable. It does **not** guarantee region **extent** is
stable — extent is listed as "freely swappable." But a territorial consumer cares about
*where the border is* ("the guild owns the land around the lake"). If Trailborne keys
anything off extent, the "swap borders later" door closes unless there's a
border-moved-under-a-live-claim migration story. There isn't one yet. **Consumer claims on
extent need a migration contract from the substrate first.**

## What's safe to lean on today (the stable seam)

- **Determinism across clients** is sound (no unseeded RNG; order-dependent steps sorted
  with deterministic tie-breaks). Every client computes the same tessellation from the same
  seed — so a "recompute live" provider is viable *without* networking the region data.
- **`RegionId` stability** holds *as long as seeds don't change* — fine for read-only
  display (show the region name on the minimap), **not** fine for persisted claims until the
  identity decoupling lands.
- **Region name + extent for the current world** are queryable client-side at world load.

## Recommended integration order (when this becomes real — post-launch)

1. **Substrate first:** decouple `RegionId` from seed index; define an extent-stability /
   migration contract. (Both are WorldZones-side; neither is done.)
2. **Write the provider interface:** one document answering snapshot-vs-recompute,
   networked-vs-local, and what identity a bound regional map persists.
3. **Read-only provider first:** a regional map that *displays* region name/extent by live
   recompute (no persistence) — ships value, dodges every identity hazard above.
4. **Only then** persistent/territorial bindings (claims, guild holdings), once the
   substrate guarantees they can rest on.

## Cross-references

- Substrate vision (consumer-agnostic): [`../vision/territorial-substrate.md`](../vision/territorial-substrate.md)
- Contract & invariants (what's stable vs swappable): [`../knowledge/contract-and-invariants.md`](../knowledge/contract-and-invariants.md)
- Region pipeline internals: [`../knowledge/regions.md`](../knowledge/regions.md)
