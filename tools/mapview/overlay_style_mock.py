#!/usr/bin/env python3
"""
World-map region overlay — DESIGN MOCK (not the mod; an eye-validation of the style dial).

Renders the 162-region gazetteer the way each overlay STYLE MODE would composite over Valheim's
circular world map, so Daniel can judge the concept before the in-world walk. Produces a 4-up:
  [vanilla-ish terrain]  [borders only]  [borders + translucent tint]  [full parchment]
+ a fog-of-war demo strip (explored vs map-wide) since that's the open design lock.

This is a MOCK: the "vanilla" panel is our terrain raster styled to evoke the game map (it is NOT a
screenshot of the real map — we can't run the Steam client headless). The geometry, borders, and
region fills ARE the real gazetteer data. Purpose: see the dial, judge the styles.
"""
import struct, sys, json, math, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

SCALE = 3  # px per zone (smaller — 4 panels)

def load_grid(path):
    with open(path, "rb") as f: d = f.read()
    off = 0
    assert d[off:off+4] == b"WZGR"; off += 4
    _ver, = struct.unpack_from("<i", d, off); off += 4
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
            b, _p = struct.unpack_from("<HH", d, off); off += 4
            h, = struct.unpack_from("<f", d, off); off += 4
            region[gy][gx]=rid; biome[gy][gx]=b; height[gy][gx]=h
    return dict(minIndex=minIndex,size=size,zoneSize=zoneSize,id2key=id2key,
                region=region,biome=biome,height=height)

# Valheim-map-evocative biome tints (the real game map is muted/desaturated terrain)
BIOME_VANILLA = {
    0:(40,44,58), 256:(38,56,92), 1:(120,140,86), 8:(58,84,60), 2:(92,84,58),
    4:(200,205,214), 16:(186,176,96), 32:(140,58,46), 64:(206,220,232), 512:(86,86,104),
}
# parchment palette: warm cream base, regions as soft ink-bordered tints by VALUE not hue
PARCH_BASE = (222, 206, 170)
PARCH_INK  = (64, 48, 32)
BIOME_NAME = {0:"None",256:"Ocean",1:"Meadows",8:"BlackForest",2:"Swamp",4:"Mountain",
              16:"Plains",32:"AshLands",64:"DeepNorth",512:"Mistlands"}

def font(sz, bold=False):
    for p in (f"/usr/share/fonts/truetype/dejavu/DejaVuSans{'-Bold' if bold else ''}.ttf",
              f"/usr/share/fonts/truetype/dejavu/DejaVuSerif{'-Bold' if bold else ''}.ttf"):
        try: return ImageFont.truetype(p, sz)
        except: pass
    return ImageFont.load_default()

def region_value(rid, lo=0.80, hi=1.16):
    if rid < 0: return 1.0
    h = (rid*2654435761) & 0xFFFFFFFF
    return lo + (h % 1000)/1000.0 * (hi-lo)

def hillshade(H, size, zs):
    sh=[[1.0]*size for _ in range(size)]
    lx,ly,lz=-1,-1,1.3; ln=math.sqrt(lx*lx+ly*ly+lz*lz); lx/=ln;ly/=ln;lz/=ln
    for y in range(size):
        for x in range(size):
            x0=H[y][max(0,x-1)]; x1=H[y][min(size-1,x+1)]
            y0=H[max(0,y-1)][x]; y1=H[min(size-1,y+1)][x]
            dx=(x1-x0)/(2*zs); dy=(y1-y0)/(2*zs)
            nx,ny,nz=-dx,-dy,1.0; nn=math.sqrt(nx*nx+ny*ny+nz*nz)
            sh[y][x]=max(0.6,min(1.2,0.78+(nx*lx+ny*ly+nz*lz)/nn*0.5))
    return sh

def is_border(R, gx, gy, size):
    rid=R[gy][gx]
    if rid<0: return False
    for dx,dy in ((1,0),(0,1),(-1,0),(0,-1)):
        nx,ny=gx+dx,gy+dy
        nid = R[ny][nx] if (0<=nx<size and 0<=ny<size) else -1
        if nid!=rid: return True
    return False

