#!/usr/bin/env python3
"""Border-explorer: on REAL terrain sampled from the verified port, compare today's unweighted BFS
region growth against the proposed weighted-Dijkstra (watershed-style) growth, and render both on a
hillshaded terrain + biome basemap. Colorblind-safe: biomes by lightness ramp + text legend; the two
border styles distinguished by line STYLE (dashed vs solid) and a value-only agreement inset.

Reads WZBX binary from export_patch. Pure stdlib + PIL + (numpy if available, else pure-python).
"""
import struct, sys, os, math, heapq
from PIL import Image, ImageDraw, ImageFont

BIOME_NAME = {0:"None",1:"Meadows",2:"Swamp",4:"Mountain",8:"BlackForest",
              16:"Plains",32:"AshLands",64:"DeepNorth",256:"Ocean",512:"Mistlands"}
# lightness ramp (colorblind-safe: distinguishable by value, labeled in text)
BIOME_VAL = {256:(28,40,66),    # ocean  - darkest, bluish
             0:(20,20,24),
             2:(78,86,70),      # swamp  - dark olive
             8:(54,86,60),      # blackforest - dark green
             4:(225,228,235),   # mountain - near white
             64:(180,205,225),  # deepnorth - pale ice
             1:(150,180,96),    # meadows - light green
             16:(196,186,96),   # plains - tan-yellow
             512:(120,116,140), # mistlands - grey-violet
             32:(170,70,48)}    # ashlands - red

def load(path):
    with open(path,"rb") as f:
        magic = f.read(4)
        assert magic == b"WZBX", magic
        ver, = struct.unpack("<i", f.read(4))
        ox,oz,step = struct.unpack("<fff", f.read(12))
        nx,nz = struct.unpack("<ii", f.read(8))
        H = [[0.0]*nx for _ in range(nz)]
        B = [[0]*nx for _ in range(nz)]
        for jz in range(nz):
            for ix in range(nx):
                h, b, _pad = struct.unpack("<fHH", f.read(8))
                H[jz][ix]=h; B[jz][ix]=b
    return dict(ox=ox,oz=oz,step=step,nx=nx,nz=nz,H=H,B=B)

def slope_grid(H, step):
    nz=len(H); nx=len(H[0])
    S=[[0.0]*nx for _ in range(nz)]
    for z in range(nz):
        for x in range(nx):
            x0=max(0,x-1); x1=min(nx-1,x+1); z0=max(0,z-1); z1=min(nz-1,z+1)
            dx=(H[z][x1]-H[z][x0])/((x1-x0)*step or 1)
            dz=(H[z1][x]-H[z0][x])/((z1-z0)*step or 1)
            S[z][x]=math.hypot(dx,dz)
    return S

def is_water(b): return b==256 or b==0

def cost_field(P, S):
    """Edge-traversal cost: high where crossing should be EXPENSIVE so borders fall there.
    Terms: base 1; + slope (ridges = walls); + shore crossing (land<->water); + biome transition."""
    nz=P['nz']; nx=P['nx']; B=P['B']
    C=[[1.0]*nx for _ in range(nz)]
    smax=max(max(r) for r in S) or 1.0
    for z in range(nz):
        for x in range(nx):
            slope_term = 6.0*(S[z][x]/smax)        # ridge cost (tunable weight)
            C[z][x]=1.0+slope_term
    return C

def neighbors4(x,z,nx,nz):
    if x>0: yield x-1,z
    if x<nx-1: yield x+1,z
    if z>0: yield x,z-1
    if z<nz-1: yield x,z+1

def grow_unweighted(P, seeds):
    """Today's algorithm: multi-source BFS, all steps equal, land-only."""
    nz=P['nz']; nx=P['nx']; B=P['B']
    owner=[[-1]*nx for _ in range(nz)]
    from collections import deque
    dq=deque()
    for i,(sx,sz) in enumerate(seeds):
        owner[sz][sx]=i; dq.append((sx,sz))
    while dq:
        x,z=dq.popleft(); o=owner[z][x]
        for nx_,nz_ in neighbors4(x,z,nx,nz):
            if owner[nz_][nx_]!=-1: continue
            if is_water(B[nz_][nx_]): continue
            owner[nz_][nx_]=o; dq.append((nx_,nz_))
    return owner

