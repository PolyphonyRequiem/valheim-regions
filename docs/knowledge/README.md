# docs/knowledge — WorldZones Knowledge Base

A curated, grounded knowledge map of the WorldZones codebase, plus a mechanical index for
~0-token navigation. Built so an engineer (human or agent) can reason about the worldgen and
region systems with domain expertise without re-reading 7k LOC + a 142k-line decomp each time.

## Read order

1. **`map.md`** — entry point. What WorldZones is, where everything lives, the navigation cheatsheet. **Start here.**
2. **`worldgen.md`** — Valheim's worldgen as ported (bit-exact height + biome pipeline, decomp line correspondence).
3. **`regions.md`** — the region pipeline (classify → seed → BFS borders → merge → inland water) + the terrain-blind-borders finding.
4. **`contract-and-invariants.md`** — the keystone: invariants (don't break), swap-points (safe to change), the consumer contract, the ambition ladder.

## Mechanical index (the token-cheap layer)

- `index/` — generated: `members.tsv`, `by_type/<Type>.txt`, `methods.json` (same schema as the SBPR decomp-index).
- Query with `../../tools/knowledge/wzq.py` (port) — and `wzq.py decomp <name>` pivots to the Valheim original.
- Regenerate after code changes: `python3 ../../tools/knowledge/build_index.py`

## Maintenance discipline (important)

**A stale knowledge map is worse than none.** When you change code:
1. Update the prose doc that describes it (in the same commit).
2. Re-run `build_index.py` so symbol lookups stay accurate.
3. If you change a *contract* surface or an *invariant*, update `contract-and-invariants.md`
   explicitly — that doc is load-bearing for "what's safe to change."

The docs are grounded against `main` HEAD as of 2026-06-15. Each cites real symbols/lines;
verify with `wzq.py` rather than trusting prose that may have drifted.
