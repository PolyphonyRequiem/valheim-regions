#!/usr/bin/env python3
"""
Region MAP renderer — the named gazetteer, on real terrain. The eye-validation artifact.

Reads {seed}_gazetteer_grid.bin (per-zone regionId+biome+height) + {seed}_gazetteer_named.json
(multi-schema names, centroids) and composites:
  hillshade relief  ->  biome tint  ->  per-region lightness fill  ->  white borders
  ->  seed dots  ->  region name labels at centroids.

COLORBLIND-SAFE (Daniel): regions are distinguished by LIGHTNESS + the white border outline + the
text label, never by hue alone. Biome tints use lightness-separated values; every region is outlined.
"""
import struct, sys, json, math
from collections import defaultdict
from PIL import Image, ImageDraw, ImageFont

SCALE = 5          # px per zone
LABEL_MIN_ZONES = 28   # only label regions big enough to fit text

def load_grid(path):
    with open(path, "rb") as f:
        d = f.read()
    off = 0
    magic = d[off:off+4]; off += 4
    assert magic == b"WZGR", f"bad magic {magic}"
    ver, = struct.unpack_from("<i", d, off); off += 4
    minIndex, size, zoneSize = struct.unpack_from("<iii", d, off); off += 12
    rc, = struct.unpack_from("<i", d, off); off += 4
    id2key = {}
    for _ in range(rc):
        rid, = struct.unpack_from("<i", d, off); off += 4
        klen, = struct.unpack_from("<i", d, off); off += 4
        key = d[off:off+klen].decode("utf-8"); off += klen
        id2key[rid] = key
    region = [[-1]*size for _ in range(size)]
    biome  = [[0]*size for _ in range(size)]
    height = [[0.0]*size for _ in range(size)]
    for gy in range(size):
        for gx in range(size):
            rid, = struct.unpack_from("<i", d, off); off += 4
            b, _pad = struct.unpack_from("<HH", d, off); off += 4
            h, = struct.unpack_from("<f", d, off); off += 4
            region[gy][gx] = rid; biome[gy][gx] = b; height[gy][gx] = h
    return dict(minIndex=minIndex, size=size, zoneSize=zoneSize, id2key=id2key,
                region=region, biome=biome, height=height)

# biome base tints — chosen LIGHTNESS-SEPARATED (value differs even if hue is invisible).
# (name: (r,g,b, approx_lightness))  Ocean/None dark; land ramps light→mid by biome.
BIOME_RGB = {
    0:   (28, 32, 46),     # None
    256: (24, 36, 64),     # Ocean (dark blue, low L)
    1:   (150, 168, 110),  # Meadows (light)
    8:   (70, 96, 74),     # BlackForest (dark green, low-mid L)
    2:   (120, 100, 70),   # Swamp (muddy mid L)
    4:   (220, 222, 228),  # Mountain (near-white, high L)
    16:  (198, 190, 96),   # Plains (light gold)
    32:  (150, 60, 48),    # AshLands (dark red, low-mid L)
    64:  (210, 224, 236),  # DeepNorth (pale, high L)
    512: (96, 96, 110),    # Mistlands (grey, mid L)
}
BIOME_NAME = {0:"None",256:"Ocean",1:"Meadows",8:"BlackForest",2:"Swamp",4:"Mountain",
              16:"Plains",32:"AshLands",64:"DeepNorth",512:"Mistlands"}

def hillshade(H, size, zoneSize):
    # simple Lambert hillshade from height gradient, light from NW
    sh = [[1.0]*size for _ in range(size)]
    lx, ly, lz = -1, -1, 1.2
    ln = math.sqrt(lx*lx+ly*ly+lz*lz); lx/=ln; ly/=ln; lz/=ln
    for y in range(size):
        for x in range(size):
            x0 = H[y][max(0,x-1)]; x1 = H[y][min(size-1,x+1)]
            y0 = H[max(0,y-1)][x]; y1 = H[min(size-1,y+1)][x]
            dx = (x1-x0)/(2*zoneSize); dy = (y1-y0)/(2*zoneSize)
            nx, ny, nz = -dx, -dy, 1.0
            nn = math.sqrt(nx*nx+ny*ny+nz*nz)
            dot = (nx*lx+ny*ly+nz*lz)/nn
            sh[y][x] = max(0.55, min(1.25, 0.75+dot*0.6))
    return sh

def region_lightness(rid):
    # deterministic per-region lightness multiplier in [0.80,1.18] — separates neighbors by VALUE
    if rid < 0: return 1.0
    h = (rid*2654435761) & 0xFFFFFFFF
    return 0.80 + (h % 1000)/1000.0 * 0.38

def font(sz, bold=False):
    for p in (f"/usr/share/fonts/truetype/dejavu/DejaVuSans{'-Bold' if bold else ''}.ttf",
              "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"):
        try: return ImageFont.truetype(p, sz)
        except: pass
    return ImageFont.load_default()