def grow_weighted(P, seeds, C):
    """Proposed: multi-source Dijkstra on the cost field. Borders fall on high-cost seams
    (ridges/shores). Land-only like today, but cost-weighted so the MIDLINE bends to features."""
    nz=P['nz']; nx=P['nx']; B=P['B']
    owner=[[-1]*nx for _ in range(nz)]
    dist=[[float('inf')]*nx for _ in range(nz)]
    pq=[]
    for i,(sx,sz) in enumerate(seeds):
        dist[sz][sx]=0.0; owner[sz][sx]=i; heapq.heappush(pq,(0.0,sx,sz,i))
    while pq:
        d,x,z,o=heapq.heappop(pq)
        if d>dist[z][x]: continue
        for nx_,nz_ in neighbors4(x,z,nx,nz):
            if is_water(B[nz_][nx_]): continue
            # cost to ENTER neighbor = its cell cost (ridge/shore expensive)
            nd=d+C[nz_][nx_]
            if nd<dist[nz_][nx_]:
                dist[nz_][nx_]=nd; owner[nz_][nx_]=o; heapq.heappush(pq,(nd,nx_,nz_,o))
    return owner

def hillshade(H, step, az=315, alt=45):
    nz=len(H); nx=len(H[0])
    azr=math.radians(az); altr=math.radians(alt)
    lx=math.cos(altr)*math.cos(azr); ly=math.cos(altr)*math.sin(azr); lz=math.sin(altr)
    out=[[0.0]*nx for _ in range(nz)]
    vscale=2000.0  # exaggerate normalized height for shading
    for z in range(nz):
        for x in range(nx):
            x0=max(0,x-1);x1=min(nx-1,x+1);z0=max(0,z-1);z1=min(nz-1,z+1)
            dx=(H[z][x1]-H[z][x0])*vscale/((x1-x0)*step or 1)
            dz=(H[z1][x]-H[z0][x])*vscale/((z1-z0)*step or 1)
            nlen=math.sqrt(dx*dx+dz*dz+1)
            shade=(-dx*lx -dz*ly +1*lz)/nlen
            out[z][x]=max(0.0,min(1.0,0.5+0.6*shade))
    return out

def border_cells(owner):
    """Cells on a region boundary (4-neighbor owner differs)."""
    nz=len(owner); nx=len(owner[0])
    edge=set()
    for z in range(nz):
        for x in range(nx):
            o=owner[z][x]
            if o<0: continue
            for nx_,nz_ in neighbors4(x,z,nx,nz):
                if owner[nz_][nx_]!=o:
                    edge.add((x,z)); break
    return edge

def font(sz,bold=False):
    for p in ["/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"%("-Bold" if bold else""),
              "/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf"%("-Bold" if bold else"")]:
        try: return ImageFont.truetype(p,sz)
        except Exception: pass
    return ImageFont.load_default()

