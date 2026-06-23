#!/usr/bin/env python3
"""
Ore-density MAP renderer — the named gazetteer + the MODELED ore sidecar, on real terrain.
The eye-validation artifact for the vegetation/ore dataset (PR #13, Wall 2 down).

Reads {seed}_gazetteer_grid.bin + {seed}_gazetteer_named.json + {seed}_vegetation.json and
composites the standard region map, then overlays per-region ORE markers:
  marker SIZE + inner BRIGHTNESS scale with resourceTotal (graduated circles), count labeled.

COLORBLIND-SAFE (Daniel): ore intensity is carried by marker SIZE + LIGHTNESS + the numeric
label — never by hue. The marker ring is white; the fill is a value ramp (dark→bright), and the
ore count is printed. Red/brown hue differences are never load-bearing.
"""
import struct, sys, json, math
from PIL import Image, ImageDraw, ImageFont

SCALE = 5
LABEL_MIN_ZONES = 28

def load_grid(path):
    with open(path, "rb") as f:
        d = f.read()
    off = 0
    assert d[off:off+4] == b"WZGR"; off += 4
    ver, = struct.unpack_from("<i", d, off); off += 4
    minIndex, size, zoneSize = struct.unpack_from("<iii", d, off); off += 12
    rc, = struct.unpack_from("<i", d, off); off += 4
    id2key = {}
    for _ in range(rc):
        rid, = struct.unpack_from("<i", d, off); off += 4
        klen, = struct.unpack_from("<i", d, off); off += 4
        id2key[rid] = d[off:off+klen].decode("utf-8"); off += klen
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

BIOME_RGB = {
    0:(28,32,46), 256:(24,36,64), 1:(150,168,110), 8:(70,96,74), 2:(120,100,70),
    4:(220,222,228), 16:(198,190,96), 32:(150,60,48), 64:(210,224,236), 512:(96,96,110),
}

