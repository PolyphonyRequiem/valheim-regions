# Location port — offline `GenerateLocations` & validation verdict

> **Status:** ✅ BUILT 2026-06-24. A pure-C# offline port of Valheim's `ZoneSystem.GenerateLocations`
> (`src/WorldZones.WorldGen/LocationModel.cs`) computes every location's world position **from the seed
> alone** — no game client, no `.db`, no walk — the same superpower the terrain port has. Validated
> against the real Niflheim world (`.db`, seed `ForTheWort`, worldVersion 37, 11,312 locations).
> RNG is **bit-exact** (proven), count-exact on **79/86** enabled types, and ~**83% of placements land
> within region-scale (≤500m)** of the real one. The catalogue covers the 118 base configs in
> `_ZoneSystem.prefab`; a known +60 live in separate `LocationList` assets (see §6). Daniel's call to
> lock the approach. Tools: CLI `locations` (validation) + `probe` (single-type RNG-stream diff).

## What we wanted

Locations (POIs, dungeons, bosses, structures, runestones, the spawn temple, traders) as a region
substrate signal — "what places are in region R", computable offline like terrain, so the gazetteer
isn't dependent on a player having walked the world into a `.db`. This is the location analogue of the
terrain port: turn a `.db`-decode dependency into a from-seed computation.

## The TWO-PHASE model — registration vs realization (load-bearing)

Locations are NOT fully generated at world-create. There are two distinct phases, and the port targets
the first:

| Phase | When | What it does | Decomp |
|---|---|---|---|
| **1. Registration** | worldgen, ONCE (`LocationsGenerated` gate) | computes candidate **(x,z)** for every instance, stores `m_placed=false` | `GenerateLocations` 97319 → `GenerateLocationsTimeSliced` 97404 |
| **2. Realization** | per-zone, on first load (`!IsZoneGenerated`) | instantiates the prefab, resolves **y (height) + rotation only**, sets `m_placed=true` | `SpawnZone` 96989 → `PlaceLocations` 97674 |

**The port reproduces Phase 1.** Positions are fixed at registration and never move; realization only
decides *when* a registered site becomes a real GameObject and resolves its ground height. Two
consequences:

- **The `.db` `placed` flag is EXPLORATION STATE, not placement success.** Niflheim: `placed = 185 /
  11,312` — only zones a player has loaded are realized. Validation targets the FULL registered set
  (all 11,312), which is what we want.
- **Unique locations COLLAPSE on first realization.** `PlaceLocations` calls `RemoveUnplacedLocations`
  when `m_unique` (decomp 97717): realizing one deletes all other unplaced candidates of that type.
  This is the **merchant**: `Vendor_BlackForest` registers **10** candidate sites (`.db`: registered=10,
  placed=0), but only the first you discover spawns; the other 9 vanish. A consumer must TAG uniques —
  report "N candidate sites, exactly one realizes (exploration-order-dependent)", NOT "10 merchants
  here." In the base catalogue, only `Vendor_BlackForest` is `m_unique` + enabled.

## Ground truth: how registration places a location (decomp 97404–97616)

Per location type, in `OrderByDescending(prioritized)` order, sharing a **global** `m_locationInstances`
zone-occupancy map (one location per 64m zone, across ALL types):

1. Reseed RNG **byte-exact**: `InitState(worldSeed + prefabName.GetStableHashCode())`, where
   `worldSeed = seedName.GetStableHashCode()` (= `World.m_seed`, decomp 95640).
2. Up to 100k (or 200k if prioritized) outer attempts until `placed == quantity`:
   - `GetRandomZone(maxRange)` — 2 int draws + a pure-math <10km rejection. `centerFirst` grows
     `maxRange += 1` per attempt (a spiral out from origin).
   - If the zone is unoccupied and biome-area matches, up to 20 inner tries:
     - `GetRandomPointInZone` — 2 float draws → a point at zone-center **+ random offset**, NOT the
       geographic center (offset bounded ±(32 − exteriorRadius)m).
     - Filter chain: center-distance → biome → altitude (`y − 30`) → forest factor (if `m_inForest`) →
       distance-from-world-center → **terrain delta** (10× `insideUnitCircle`, max−min height over
       `exteriorRadius`) vs `m_maxTerrainDelta` → **proximity** (`HaveLocationInRange` vs already-placed
       same-type/group within `m_minDistanceFromSimilar`).
     - First candidate to pass → `RegisterLocation` (claims its zone) → `placed++`.
3. If `placed < quantity`, log `"Failed to place all X, placed N out of Q"` (decomp 97599) — the quota
   shortfall (see §5).

**The registration loop is PURE MATH — verified zero `Physics.Raycast`/collider/mesh calls in 97404–
97616.** Every gate is `GetBiome`/`GetBiomeArea`/`GetHeight`/`GetForestFactor`/`GetTerrainDelta`, all of
which the verified port provides. This is why locations port cleanly where vegetation hit a mesh wall.

