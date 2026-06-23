# Ground-truth biome validation

> **Status:** Restored 2026-06. The empirical correctness guard that commit `aea19e0`
> deleted ("ground truth tests dependent on external seed data") is back — rebuilt on the
> lossless tile data instead of the lossy PNG that made the original unreliable.

## Why this exists

The 102 structural tests prove the pipeline is **self-consistent** (hashes match, scenarios
hold). None of them prove the computed map matches the **real Valheim world**. "Bit-exact" was a
historical claim (`7b34f19`) with no live regression. That's a silent-failure risk, and it gets
worse at Valheim 1.0 / Deep North, which churns the worldgen assemblies: if our port drifts, every
client is wrong *identically* (determinism still holds), and nothing catches it. This test is that
catch.

## The oracle is independent (the load-bearing property)

The fixture is decoded from a [valheim-map.world](http://valheim-map.world/) "All Data" export for
seed `HHcLC5acQt` (generator v9.1, **Valheim 0.221.4**). That site runs its **own** reconstruction
of Valheim worldgen — so agreement between it and our port is genuine cross-validation, not a
tautology. Provenance is asserted by the bundle's own `map.json` + `Readme.txt` (signed
"-wd40bomber7"), corroborated by 256 binary tiles carrying the exact `MapSample` struct.

We decode the **lossless biome enum** from those tiles — NOT the lossy color PNG the original
deleted test parsed with fuzzy color-matching (which mis-mapped Ashlands/DeepNorth and was a big
source of its bogus numbers).

## What the comparison actually showed (2026-06)

A full 8-way dihedral sweep (every flip/transpose of the X/Z plane) against the port:

| transform | match |
|---|---|
| **identity** | **99.85%** |
| next-best (flipX) | 64.7% |
| all others | 20–38% |

Identity winning by 35+ points **proves the tile→world georeferencing is correct** (not
accidentally aligned) and that the port is essentially bit-exact against an independent source.

### The old "16% match" was a harness bug, not a port bug
The original PNG test hardcoded `WORLD_SIZE = 24576`; the export's own `map.json` says **24000**.
That + lossy color-guessing produced the low number that made it look like the engine was broken.
The engine was sound the whole time — the *validation* had rotted.

## Why the threshold is 99%, not 100% (measured)

A biome map is a **categorical** function with razor-thin boundaries. The oracle stores biomes on
ITS grid (~5.86 m); we evaluate at the EXACT query coordinate. At a biome edge the two grids land on
opposite sides of the line by ~1 m — both correct, sampled differently. Measured:

- **100% of mismatches resolve within 2 m** (the port reproduces the oracle's biome a hair away).
- 99% resolve within **1 m**. Zero survive 2 m. Zero blob/systematic confusion.
- Mismatches split 50/50 coastline (sea-level height threshold) vs inland seam (noise threshold).

So the test asserts **two** things:
1. **overall match ≥ 99%** — the headline correctness gate;
2. **≤5 mismatches survive a 2 m nudge** — the sharp drift detector. A real regression (1.0 shifting
   worldgen) produces SOLID disagreement a 2 m move can't explain; sampling-seam noise always can.
   This clause is what actually catches drift; clause 1 is the headline.

Chasing 100% would be chasing noise — it's *impossible* for two different sampling grids over a
categorical field unless they share sample points.

## Files

- `tests/WorldZones.WorldGen.Tests/GroundTruthBiomeValidationTests.cs` — the test.
- `tests/WorldZones.WorldGen.Tests/fixtures/biome_oracle_HHcLC5acQt.bin.gz` — 30,976-point
  subsample (~74 KB gz). Format documented in `fixtures/README.md`.
- `tools/validation/decode_oracle.py` — regenerates the fixture from a raw export bundle. The full
  ~1.5 GB export is intentionally NOT committed; the decoder + subsample are the lean artifacts.

## Re-baselining at Valheim 1.0

Deep North will change worldgen. When it lands: pull a fresh valheim-map.world export for the new
version, re-run `decode_oracle.py`, and **expect this test to go red first** — that red is the
signal telling you exactly where the port needs to follow 1.0. Update the port, then re-green.