def render(P, seeds, owner_bfs, owner_dij, S, title, out):
    nz=P['nz']; nx=P['nx']; B=P['B']; H=P['H']; step=P['step']
    hs=hillshade(H,step)
    SCALE=3
    def basemap():
        im=Image.new("RGB",(nx,nz))
        px=im.load()
        for z in range(nz):
            for x in range(nx):
                base=BIOME_VAL.get(B[z][x],(120,120,120))
                sh=0.45+0.55*hs[z][x]
                px[x,z]=(int(base[0]*sh),int(base[1]*sh),int(base[2]*sh))
        return im.resize((nx*SCALE,nz*SCALE),Image.NEAREST)
    bfs_edge=border_cells(owner_bfs)
    dij_edge=border_cells(owner_dij)

    panelW=nx*SCALE; panelH=nz*SCALE
    pad=24; topgap=70; botgap=150
    W=panelW*2+pad*3; Hh=panelH+topgap+botgap
    canvas=Image.new("RGB",(W,Hh),(24,24,28))
    d=ImageDraw.Draw(canvas)
    FT=font(26,True); FS=font(16); FL=font(15,True); FA=font(15)

    def stamp(img, edge, seeds, ox, oy, dashed):
        canvas.paste(img,(ox,oy))
        dd=ImageDraw.Draw(canvas)
        # borders
        for i,(x,z) in enumerate(sorted(edge)):
            cx=ox+int((x+0.5)*SCALE); cy=oy+int((z+0.5)*SCALE)
            if dashed and ((x+z)%2==0): continue
            dd.rectangle([cx-SCALE//2,cy-SCALE//2,cx+SCALE//2,cy+SCALE//2],fill=(255,255,255))
        # seeds
        for (sx,sz) in seeds:
            cx=ox+int((sx+0.5)*SCALE); cy=oy+int((sz+0.5)*SCALE)
            dd.ellipse([cx-5,cy-5,cx+5,cy+5],fill=(255,210,80),outline=(0,0,0))

    bm1=basemap(); bm2=bm1.copy()
    ox1=pad; ox2=pad*2+panelW; oy=topgap
    stamp(bm1,bfs_edge,seeds,ox1,oy,dashed=True)
    stamp(bm2,dij_edge,seeds,ox2,oy,dashed=False)

    def centered(t,cx,y,fnt,fill):
        w=d.textlength(t,font=fnt); d.text((cx-w/2,y),t,font=fnt,fill=fill)
    centered(title,W/2,16,FT,(238,238,244))
    centered("TODAY: unweighted BFS  (border = geometric midline, dashed)",ox1+panelW/2,topgap-22,FL,(235,235,240))
    centered("PROPOSED: weighted Dijkstra  (border bends to ridges/slope, solid)",ox2+panelW/2,topgap-22,FL,(250,220,120))

    # biome legend (only present biomes)
    present=sorted({B[z][x] for z in range(nz) for x in range(nx)})
    lx=pad; ly=topgap+panelH+14
    d.text((lx,ly),"biomes (lightness ramp; labeled because hue is unreliable):",font=FL,fill=(210,210,218))
    lx2=pad; ly2=ly+24
    for b in present:
        col=BIOME_VAL.get(b,(120,120,120))
        d.rectangle([lx2,ly2,lx2+16,ly2+16],fill=col,outline=(90,90,98))
        nm=BIOME_NAME.get(b,str(b))
        d.text((lx2+22,ly2),nm,font=FA,fill=(220,220,228))
        lx2+=22+ d.textlength(nm,font=FA)+22
    d.text((pad,ly2+30),"gold dots = region seeds (identical in both).  Same seeds, same terrain — only the growth rule differs.",font=FS,fill=(180,184,194))
    d.text((pad,ly2+52),"Watch where the SOLID (proposed) border departs the DASHED (today) midline to sit on a ridge crest or shoreline.",font=FS,fill=(180,184,194))

    canvas.save(out)
    print("saved",out,canvas.size)

def pick_seeds(P, k=4, margin=20):
    """Place k seeds deterministically on LAND, spread out (farthest-point), so both algorithms
    get the identical seed set. Mirrors the engine's intent without importing it."""
    nz=P['nz']; nx=P['nx']; B=P['B']
    land=[(x,z) for z in range(margin,nz-margin) for x in range(margin,nx-margin) if not is_water(B[z][x])]
    if not land: return []
    seeds=[land[len(land)//2]]
    while len(seeds)<k:
        best=None;bd=-1
        for (x,z) in land[::7]:  # subsample for speed
            dmin=min((x-sx)**2+(z-sz)**2 for (sx,sz) in seeds)
            if dmin>bd: bd=dmin;best=(x,z)
        if best is None: break
        seeds.append(best)
    return seeds

def main():
    patch=sys.argv[1]; title=sys.argv[2]; out=sys.argv[3]
    P=load(patch)
    S=slope_grid(P['H'],P['step'])
    C=cost_field(P,S)
    seeds=pick_seeds(P,k=4)
    if len(seeds)<2:
        print("not enough land for seeds in",patch); return
    owner_bfs=grow_unweighted(P,seeds)
    owner_dij=grow_weighted(P,seeds,C)
    render(P,seeds,owner_bfs,owner_dij,S,title,out)

if __name__=="__main__":
    main()
