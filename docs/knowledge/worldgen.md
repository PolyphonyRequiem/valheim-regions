# WorldGen — Valheim's worldgen, as ported

> How `WorldZones.WorldGen` reproduces Valheim's terrain generation. This is a **port**:
> the goal is *bit-exact* output against the game so region algorithms can be validated
> offline. Every claim here is grounded against the decomp
> (`/home/polyphonyrequiem/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`).
> Pivot to any original with `wzq.py decomp <name>`.

## The contract

`WorldGenerator(seed)` → deterministic `GetBiome(x,z)` and `GetHeight(x,z)` for any world
coordinate. Same seed ⇒ identical output, on every client, forever. **Determinism is
load-bearing** — Valheim computes terrain independently per client and they must agree.

## Coordinate system & constants (port `WorldGenerator.cs`)

| Constant | Value | Meaning |
|---|---|---|
| `WorldRadius` | `10000f` | Playable radius (m). Biome rings are defined in this space. |
| `WorldEdgeRadius` | `10500f` | Hard world edge; terrain collapses to −2 beyond. |
| `ashlandsMinDistance` | `12000f` | Ashlands gate (south, via Y-offset). |
| `ashlandsYOffset` | `-4000f` | Shifts the Ashlands test south. |
| zone size (Regions) | `64` m | `ZoneGrid.ZoneSize` — matches Valheim `ZoneSystem.c_ZoneSize`. |

⚠️ **Coordinate-mapping is the historically dangerous spot.** A 2026-02 note recorded a
ground-truth test failing at 15.6% match because it used worldSize 21000 vs the real
24576 (3.0 m/px). That test was later *deleted* (`aea19e0`), not fixed in place — see
`contract-and-invariants.md` §validation-gap. The generator itself is believed correct
(structural tests pass); the *empirical* check is what went away.

## Height pipeline — `GetBaseHeight(x,z)` (port L269 ↔ decomp L130307)

Multi-octave Perlin, faithfully ported with decomp line refs inline in the source:

1. **Offset** coords by `100000 + offsetN` (decorrelates octaves; offsets are seed-derived).
2. **Octave 1** (broad): `Perlin(·×0.001) · Perlin(·×0.0015)`.
3. **Octave 2** (medium): amplifies existing height by `·×0.9`.
4. **Octave 3** (fine): detail by `·×0.5`.
5. **Baseline**: subtract `0.07`.
6. **Rivers**: two offset Perlin samples → `LerpStep(0.02,0.12,|Δ|)`, gated by
   `SmoothStep(744,1000,distance)` → carves river valleys into height.
7. **World edge**: beyond 10000m lerp toward −0.2, beyond 10490m toward −2.
8. **Mountain suppression** near center: keeps spawn area from spiking into mountains.

`GetHeight(x,z)` = `GetBiomeHeight(GetBiome(x,z), x,z)` — i.e. height is **biome-specific**;
each biome has its own height function (`GetMeadowsHeight`, `GetForestHeight`,
`GetPlainsHeight`, `GetSnowMountainHeight`, …) layering biome character + `AddRivers` on
top of base height.

## Biome placement — `GetBiome(x,z)` (port L153 ↔ decomp L130242)

**Distance-ring + Perlin-threshold model.** Evaluated in this exact priority order (order
matters — first match wins):

1. `waterAlwaysOcean && GetHeight ≤ oceanLevel` → **Ocean**
2. `IsAshlands` → **AshLands** (DLC; `Length(x, y−4000) > 12000 + angleVar`)
3. `baseHeight ≤ oceanLevel (0.02)` → **Ocean**
4. `IsDeepnorth` → **Mountain** if `baseHeight>0.4` else **DeepNorth**
5. `baseHeight > 0.4` → **Mountain** (high elevation anywhere)
6. Swamp: `Perlin(offset0·0.001) > 0.6 && 2000<dist<maxMarsh && 0.05<base<0.25` → **Swamp**
7. Mistlands: `Perlin(offset4·0.001) > minDarkland && 6000+angle<dist<10000` → **Mistlands**
8. Plains: `Perlin(offset1·0.001) > 0.4 && 3000+angle<dist<8000` → **Plains**
9. Black Forest: `Perlin(offset2·0.001) > 0.4 && 600+angle<dist<6000` → **BlackForest**
10. `dist > 5000+angle` → **BlackForest** (far fallback)
11. else → **Meadows** (safe spawn)

**`WorldAngle(x,z) = sin(atan2(x,z) · 20)`** → the `×100` angle variation is what makes
biome rings wavy instead of perfect circles. This is why biomes "wobble" radially.

### Why this matters for Regions
Biomes are a **function of distance-from-center + Perlin noise**, not of region structure.
So progression tier *correlates with radius* (Meadows center → Ashlands/DeepNorth rim) but
is **noisy** — a Swamp can punch into Black-Forest distance. Any "region tier" model
(see regions.md) must read the *actual* per-zone biome, not assume clean concentric bands.

## Noise primitives (the bit-exactness foundation)

- `PerlinNoise.cs` / `FastNoise.cs` — hand-spun to match **Unity's** `Mathf.PerlinNoise`
  exactly (Valheim's output depends on Unity's specific implementation). Verified by
  `7b34f19 "Achieve bit-exact WorldGenerator match against Valheim assembly"`.
- `UnityRandom.cs` — reproduces Unity's RNG for seed→offset derivation.
- `MathUtils.cs` — `Lerp/LerpStep/SmoothStep/Clamp01/Length` matching Unity `Mathf`.
- **Future direction** (constitution): replace UnityEngine.CoreModule build-dep with these
  hand-spun primitives entirely → removes the only non-portable dependency. Out of scope now.

## Rivers — the thing that ISN'T in regions

`River.cs` (`p0/p1/center/widthMin/widthMax/curveWidth/curveWavelength`) + `RiverPoint`.
Rivers are generated and folded into **height** (`AddRivers`, called from every biome
height fn). **They do not influence region boundaries** — that disconnect is the core
finding in regions.md §borders. Rivers carve terrain; borders ignore terrain.

## Quick index

```bash
wzq.py type WorldGenerator        # all members
wzq.py method GetBiome            # the biome fn (2 overloads)
wzq.py method GetBaseHeight       # the height core
wzq.py decomp GetBiome           # → Valheim original (assembly_valheim L130242)
wzq.py decomp GetBiomeHeight     # → L130399
```
