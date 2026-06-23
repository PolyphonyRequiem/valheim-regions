# gazetteer_query — cross-source region queries

Joins the region dataset's **three sidecars** on `regionKey` and answers questions no single sidecar
can. This is the payoff of the three-source architecture (measured terrain + real-db locations +
modeled vegetation): one query surface over all of them.

| sidecar | from | carries | source |
|---|---|---|---|
| `{seed}_gazetteer.json` | `gazetteer` CLI | name, biome, geometry, neighbours | computed |
| `{seed}_locations.json` | `tools/locations/` (decode `.db`) | bosses, traders, POI/dungeon counts | real-db |
| `{seed}_vegetation.json` | `gazetteer --vegetation` | modeled ore / flora counts | modeled (upper-bias) |

## Usage

```bash
python3 tools/knowledge/gazetteer_query.py <dir> <query> [args]
```

`<dir>` holds the three `{seed}_*.json` sidecars (e.g. a gazetteer `--output` dir into which you've
also built the locations sidecar). Locations/vegetation are optional — the join is LEFT from the
gazetteer, so missing sidecars just yield zeros.

### Queries
- `summary` — dataset-wide totals across all three sources
- `richest [N]` — top N regions by modeled ore
- `boss-ore [N]` — boss regions ranked by ore (the "mine near the altar" list)
- `ore <prefab>` — top regions for a specific ore (e.g. `silvervein`, `rock4_copper`, `MineRock_Tin`)
- `bosses` — every boss → its named region (+ that region's ore)
- `traders` — every trader region, with biome + ore
- `biome <Biome> [N]` — richest-ore regions within a biome (Mountain, BlackForest, Plains…)
- `region <regionKey>` — full joined dossier for one region

### Example (Niflheim, seed ForTheWort)

```
$ gazetteer_query.py /tmp/out boss-ore 3
r.-77.-58  Myrkholt   Mountain  ore=1194 POI=181 ★ Moder ⚒trader
           └ MineRock_Obsidian=918, MineRock_Tin=140, silvervein=66, rock4_copper=54
r.-21.43   Aesirvoll  Plains    ore= 927 POI=153 ★ Yagluth
r.-54.-52  Hrimholt   Plains    ore= 799 POI= 62 ★ Yagluth ⚒trader
```

`Myrkholt` is one fact assembled from all three sources: the **name** (gazetteer), the **boss +
trader** (real `.db`), and the **ore** (modeled vegetation) — a Mountain region that's Moder's seat,
has a trader, and the richest obsidian/silver on the map.

## Building the three sidecars into one dir

```bash
# 1. gazetteer + grid + vegetation
dotnet run --project src/WorldZones.Cli -f net8.0 -- gazetteer --seed ForTheWort \
    --output /tmp/out --inland-water --vegetation data/valheim_vegetation_catalogue.json
# 2. locations (decode the world .db, join to the same grid)
python3 tools/locations/decode_locations.py /path/to/world.db
python3 tools/locations/build_location_sidecar.py ForTheWort niflheim_locations_raw.json \
    /tmp/out/ForTheWort_gazetteer_grid.bin /tmp/out/ForTheWort_locations.json
# 3. query
python3 tools/knowledge/gazetteer_query.py /tmp/out summary
```
