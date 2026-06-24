# Location decoder, catalogue parser, and offline registration port

This directory has THREE related pieces — keep them straight (they map to the two-phase location model;
see `docs/design/location-port.md`):

| Tool | Layer | Needs | Output |
|---|---|---|---|
| `decode_locations.py` | realized `.db` (Phase 2 oracle) | a world `.db` | `{world}_locations_raw.json` (real placed/registered) |
| `parse_locations.py` | the config catalogue | AssetRipper export (Prime) | `data/valheim_locations_catalogue.json` (118 configs) |
| `WorldZones.Cli locations` | **offline registration port** (Phase 1) | seed + catalogue | computed placements, validated vs the `.db` |
| `build_location_sidecar.py` | region join | raw json + gazetteer grid | `{seed}_locations.json` sidecar |

The **decoder** is the validation ORACLE and the realized-POI source. The **port** is the from-seed
offline computation (no `.db`, no client). See `docs/design/location-port.md` for the verdict
(bit-exact RNG, 79/86 count-exact, ~83% region-scale) and the two open items (quota over-placement §5,
the Mistlands/Ashlands `LocationList` catalogue gap §6).

## Catalogue parser (`parse_locations.py`)

Parses Valheim's `ZoneLocation` configs (the `m_locations` block) out of an AssetRipper export of
`_ZoneSystem.prefab`. Mirrors `tools/vegetation/parse_vegetation.py`. Runs on Prime, where the export
lives (`/tmp/valheim_export/ExportedProject`). Emits the schema consumed by `WorldZones.Cli locations`
and `LocationModel.LocationConfig`.

```bash
# on Prime:
python3 tools/locations/parse_locations.py data/valheim_locations_catalogue.json
#  -> 118 ZoneLocation configs (86 enabled w/ quantity>0)
```

⚠️ **Coverage gap:** `_ZoneSystem.prefab` holds the 118 base configs. ~60 more (Mistlands, Ashlands,
Hildir, mountain caves) live in `Assets/Systems/LocationLists/_LocationList_*.prefab` — SAME YAML shape.
To get full coverage, point the parser at that directory too and concatenate, preserving `m_sortOrder`
(the game sorts LocationLists before registering — order affects global zone-occupancy). Not yet done.

---

# Location decoder + region sidecar (`.db` oracle)

Decodes **real location/POI data** from a Valheim world `.db` and joins it to gazetteer regions.
This is the **real-db source** in the gazetteer's three-source architecture (measured terrain /
real-db locations / modeled vegetation) — ground truth, not estimated.

## What it does

Valheim generates all locations eagerly at world-create and persists them in the `.db`'s
`ZoneSystem` block. `decode_locations.py` is a from-scratch parser for the world save format
(worldVersion 37): it walks the ZDO list, then decodes the `ZoneSystem.SaveASync` location-instance
block (`string prefabName · Vector3 pos · bool placed`). `build_location_sidecar.py` assigns each
location to a region via the gazetteer grid and aggregates per-region counts, boss flags, and trader
presence.

## Usage

```bash
# 1. decode the .db -> raw locations json (real prefab names + positions)
python3 tools/locations/decode_locations.py /path/to/world.db
#    -> world_locations_raw.json   (e.g. 11,312 locations from Niflheim)

# 2. gazetteer grid for the same seed (emits {seed}_gazetteer_grid.bin)
dotnet run --project src/WorldZones.Cli -f net8.0 -- gazetteer --seed ForTheWort --output . --inland-water

# 3. join locations -> regions -> sidecar
python3 tools/locations/build_location_sidecar.py ForTheWort world_locations_raw.json ForTheWort_gazetteer_grid.bin ForTheWort_locations.json
```

## Sidecar schema (`{seed}_locations.json`)

```jsonc
{
  "provenance": {"seed": "...", "source": "real-db", "worldVersion": 37, "schemaVersion": 1},
  "unregionedLocations": 1809,           // on ocean/islets, no region
  "regions": {
    "r.3.-10": {                          // joins gazetteer on regionKey
      "totalPOIs": 157,
      "counts": {"ruin": 60, "runestone": 30, "grave": 16, "boss": 1, ...},
      "hasBoss": true, "bosses": ["Eikthyr"], "traderPresent": false
    }
  }
}
```

## The save format (worldVersion 37), grounded in the decomp

World envelope (`Game.SaveWorld` → `binary.Write`):
```
int32 worldVersion(37) · double netTime
· ZDOMan.SaveAsync:  long sessionID · uint nextUid · int zdoCount · zdoCount × ZDO.Save
· ZoneSystem.SaveASync:  int genZoneCount · genZoneCount×Vector2i · int 0 · int locationVersion
                       · int globalKeyCount · globalKeyCount×string · bool locationsGenerated
                       · int locationCount · locationCount×(string prefab, float x,y,z, bool placed)
· RandEventSystem (21 bytes, ignored)
```
`ZDO.Save` per record (the variable-length part — must parse fully to stay in sync):
```
ushort flags · Vector2s sector(2×int16!) · Vector3 pos · int prefabHash
· [Vector3 rotation if flags&0x1000]
· if flags&0x00FF: [connection] + 7 typed sections (float/vec3/quat/int/long/string/bytes)
  each = ReadNumItems() count, then count × (int key, <value>)
```
Gotchas that bite: **sector is Vector2s (int16), not Vector2i**; section counts use
`ReadNumItems()` (1 byte, or big-endian 2-byte if high bit set), not a plain byte; `byte[]` is
int32-length-prefixed; strings are .NET 7-bit-length-prefixed UTF8. The `.db` is **uncompressed**.

## Validation

- The ZDO walk consumes the whole file (Niflheim: 3,205,466 / 3,205,487 bytes — only the 21-byte
  RandEvent tail remains). A desync blows the offset up immediately, so full consumption = correct.
- Prefab-hash sanity: `GetStableHashCode("Rock_4")`/`"Beech1"` etc. match the dominant hashes in the
  ZDO list at the right counts — proves the hash function + record layout.
- Geographic sanity: Eikthyr (first boss, always Meadows-near-spawn) joins to a Meadows region 668 m
  from origin. ✓
- Generalizes: also decodes Heistan (24 MB, 524,907 ZDOs, 11,354 locations).

Outputs are derived; gitignored.