## What landed (the port)

`LocationModel.Generate(worldSeed, gen, catalogue, strategy)` — faithful transcription of the loop:
ordered pipeline, per-type reseed, global zone occupancy, full filter chain, `centerFirst` spiral,
`unique` short-circuit, `HaveLocationInRange` proximity. Drops only the coroutine/time-slicing/logging
(no RNG effect) and the vegetation-mask filters (`m_minimumVegetation` etc. — draw nothing, unused by
all 86 enabled base types). Added to the port: `GetBiomeArea` + `GetForestFactor` (decomp 130191 /
130747), and `UnityRandom.InsideUnitCircle` (see §"insideUnitCircle").

**The catalogue** (118 `ZoneLocation` configs) was extracted from the EXISTING AssetRipper export — no
new install needed; `m_locations` was already in `_ZoneSystem.prefab` from the vegetation work.
`tools/locations/parse_locations.py` parses it (mirrors `parse_vegetation.py`).

## Validation verdict (vs real Niflheim `.db`, 11,312 locations)

| Metric | Result | Meaning |
|---|---|---|
| **count-exact** | **79/86** enabled types | the right *number* of nearly every location type (InfestedTree01 700/700, all Crypts 200/200) |
| **region-scale (≤500m)** | **~83%** | computed location within ~region-radius of the real one — see caveat below |
| same-locale (≤50m) | ~36% | |
| **bit-exact (≤0.5m)** | ~16% | RNG-faithful; capped by terrain precision through tight delta gates |
| RNG faithfulness | **bit-exact** | the stream re-syncs to 0.00m *after* misses — impossible unless draw-count is right |
| spawn temple | **0.00m** | placed at exactly (134.16, 0.97) after fixing a real bug (see below) |

> ⚠️ **The ≤500m number is a DISTANCE PROXY, not a gazetteer-grid join.** Regions average ~1,400m
> radius, so ≤500m strongly implies same-region, but the rigorous test is binning both the computed and
> real location sets through `{seed}_gazetteer_grid.bin` and comparing `RegionKey`. That join is the
> correct substrate metric and is NOT yet run (no `ForTheWort_gazetteer_grid.bin` was staged at
> validation time). Treat ~83% as "very likely same region," to be confirmed by the grid join.

### `insideUnitCircle` — resolved empirically, not guessed

`GetTerrainDelta` calls `Random.insideUnitCircle` 10× per candidate. It is a **native C++ binding**
(`GetRandomUnitCircle`) — its exact draw pattern is NOT in Unity's public C# reference, so we did not
guess. `UnityRandom.InsideUnitCircle` is a **swappable strategy** (`InsideUnitCircleStrategy`: polar-
radius-first, polar-angle-first, rejection-sampling). The CLI sweeps all three against the `.db`: the
two **polar** forms (2 fixed draws each) score ~16% bit-exact; **rejection-sampling** (variable draws)
scores ~10%. Because a wrong draw-count desyncs the entire downstream stream, the polar forms winning
*is* the proof they consume the right number of draws — corroborated by the probe showing the stream
re-sync to 0.00m after a divergence.

### The spawn-temple bug (canary)

`StartTemple` placed **0/1** — the port couldn't place the spawn temple at all. Root cause: `GetForestFactor`
called `MathUtils.Fbm`, which applies a `*2-1` remap (correct for ITS callers) that centers the value
near 0; the game's `DUtils.Fbm` (assembly_utils 575) is a **raw PerlinNoise sum** centered near ~1.0,
matching `m_forestTresholdMin/Max` (e.g. [1,5]). The temple needs forest-factor ≥1, so the remapped
value (−0.016) made the port reject the real spawn forever. Fixed `GetForestFactor` to the game's exact
raw-sum Fbm → temple now places at **(134.16, 0.97), 0.00m**. One `InForest` type exposed a systematic
function error — exactly what a validation harness is for.

## §5 — Quota shortfalls (not all locations place)

The game itself fails to fill quota for some types (it logs `"Failed to place all..."`). In Niflheim,
**8 of 86 enabled types fell short** (total 138 locations): Grave1 137/200, SwampRuin2 7/30, FireHole
56/75, TrollCave02 188/200, ShipSetting01 92/100, SwampHut3 43/50, SwampRuin1 27/30, Runestone_Mountains
97/100. Cause split: **terrain-delta** (tight `maxTD≤3` gate, hard to satisfy on rough terrain) and
**proximity** (`minDistanceFromSimilar ≥ 200` crowding — too many of the same type already placed
nearby). Both are modeled by the port.