def hillshade(H, size, zoneSize):
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
    grid_path = sys.argv[1]
    named_path = sys.argv[2]
    veg_path  = sys.argv[3]
    out_path  = sys.argv[4] if len(sys.argv) > 4 else "ForTheWort_ore_map.png"

    g = load_grid(grid_path)
    size, zs, minIndex = g["size"], g["zoneSize"], g["minIndex"]
    R, B, H = g["region"], g["biome"], g["height"]
    named = json.load(open(named_path))
    veg = json.load(open(veg_path))
    veg_regions = veg["regions"]
    by_key = {r["regionKey"]: r for r in named["regions"]}

    sh = hillshade(H, size, zs)
    W = size*SCALE
    topbar = 60
    img = Image.new("RGB", (W, topbar+W+120), (18, 20, 28))
    px = img.load()

    # base raster — DESATURATED a touch so ore markers pop (multiply toward grey)
    for gy in range(size):
        py0 = topbar + (size-1-gy)*SCALE
        for gx in range(size):
            b = B[gy][gx]; rid = R[gy][gx]
            base = BIOME_RGB.get(b, (255,0,255))
            s = sh[gy][gx]
            lm = region_lightness(rid) if rid >= 0 else 1.0
            # dim land slightly to make overlay markers read
            dim = 0.78 if rid >= 0 else 1.0
            r = max(0,min(255,int(base[0]*s*lm*dim)))
            gg= max(0,min(255,int(base[1]*s*lm*dim)))
            bl= max(0,min(255,int(base[2]*s*lm*dim)))
            x0 = gx*SCALE
            for dy in range(SCALE):
                for dx in range(SCALE):
                    px[x0+dx, py0+dy] = (r,gg,bl)

    draw = ImageDraw.Draw(img)
    # faint region borders (lighter than the names map so ore is the star)
    for gy in range(size):
        py0 = topbar + (size-1-gy)*SCALE
        for gx in range(size):
            rid = R[gy][gx]
            if rid < 0: continue
            for dx,dy in ((1,0),(0,1)):
                nx,ny = gx+dx, gy+dy
                nid = R[ny][nx] if (0<=nx<size and 0<=ny<size) else -1
                if nid != rid:
                    x0 = gx*SCALE
                    if dx==1:  draw.line([(x0+SCALE-1,py0),(x0+SCALE-1,py0+SCALE-1)], fill=(120,124,134), width=1)
                    if dy==1:  draw.line([(x0,py0),(x0+SCALE-1,py0)], fill=(120,124,134), width=1)

    # ── ORE OVERLAY ──
    # graduated markers: radius + inner brightness scale with resourceTotal.
    maxore = max((rv["resourceTotal"] for rv in veg_regions.values()), default=1) or 1
    def centroid_px(key):
        r = by_key.get(key)
        if not r: return None
        cx_m, cz_m = r["centroidMeters"]["x"], r["centroidMeters"]["z"]
        zx = cx_m/zs - minIndex; zy = cz_m/zs - minIndex
        return int(zx*SCALE), topbar + int((size-1-zy)*SCALE)

    # draw smaller ore first so big rich regions sit on top
    ordered = sorted(veg_regions.items(), key=lambda kv: kv[1]["resourceTotal"])
    fnum = font(11, bold=True)
    for key, rv in ordered:
        ore = rv["resourceTotal"]
        if ore <= 0: continue
        p = centroid_px(key)
        if not p: continue
        sx, sy = p
        frac = ore/maxore
        rad = int(5 + math.sqrt(frac)*26)          # area-ish scaling, 5..31 px
        bright = int(90 + frac*150)                # 90..240 value ramp (NOT hue)
        # outer white ring (always visible regardless of background)
        draw.ellipse([sx-rad-2, sy-rad-2, sx+rad+2, sy+rad+2], outline=(245,246,250), width=2)
        # inner value-ramp fill (greyscale-amber by VALUE; readable for colorblind)
        fill = (bright, int(bright*0.82), int(bright*0.30))
        draw.ellipse([sx-rad, sy-rad, sx+rad, sy+rad], fill=fill)
        # count label centered, dark plate for contrast
        s = str(ore)
        tb = draw.textbbox((0,0), s, font=fnum); tw=tb[2]-tb[0]; th=tb[3]-tb[1]
        draw.text((sx-tw//2, sy-th//2), s, font=fnum, fill=(16,16,12) if bright>150 else (245,246,250))

    # title + legend
    ft = font(26, bold=True); fs = font(15)
    total_ore = sum(rv["resourceTotal"] for rv in veg_regions.values())
    nreg = sum(1 for rv in veg_regions.values() if rv["resourceTotal"]>0)
    draw.text((14,12), "NIFLHEIM · ORE", font=ft, fill=(240,242,248))
    draw.text((250,20), f"seed ForTheWort  ·  {total_ore:,} modeled ore nodes  ·  {nreg} regions with ore  ·  source=modeled (upper-bias)",
              font=fs, fill=(180,184,196))
    ly = topbar+W+10
    draw.text((14,ly), "Marker SIZE + inner BRIGHTNESS = ore richness (not hue). Number = modeled node count. Bigger+brighter = more ore.",
              font=fs, fill=(176,180,192))
    # graduated size key
    ly2 = ly+30; lx = 16
    for label, val in [("low", int(maxore*0.12)), ("mid", int(maxore*0.45)), ("high", maxore)]:
        frac = val/maxore
        rad = int(5 + math.sqrt(frac)*26); bright = int(90+frac*150)
        cx = lx+rad
        draw.ellipse([cx-rad-2, ly2+22-rad-2, cx+rad+2, ly2+22+rad+2], outline=(245,246,250), width=2)
        draw.ellipse([cx-rad, ly2+22-rad, cx+rad, ly2+22+rad], fill=(bright,int(bright*0.82),int(bright*0.30)))
        draw.text((cx-12, ly2+44), f"{label} (~{val})", font=font(11), fill=(190,194,206))
        lx += rad*2 + 90

    img.save(out_path)
    print(f"saved {out_path}  ({img.size[0]}x{img.size[1]})  ore-regions {nreg}  total {total_ore}")

if __name__ == "__main__":
    main()
