# Region map renderer

Renders the gazetteer as a visual map on real terrain — the **eye-validation artifact** for the
region system. Reads the per-zone grid binary the gazetteer emits + the (optionally name-enriched)
gazetteer JSON, and composites hillshade relief → biome tint → per-region lightness fill → white
borders → region name labels.

## Colorblind-safe by design

Regions are distinguished by **lightness + white border outline + text label** — never hue alone.
Biome tints are value-separated. (Daniel is colorblind; red-on-red / red-vs-brown read invisible —
so the map never relies on hue to carry region identity.)

## Usage

```bash
# 1. gazetteer with grid (the CLI emits {seed}_gazetteer_grid.bin automatically)
dotnet run --project src/WorldZones.Cli -f net8.0 -- gazetteer --seed ForTheWort --output /tmp/out --inland-water

# 2. (optional) enrich with multi-schema names so the map is labeled with them
python3 tools/naming/enrich_gazetteer.py /tmp/out/ForTheWort_gazetteer.json

# 3. render
python3 tools/mapview/render_map.py \
    /tmp/out/ForTheWort_gazetteer_grid.bin \
    /tmp/out/ForTheWort_gazetteer_named.json \
    /tmp/out/ForTheWort_map.png
```

If the named JSON is absent, pass the plain `_gazetteer.json` instead — it'll label with the catalogue
names.

## Grid binary format (`WZGR`)

Emitted by `Gazetteer.WriteGrid` (`src/WorldZones.Cli/Gazetteer.cs`). Little-endian:
`"WZGR"` magic, version, `minIndex/size/zoneSize`, an `id→RegionKey` table, then `size*size` records of
`int32 regionId · uint16 biome · uint16 pad · float32 height` (row-major, gy-major).
Region ids in the grid are the **transient** BFS ids; the header maps them to durable `RegionKey`.

## Knobs (top of `render_map.py`)
- `SCALE` — px per zone (5 default).
- `LABEL_MIN_ZONES` — area threshold below which a region is left unlabeled (declutter).

Output PNG is derived; gitignored.

## Ore-density variant (`render_ore_map.py`)

Overlays the MODELED ore sidecar (`{seed}_vegetation.json`, from `gazetteer --vegetation`) on the
region map as graduated markers — **size + inner brightness + printed count** scale with each
region's `resourceTotal`. Colorblind-safe: ore richness is carried by size/lightness/number, never hue.

```bash
python3 tools/mapview/render_ore_map.py \
    {seed}_gazetteer_grid.bin {seed}_gazetteer_named.json {seed}_vegetation.json {seed}_ore_map.png
```