**The honest seam:** where the game falls short, the port places **slightly MORE** (Grave1: port 164 vs
game 137; TrollCave02 191 vs 188). This is NOT a mesh wall (registration is pure math). It is one of:
(a) terrain-precision flipping the `maxTerrainDelta` gate the lenient way (port terrain is 99.85%, not
100% — a sub-meter height error flips a 2–3m-threshold gate), or (b) a proximity-ordering effect, since
which candidates `HaveLocationInRange` rejects depends on where prior placements landed. **Unblock path:**
port the decomp's reject-reason counters (`errorTerrainDelta`, `errorSimilar`, `errorBiome`, `errorAlt`,
…) and compare the port's reject histogram per type to the game's — that localizes exactly which gate
over-admits. This is a *measurement* task, not a blocked one. Tracked as follow-up; does not block the
substrate use (region-scale is unaffected by ±a-few placements per type).

## §6 — Catalogue coverage — ✅ 147/147 COMPLETE (2026-06-24)

Three parser gaps, all closed:
1. **LocationLists** — the +60 post-vanilla locations (Mistlands, Ashlands, Hildir, mountain caves) live
   in `Assets/Systems/LocationLists/_LocationList_*.prefab`; the parser walks them in `m_sortOrder` order
   (matching `SetupLocations`' `Sort(sortOrder)`).
2. **`_GameMain.prefab` + assetID resolution** — the dev/combat locations (`CombatRuin01`,
   `BigRockClearing`, the `Dev*` set) are registered in **`_GameMain.prefab`** (NOT `_ZoneSystem`), with
   an **empty `m_prefabName`** (filled at runtime from `m_prefab`'s SoftReference). Resolved via the
   SoftRef manifest assetID→name map; assetID hex = the four `m_assetID` words (v3,v2,v1,v0) big-endian
   uint32 each (verified vs StoneCircle). `_GameMain` re-embeds the whole ZoneSystem → deduped by assetID,
   or by content key when the assetID is **null** (the all-zero id is shared by many disabled entries and
   is NOT an identity — keying dedup on it pruned 18 real configs before it was caught).
3. **🔴 assetID is AUTHORITATIVE over `m_prefabName` (the BogWitch fix).** The serialized `m_prefabName`
   is a cache that goes STALE — verified: 3 of 260 entries mismatch their assetID's true name. A
   `_LocationList_Ashlands` entry labelled `Hildir_camp` is **really `BogWitch_Camp`** (its `m_prefab`
   SoftReference resolves to BogWitch), and two `_GameMain` `DevHouse4` entries are really
   `DevHouse5`/`DevDressingRoom`. The game resolves `m_prefab.Name` from the SoftReference at runtime, so
   the assetID→manifest name wins; `m_prefabName` is only a fallback when the assetID can't resolve. This
   is what hid BogWitch — it was mislabelled, not absent.

**`BogWitch_Camp` is a registered UNIQUE** (q=10, Swamp, `m_unique`, `m_iconPlaced`) — generated
dynamically when a player first reaches one of its candidate sites, exactly like the vendors (Daniel
called this). It's one of the 7 candidate-group uniques.

Result: **188 configs, 147 enabled, 0 unresolved/mislabelled names, 147/147 `.db` prefab types covered**
(was 86). This was **NOT a version mismatch and NOT a missing bundle** — every location was in the export
all along; the original parser trusted the stale `m_prefabName`, missed `_GameMain`, and didn't resolve
assetID-only entries.

## Why this is the right cut

The terrain port made "what biome/height is at (x,z)" a from-seed computation. This makes "what places
are in region R" the same — registration positions are pure seed-determined math, fully reproducible
offline. The substrate gets a location signal that doesn't require a walked `.db`, with honesty rails:
the two-phase model (registration ≠ realization), the unique-collapse tag, the quota-shortfall seam, and
the catalogue gap all documented rather than papered over. The `.db` decoder (`tools/locations/`) remains
the validation oracle and the realized-POI source; the port is the offline registration computation.

## Reproduce

```bash
# catalogue (on Prime, where the AssetRipper export lives):
python3 tools/locations/parse_locations.py /tmp/loc_cat.json     # 118 configs from _ZoneSystem.prefab

# oracle (decode the real world):
python3 tools/locations/decode_locations.py path/to/niflheim.db  # -> niflheim_locations_raw.json (11,312)

# validate the port (sweeps insideUnitCircle strategies, tiered distance buckets):
dotnet run --project src/WorldZones.Cli -f net8.0 -c Release -- locations \
  --seed ForTheWort --catalogue loc_cat.json --oracle niflheim_locations_raw.json

# probe a single type's RNG stream candidate-by-candidate:
dotnet run --project src/WorldZones.Cli -f net8.0 -c Release -- probe \
  --seed ForTheWort --prefab StartTemple --catalogue loc_st.json \
  --oracle niflheim_locations_raw.json --strategy PolarRadiusFirst
```
