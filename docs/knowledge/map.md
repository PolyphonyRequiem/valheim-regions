# WorldZones Knowledge Map

> **Entry point. Load this first.** It tells you what WorldZones is, where each piece
> lives, and how to navigate both the port and the Valheim original at ~0 tokens.
> Authored 2026-06-15 against `main` HEAD (`4fca9eb`). Keep it current: when code
> changes, the doc that describes it changes in the same commit.

## What WorldZones is (one breath)

A **foundational library mod** that deterministically divides a Valheim world into
**named, bounded, queryable regions** other mods (and players) can reason about. It is a
*substrate* — SBPR/Trailborne and others consume it. It does not author gameplay meaning;
it provides the structure consumers hang meaning on.

## The two systems

| System | Project | Role | Deep doc |
|---|---|---|---|
| **WorldGen** | `src/WorldZones.WorldGen` (4.4k LOC) | Pure-C# **port of Valheim's worldgen** — seed → height → biome → rivers. Bit-exact, Unity-free at runtime. | [`worldgen.md`](worldgen.md) |
| **Regions** | `src/WorldZones.Regions` (2.6k LOC) | The **region pipeline** — classify zones → components → seed → BFS borders → merge → inland-water. | [`regions.md`](regions.md) |
| **Contract** | interfaces + invariants | What stays true across any algorithm swap. The **start-over insurance**. | [`contract-and-invariants.md`](contract-and-invariants.md) |
| **Mod** | `src/WorldZones.Mod.RegionOverlay` | Thin BepInEx layer: computes the tessellation client-side, shows region name on minimap. **Client-only — needs Valheim assemblies to build.** | (see regions.md §consumer) |

## The pipeline (the spine — locked 2026-06-15)

```
[pre-gen: declare region intent]   ← optional, future   (zones → world / "lower")
[worldgen guided by intent]        ← optional, future   (Level 3)
[vanilla worldgen]                 ← baseline today
[post-gen: derive regions]         ← ALWAYS RUNS  ← THE MVP  (world → zones / "lift")
[consumer layers meaning]          ← modder's job, not the library's
```
**Post-gen is the only stage that always runs** (even authored worlds must be re-read), so
it is the correct MVP — and it is ~80% built already (this is WorldZones today). The known
weak point is the *quality* of one post-gen step: **borders are terrain-blind** (see
regions.md §borders).

## How to navigate at ~0 tokens — the mechanical index

Two sibling indexes, same schema:
- **WorldZones port:** `tools/knowledge/wzq.py` over `docs/knowledge/index/`
- **Valheim decomp (142k lines):** `/home/polyphonyrequiem/valheim/sbpr-corpus/scripts/q.py`

```bash
# in the repo root:
python3 tools/knowledge/wzq.py method GetBaseHeight   # find it in the PORT
python3 tools/knowledge/wzq.py type ProtoRegionGenerator
python3 tools/knowledge/wzq.py xref GetBaseHeight     # call sites in src/
python3 tools/knowledge/wzq.py decomp GetBaseHeight   # pivot to the Valheim ORIGINAL
```
Rebuild the port index after code changes: `python3 tools/knowledge/build_index.py`

> ⚠️ **Path trap:** this Hermes profile's `$HOME` is the profile sandbox
> (`~/.hermes/profiles/starbright-engineering/home`). The git repo is under `$HOME/repos`.
> The Valheim decomp + index tooling live under the **real** home
> `/home/polyphonyrequiem/valheim/`. Anchor to `$HOME` for the repo; use the absolute
> `/home/polyphonyrequiem/valheim/...` for decomp.

## "I want to understand X" → go here

| Question | Read / run |
|---|---|
| How does Valheim place biomes? | `worldgen.md` §biome + `wzq.py method GetBiome` |
| How is terrain height computed? | `worldgen.md` §height + `wzq.py method GetBaseHeight` |
| Is the port actually faithful? | `worldgen.md` §correspondence (decomp line map) |
| How do regions form / why are borders "off"? | `regions.md` §pipeline, §borders |
| What can I NOT break? | `contract-and-invariants.md` |
| Where's the swappable seam for better borders? | `contract-and-invariants.md` §swap-points |
| What does a consumer mod see? | `regions.md` §consumer-surface |

## Status (2026-06-15)

- ✅ Builds clean (net472 + net8.0 multitarget added for Linux test runs).
- ✅ **102/102 tests pass** (WorldGen 34, Regions 68).
- ⚠️ Borders are the v0 placeholder (terrain-blind BFS) — the one unfinished foundation.
- ⚠️ Empirical (PNG-vs-real-map) validation was deleted in `aea19e0`; only structural
  validation remains. Gap to close before ship.