def main():
    grid_path = sys.argv[1] if len(sys.argv) > 1 else "ForTheWort_gazetteer_grid.bin"
    named_path = sys.argv[2] if len(sys.argv) > 2 else "ForTheWort_gazetteer_named.json"
    out_path = sys.argv[3] if len(sys.argv) > 3 else "ForTheWort_map.png"

    g = load_grid(grid_path)
    size, zs, minIndex = g["size"], g["zoneSize"], g["minIndex"]
    R, B, H = g["region"], g["biome"], g["height"]
    named = json.load(open(named_path))
    by_key = {r["regionKey"]: r for r in named["regions"]}
    key2id = {v: k for k, v in g["id2key"].items()}

    sh = hillshade(H, size, zs)
    W = size*SCALE
    topbar = 60
    img = Image.new("RGB", (W, topbar+W+96), (18, 20, 28))
    px = img.load()

    # base raster: biome tint * hillshade * region lightness
    for gy in range(size):
        py0 = topbar + (size-1-gy)*SCALE
        for gx in range(size):
            b = B[gy][gx]; rid = R[gy][gx]
            base = BIOME_RGB.get(b, (255,0,255))
            s = sh[gy][gx]
            lm = region_lightness(rid) if rid >= 0 else 1.0
            r = max(0,min(255,int(base[0]*s*lm)))
            gg= max(0,min(255,int(base[1]*s*lm)))
            bl= max(0,min(255,int(base[2]*s*lm)))
            x0 = gx*SCALE
            for dy in range(SCALE):
                for dx in range(SCALE):
                    px[x0+dx, py0+dy] = (r,gg,bl)

    draw = ImageDraw.Draw(img)
    # white borders: zone edge between different region ids
    for gy in range(size):
        py0 = topbar + (size-1-gy)*SCALE
        for gx in range(size):
            rid = R[gy][gx]
            if rid < 0: continue
            for dx,dy in ((1,0),(0,1),(-1,0),(0,-1)):
                nx,ny = gx+dx, gy+dy
                nid = R[ny][nx] if (0<=nx<size and 0<=ny<size) else -1
                if nid != rid:
                    x0 = gx*SCALE
                    if dx==1:  draw.line([(x0+SCALE-1,py0),(x0+SCALE-1,py0+SCALE-1)], fill=(245,245,250), width=1)
                    if dx==-1: draw.line([(x0,py0),(x0,py0+SCALE-1)], fill=(245,245,250), width=1)
                    if dy==1:  draw.line([(x0,py0),(x0+SCALE-1,py0)], fill=(245,245,250), width=1)
                    if dy==-1: draw.line([(x0,py0+SCALE-1),(x0+SCALE-1,py0+SCALE-1)], fill=(245,245,250), width=1)

    # labels at centroids (only regions big enough)
    fb = font(13, bold=True)
    placed = 0
    for r in sorted(named["regions"], key=lambda x:-x["areaZones"]):
        if r["areaZones"] < LABEL_MIN_ZONES: continue
        cx_m, cz_m = r["centroidMeters"]["x"], r["centroidMeters"]["z"]
        # world meters -> zone grid index -> pixel
        zx = cx_m/zs - minIndex; zy = cz_m/zs - minIndex
        sx = int(zx*SCALE); sy = topbar + int((size-1-zy)*SCALE)
        nm = r["name"]
        tb = draw.textbbox((0,0), nm, font=fb); tw = tb[2]-tb[0]; th=tb[3]-tb[1]
        bx, by = sx-tw//2, sy-th//2
        # dark plate behind text for legibility (value contrast, not hue)
        draw.rectangle([bx-3,by-2,bx+tw+3,by+th+2], fill=(12,12,18))
        draw.text((bx,by), nm, font=fb, fill=(238,240,246))
        placed += 1

    # title bar
    ft = font(26, bold=True); fs = font(15)
    draw.text((14,12), f"NIFLHEIM", font=ft, fill=(240,242,248))
    draw.text((220,20), f"seed ForTheWort  ·  {len(named['regions'])} regions  ·  {placed} labeled  ·  multi-schema names (provisional)",
              font=fs, fill=(180,184,196))
    # legend (colorblind note + biome value key)
    ly = topbar+W+8
    draw.text((14,ly), "Regions distinguished by LIGHTNESS + white border + label (not hue). Hillshade = real terrain relief.",
              font=fs, fill=(176,180,192))
    lx = 14; ly2 = ly+28
    for b in (1,8,2,4,16,32,64,512,256):
        c = BIOME_RGB[b]; nm = BIOME_NAME[b]
        draw.rectangle([lx,ly2,lx+16,ly2+16], fill=c, outline=(200,200,210))
        draw.text((lx+20,ly2+1), nm, font=font(12), fill=(190,194,206))
        lx += 26 + draw.textlength(nm, font=font(12))

    img.save(out_path)
    print(f"saved {out_path}  ({img.size[0]}x{img.size[1]})  labeled {placed}/{len(named['regions'])} regions")

if __name__ == "__main__":
    main()
