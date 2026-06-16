# WorldZones — Vision (territorial substrate)

> **Status: PROVISIONAL — molten. Nothing here is decided on your behalf.**
> This captures the *why* and the long-horizon ambition so it isn't only in our heads.
> It is deliberately separate from the [roadmap](../ROADMAP-2026-06.md) (what to actually
> do next) and from the [knowledge base](../knowledge/map.md) (what's true in the code
> today). Cross-repo / consumer-specific integration lives in
> [`partner-integrators/`](../partner-integrators/) — this doc stays substrate-side and
> consumer-agnostic. Authored 2026-06-15; revisit and lock piece by piece.

---

## The one-line thesis

WorldZones turns a Valheim world into **named, bounded, queryable regions** — a *substrate*
other mods and players hang meaning on. It does not author gameplay meaning itself; it
provides the structure that meaning attaches to.

## The territorial pillar (provisional)

The motivating use case — *a* consumer of the substrate, not the substrate's own job:

- **regions = places** — bounded, named, persistent areas of the map.
- **claims** — a marker (e.g. a Guardian-Stone-like object) that binds ownership to a region.
- **guilds = ownership** — who holds a place.
- **the local/regional map = what holding land grants** — territory you control becomes
  legible to you.

This is the picture that makes "regions" worth having. But note the **layering discipline**:
everything above is *consumer meaning*. WorldZones supplies regions; whether a region is a
"claim" or a "guild holding" is the consumer's call. Keeping that wall clean is what lets the
substrate stay reusable.

## The ambition ladder

How far the substrate reaches into worldgen. Each rung is independently shippable:

| Rung | What it does | Cost / safety | Status |
|---|---|---|---|
| **L1 Overlay** | read vanilla terrain → derive regions | have it (~80% of the engine) | today |
| **L2 Steer** | score/select vanilla seeds for a good progression story | cheap, network-safe; needs an IR | future |
| **L3 Replace** | author terrain to satisfy region intent | post-launch moonshot | leaning, provisional |

> The lean (yours, provisional, "a bit both, leaning second/third") sits between L2 and L3.
> Not locked — recorded so it isn't lost.

## The IR / lift–lower idea (provisional, with a known caveat)

A serializable **Region-Intent model** both directions could speak:
- **`lift`** — analysis: terrain → region intent (what L1 does today, implicitly).
- **`lower`** — synthesis: region intent → terrain (what L3 would need).

⚠️ **Honesty flag (don't oversell the symmetry):** `lift` and `lower` are **not true
inverses**. Analysis is many-to-one; synthesis is one-to-many. The honest abstraction is
`lift` + a **comparison metric** (which is what L2 actually needs); `lower` is a separate,
unsolved synthesis problem. Treat them as two problems that happen to share a vocabulary,
not as a clean round-trip.

## Progression tiers / land value (provisional)

Score regions for progression value, staging, buildability, wildness. Let
territory/social/wild candidates **emerge** from combinations of those signals rather than
hard-coding categories. **Surface signals; let playtest produce balance** — don't make the
algorithm decide the meta.

## Why this is filed separately (the discipline)

- The **roadmap** is for what to do before launch — and the honest launch cut **drops all of
  the above** (L2, the IR, territorial integration are explicitly post-September). Mixing
  molten vision into a launch checklist makes the checklist lie about scope.
- The **knowledge base** is grounded against real code; vision is not yet code.
- **Consumer-specific** integration (how Trailborne's map system would bind regions, the
  provider model, the two-repos persistence mismatch) belongs in
  [`partner-integrators/trailborne.md`](../partner-integrators/trailborne.md), not here —
  the substrate vision must stay consumer-agnostic to stay reusable.

## What's load-bearing for the substrate (the part the review flagged)

The vision attaches meaning to region **extent** ("the guild owns the land *around the
lake*"). But the current contract only guarantees **`RegionId` stability**, and *extent* is
listed as freely swappable. **Stable integer ≠ stable territory.** The moment a consumer keys
a claim off extent, the "swap borders later" door closes unless there's a
border-moved-under-a-live-claim migration story — which doesn't exist yet. This is the one
place the vision and the contract are quietly in tension; see
[`contract-and-invariants.md`](../knowledge/contract-and-invariants.md). Resolving it is
substrate work, not consumer work, and it gates anything territorial becoming real.
