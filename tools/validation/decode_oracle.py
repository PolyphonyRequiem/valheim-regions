#!/usr/bin/env python3
"""Decode a valheim-map.world "All Data" export into the compact biome oracle fixture.

The export is a directory of 256 gzipped binary tiles (`data/tiles/XX-ZZ.bin.gz`) plus a
`data/map.json` descriptor. Each tile is TileRowCount × TileRowCount samples of a 10-byte
struct (little-endian): uint16 biome @0, float32 height @2, float32 forestFactor @6.

We keep only (worldX, worldZ, biome), subsample to keep the committed fixture small, and
write a flat little-endian blob:  int32 count, then count × (float32 wx, float32 wz, uint16 biome).

Georeferencing (verified empirically — identity transform wins an 8-way dihedral sweep against
the port at 99.85%): the world spans ±WorldWidth/2; within tile (tileX, tileZ) at sample
(tx, tz),  worldX = -W/2 + tileX*TileSize + (tx+0.5)*TileSize/TileRowCount  (same for Z with tz).

Usage:  decode_oracle.py <export_data_dir> [step] [out.bin]
  export_data_dir = the folder containing map.json and tiles/
  step            = subsample stride in samples (default 96 → ~549 m grid, ~31k points)
"""
import gzip, struct, json, sys, os
from collections import Counter

BIOME_NAME = {0: "None", 1: "Meadows", 2: "Swamp", 4: "Mountain", 8: "BlackForest",
              16: "Plains", 32: "AshLands", 64: "DeepNorth", 256: "Ocean", 512: "Mistlands"}


def decode(base, step, out):
    m = json.load(open(os.path.join(base, "map.json")))
    tsc, trc, ts, ww = m["TileSideCount"], m["TileRowCount"], m["TileSize"], m["WorldWidth"]
    half, spacing = ww / 2.0, ts / trc
    records, dist = [], Counter()
    for tile_x in range(tsc):
        for tile_z in range(tsc):
            p = os.path.join(base, "tiles", f"{tile_x:02d}-{tile_z:02d}.bin.gz")
            raw = gzip.decompress(open(p, "rb").read())
            for tx in range(0, trc, step):
                for tz in range(0, trc, step):
                    biome = struct.unpack_from("<H", raw, (tx * trc + tz) * 10)[0]
                    if biome not in BIOME_NAME:
                        continue
                    wx = -half + tile_x * ts + (tx + 0.5) * spacing
                    wz = -half + tile_z * ts + (tz + 0.5) * spacing
                    records.append((wx, wz, biome))
                    dist[biome] += 1
    with open(out, "wb") as f:
        f.write(struct.pack("<i", len(records)))
        for wx, wz, b in records:
            f.write(struct.pack("<ffH", wx, wz, b))
    total = sum(dist.values())
    print(f"wrote {len(records)} records -> {out} ({os.path.getsize(out)} bytes, step={step})")
    for b, n in sorted(dist.items(), key=lambda kv: -kv[1]):
        print(f"  {BIOME_NAME[b]:<12} {n:>6}  {100 * n / total:5.1f}%")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    base = sys.argv[1]
    step = int(sys.argv[2]) if len(sys.argv) > 2 else 96
    out = sys.argv[3] if len(sys.argv) > 3 else "biome_oracle.bin"
    decode(base, step, out)
