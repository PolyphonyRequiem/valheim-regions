# Ground-truth biome validation fixture

This directory holds an **independent oracle** for validating that `WorldZones.WorldGen`'s
biome reconstruction matches real Valheim worldgen.

## What this is

`biome_oracle_HHcLC5acQt.bin.gz` — a gzipped, subsampled biome map for seed **`HHcLC5acQt`**,
decoded from a [valheim-map.world](http://valheim-map.world/) "All Data" export
(generator v9.1, **Valheim 0.221.4**, Unity 2019.4.40f1).

It is **not** rendered by our own code. valheim-map.world runs an independent reconstruction of
Valheim worldgen, so agreement between it and our port is genuine cross-validation, not a tautology.

## Format

Decompress, then read little-endian:

```
int32   recordCount
recordCount × {
    float32  worldX      // Valheim world meters, +X = east
    float32  worldZ      // +Z = north
    uint16   biome       // Heightmap.Biome flag value (see BiomeType)
}
```

Biome values: `Meadows=1, Swamp=2, Mountain=4, BlackForest=8, Plains=16, AshLands=32,
DeepNorth=64, Ocean=256, Mistlands=512`.

Subsampled at every 96th source sample (~549 m grid) → 30,976 points, ~74 KB gzipped.
That density is statistically ample for a regression gate while keeping the repo lean.

## How it was produced

`tools/validation/decode_oracle.py` decodes the raw 256-tile export bundle into this fixture.
The full export (~1.5 GB of `.bin.gz` tiles) is **deliberately not committed** — it lives only in
the original download. The decoder + this subsample are the reproducible, lean artifacts.

## Provenance note

The bundle's own `map.json` and `Readme.txt` (signed "-wd40bomber7") assert the origin and the
Valheim/generator versions. The 256 binary tiles carry the exact `MapSample` struct described in
that readme, and the lossless biome enum (not a lossy color PNG) is what we decode here.