def render_panel(g, mode, fog_center=None, fog_radius=None):
    """mode in {vanilla, borders, tint, parchment}. fog_* optional: only reveal within radius (zones)."""
    size,zs=g["size"],g["zoneSize"]; R,B,H=g["region"],g["biome"],g["height"]
    sh=hillshade(H,size,zs)
    W=size*SCALE
    cxp, cyp = W/2, W/2
    img=Image.new("RGB",(W,W),(16,18,26) if mode!="parchment" else (200,184,150))
    px=img.load()
    cx0, cy0 = size/2, size/2
    rad_zones = size/2 - 1
    for gy in range(size):
        for gx in range(size):
            # circular world disc clip
            ddx, ddy = gx-cx0, gy-cy0
            dist = math.sqrt(ddx*ddx+ddy*ddy)
            inside = dist <= rad_zones
            rid=R[gy][gx]; b=B[gy][gx]; s=sh[gy][gx]
            # fog
            fogged = False
            if fog_center is not None:
                fdx,fdy = gx-fog_center[0], gy-fog_center[1]
                if math.sqrt(fdx*fdx+fdy*fdy) > fog_radius: fogged = True

            if not inside:
                col = (16,18,26) if mode!="parchment" else (200,184,150)
            elif mode=="vanilla":
                base=BIOME_VANILLA.get(b,(255,0,255)); col=tuple(max(0,min(255,int(c*s))) for c in base)
            elif mode=="borders":
                base=BIOME_VANILLA.get(b,(255,0,255)); col=tuple(max(0,min(255,int(c*s))) for c in base)
            elif mode=="tint":
                base=BIOME_VANILLA.get(b,(255,0,255))
                v=region_value(rid)
                # translucent region tint = blend terrain toward a value-separated grey-amber
                tint=(int(180*v),int(150*v),int(90*v))
                col=tuple(max(0,min(255,int((c*s)*0.6+t*0.4))) for c,t in zip(base,tint))
            elif mode=="parchment":
                if rid<0:
                    col=(176,196,150) if b==256 else (200,184,150)  # ocean hint vs paper
                else:
                    v=region_value(rid,0.86,1.06)
                    col=tuple(max(0,min(255,int(c*v))) for c in PARCH_BASE)
            if fogged and inside:
                # fog-of-war: desaturate + darken (minimap fog feel)
                if mode=="parchment":
                    col=tuple(int(c*0.62+150*0.38) for c in col)
                else:
                    g_=sum(col)//3; col=tuple(int(c*0.25+g_*0.12) for c in col)
            x0=gx*SCALE; y0=(size-1-gy)*SCALE
            for dyp in range(SCALE):
                for dxp in range(SCALE):
                    px[x0+dxp,y0+dyp]=col

    draw=ImageDraw.Draw(img,"RGBA")
    # borders for border/tint/parchment modes
    if mode in ("borders","tint","parchment"):
        bcol = (245,246,250,255) if mode!="parchment" else (PARCH_INK[0],PARCH_INK[1],PARCH_INK[2],255)
        bw = 1 if mode!="parchment" else 1
        for gy in range(size):
            for gx in range(size):
                ddx,ddy=gx-cx0,gy-cy0
                if math.sqrt(ddx*ddx+ddy*ddy)>rad_zones: continue
                if fog_center is not None:
                    fdx,fdy=gx-fog_center[0],gy-fog_center[1]
                    if math.sqrt(fdx*fdx+fdy*fdy)>fog_radius: continue
                if is_border(R,gx,gy,size):
                    x0=gx*SCALE; y0=(size-1-gy)*SCALE
                    draw.rectangle([x0,y0,x0+SCALE-1,y0+SCALE-1],fill=bcol)
    # circular frame ring
    ring=(70,76,92,255) if mode!="parchment" else (120,96,64,255)
    draw.ellipse([2,2,W-2,W-2],outline=ring,width=3)
    return img

def main():
    grid=sys.argv[1]; named=sys.argv[2]; out=sys.argv[3] if len(sys.argv)>3 else "overlay_mock.png"
    g=load_grid(grid)
    nm=json.load(open(named))
    size=g["size"]

    panels=[("vanilla","Vanilla map (regions OFF)"),
            ("borders","Borders only"),
            ("tint","Borders + region tint"),
            ("parchment","Parchment atlas")]
    rendered=[(render_panel(g,m), label) for m,label in panels]
    pw=rendered[0][0].size[0]
    pad=24; lblh=42; titleh=70
    cols=4
    cw=pw+pad; ch=pw+lblh+pad
    canvas=Image.new("RGB",(cw*cols+pad, ch+titleh),(12,13,18))
    d=ImageDraw.Draw(canvas)
    ft=font(30,bold=True); fl=font(19,bold=True); fs=font(15)
    d.text((pad, 16), "NIFLHEIM · region overlay — the style dial", font=ft, fill=(238,240,246))
    d.text((pad, 50), "one hotkey cycles these · same region geometry, four composites · MOCK (geometry real, terrain stylized — not a Steam screenshot)",
           font=fs, fill=(168,172,186))
    for i,(im,label) in enumerate(rendered):
        x=pad+i*cw; y=titleh
        canvas.paste(im,(x,y))
        d.text((x+6,y+pw+8),f"{i+1}. {label}",font=fl,fill=(228,230,238))
    canvas.save(out)
    print(f"saved {out} ({canvas.size[0]}x{canvas.size[1]})")

if __name__=="__main__":
    main()
