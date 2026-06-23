#!/usr/bin/env python3
"""OFFLINE ESP — the decision instrument. A high-fidelity composite view of what the regions system
computes on REAL terrain, the tool we use to DEVELOP the border algorithm before the in-world walk.
Renders, layered: hillshade terrain -> biome tint -> region fills (lightness) -> feature stipple
-> border lines (v3) -> seeds -> region name labels at centroids. This is the "ESP" in the sense
that matters now: see everything the engine knows, on ground truth, side-by-side toggleable layers.

Usage: offline_esp.py <patch.bin> <out.png> [title]
"""
import struct, sys, math, heapq
from collections import deque, defaultdict
from PIL import Image, ImageDraw, ImageFont

BIOME_NAME={0:"None",1:"Meadows",2:"Swamp",4:"Mountain",8:"BlackForest",16:"Plains",32:"AshLands",64:"DeepNorth",256:"Ocean",512:"Mistlands"}
BIOME_VAL={256:(26,38,64),0:(20,20,24),2:(74,82,66),8:(50,82,56),4:(222,226,233),64:(178,203,223),1:(146,176,92),16:(192,182,92),512:(116,112,136),32:(168,68,46)}
# region tints: distinct hues BUT we rely on the LIGHTNESS ramp + labels; outline every region.
REGION_TINT=[(235,225,140),(150,205,235),(235,160,150),(170,225,160),(210,170,225),(240,200,150),(160,220,210),(225,225,225)]

def load(path):
    with open(path,"rb") as f:
        assert f.read(4)==b"WZBX"; struct.unpack("<i",f.read(4))
        ox,oz,step=struct.unpack("<fff",f.read(12)); nx,nz=struct.unpack("<ii",f.read(8))
        H=[[0.0]*nx for _ in range(nz)]; B=[[0]*nx for _ in range(nz)]
        for jz in range(nz):
            for ix in range(nx):
                h,b,_p=struct.unpack("<fHH",f.read(8)); H[jz][ix]=h;B[jz][ix]=b
    return dict(ox=ox,oz=oz,step=step,nx=nx,nz=nz,H=H,B=B)
def water(b): return b==256 or b==0
def nb(x,z,nx,nz):
    for a,b in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
        if 0<=a<nx and 0<=b<nz: yield a,b
def edges_sets(P):
    nz=P['nz'];nx=P['nx'];B=P['B'];E=set();SH=set()
    for z in range(nz):
        for x in range(nx):
            if water(B[z][x]):continue
            for a,b in nb(x,z,nx,nz):
                if water(B[b][a]):SH.add((x,z))
                elif B[b][a]!=B[z][x]:E.add((x,z))
    return E,SH
def seeds_of(P,k=5,m=18):
    nz=P['nz'];nx=P['nx'];B=P['B'];land=[(x,z) for z in range(m,nz-m) for x in range(m,nx-m) if not water(B[z][x])]
    s=[land[len(land)//2]]
    while len(s)<k:
        best=None;bd=-1
        for (x,z) in land[::5]:
            dm=min((x-a)**2+(z-b)**2 for a,b in s)
            if dm>bd:bd=dm;best=(x,z)
        s.append(best)
    return s
def dijk(P,seeds,C):
    nz=P['nz'];nx=P['nx'];B=P['B'];o=[[-1]*nx for _ in range(nz)];dist=[[1e18]*nx for _ in range(nz)];pq=[]
    for i,(x,z) in enumerate(seeds):dist[z][x]=0;o[z][x]=i;heapq.heappush(pq,(0,x,z,i))
    while pq:
        d,x,z,ow=heapq.heappop(pq)
        if d>dist[z][x]:continue
        for a,b in nb(x,z,nx,nz):
            if water(B[b][a]):continue
            ndd=d+C[b][a]
            if ndd<dist[b][a]:dist[b][a]=ndd;o[b][a]=ow;heapq.heappush(pq,(ndd,a,b,ow))
    return o
def edges_of(o):
    nz=len(o);nx=len(o[0]);e=set()
    for z in range(nz):
        for x in range(nx):
            if o[z][x]<0:continue
            for a,b in nb(x,z,nx,nz):
                if o[b][a]!=o[z][x]:e.add((x,z));break
    return e
def hillshade(H,step):
    nz=len(H);nx=len(H[0]);out=[[0.0]*nx for _ in range(nz)]
    az=math.radians(315);al=math.radians(45);lx=math.cos(al)*math.cos(az);ly=math.cos(al)*math.sin(az);lz=math.sin(al)
    for z in range(nz):
        for x in range(nx):
            x0=max(0,x-1);x1=min(nx-1,x+1);z0=max(0,z-1);z1=min(nz-1,z+1)
            dx=(H[z][x1]-H[z][x0])*2000/((x1-x0)*step or 1);dz=(H[z1][x]-H[z0][x])*2000/((z1-z0)*step or 1)
            n=math.sqrt(dx*dx+dz*dz+1);out[z][x]=max(0,min(1,0.5+0.6*(-dx*lx-dz*ly+lz)/n))
    return out
def centroids(o,k):
    acc=defaultdict(lambda:[0,0,0])
    for z in range(len(o)):
        for x in range(len(o[0])):
            v=o[z][x]
            if v>=0: acc[v][0]+=x;acc[v][1]+=z;acc[v][2]+=1
    return {v:(a[0]/a[2],a[1]/a[2]) for v,a in acc.items() if a[2]>0}
def font(sz,bold=False):
    for p in ["/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"%("-Bold" if bold else""),"/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf"%("-Bold" if bold else"")]:
        try:return ImageFont.truetype(p,sz)
        except Exception:pass
    return ImageFont.load_default()
# deterministic evocative-ish placeholder names keyed by region identity coord (mirrors RegionKey idea)
SYL1=["Vex","Drak","Mor","Thorn","Grim","Fen","Hollow","Ash","Bram","Skald","Myr","Holt"]
SYL2=["wood","marsh","reach","fell","mere","crag","vale","moor","hollow","gard","wick","barrow"]
def name_for(cx,cz):
    h=(int(cx)*73856093 ^ int(cz)*19349663)&0xffffffff
    return SYL1[h%len(SYL1)]+SYL2[(h//len(SYL1))%len(SYL2)]

def main():
    patch=sys.argv[1]; out=sys.argv[2]; title=sys.argv[3] if len(sys.argv)>3 else patch
    P=load(patch);E,SH=edges_sets(P);seeds=seeds_of(P)
    C=[[ (12.0 if (x,z) in E else 8.0 if (x,z) in SH else 1.0) for x in range(P['nx'])] for z in range(P['nz'])]
    o=dijk(P,seeds,C); border=edges_of(o); cents=centroids(o,len(seeds))
    nz=P['nz'];nx=P['nx'];B=P['B'];H=P['H'];hs=hillshade(H,P['step']);SCALE=4
    im=Image.new("RGB",(nx,nz));px=im.load()
    for z in range(nz):
        for x in range(nx):
            bc=BIOME_VAL.get(B[z][x],(120,120,120))
            sh=0.5+0.5*hs[z][x]
            # blend region tint over land (subtle, lightness still carries)
            ow=o[z][x]
            if ow>=0 and not water(B[z][x]):
                t=REGION_TINT[ow%len(REGION_TINT)]
                bc=(int(bc[0]*0.7+t[0]*0.3),int(bc[1]*0.7+t[1]*0.3),int(bc[2]*0.7+t[2]*0.3))
            px[x,z]=(int(bc[0]*sh),int(bc[1]*sh),int(bc[2]*sh))
    im=im.resize((nx*SCALE,nz*SCALE),Image.NEAREST)
    d=ImageDraw.Draw(im)
    # feature stipple (gold) faint
    for (x,z) in (E|SH):
        d.point([(x*SCALE+SCALE//2,z*SCALE+SCALE//2)],fill=(150,128,60))
    # border lines (white)
    for (x,z) in border:
        cx=x*SCALE+SCALE//2;cy=z*SCALE+SCALE//2
        d.rectangle([cx-SCALE//2,cy-SCALE//2,cx+SCALE//2,cy+SCALE//2],fill=(255,255,255))
    # seeds + name labels
    FN=font(15,True)
    for i,(sx,sz) in enumerate(seeds):
        cx=sx*SCALE+SCALE//2;cy=sz*SCALE+SCALE//2
        d.ellipse([cx-5,cy-5,cx+5,cy+5],fill=(255,210,80),outline=(0,0,0))
    for v,(cx,cz) in cents.items():
        nm=name_for(cx,cz)
        tx=cx*SCALE;ty=cz*SCALE
        w=d.textlength(nm,font=FN)
        d.rectangle([tx-w/2-3,ty-10,tx+w/2+3,ty+10],fill=(0,0,0))
        d.text((tx-w/2,ty-8),nm,font=FN,fill=(255,255,255))
    # frame + header + legend
    W=nx*SCALE; topbar=44; botbar=86; Hh=nz*SCALE+topbar+botbar
    cv=Image.new("RGB",(W,Hh),(22,22,26)); cv.paste(im,(0,topbar))
    dd=ImageDraw.Draw(cv); FT=font(22,True); FA=font(14)
    dd.text((12,12),f"OFFLINE ESP \u2014 {title}",font=FT,fill=(238,238,244))
    # on-feature stat
    feat=E|SH
    hit=sum(1 for (x,z) in border if any((x+dx,z+dz) in feat for dx in(-1,0,1) for dz in(-1,0,1)))
    pct=100*hit/len(border) if border else 0
    dd.text((12,topbar+nz*SCALE+8),f"v3 biome-edge barriers  \u00b7  border {pct:.0f}% on-feature  \u00b7  {len(seeds)} regions  \u00b7  names are RegionKey-derived placeholders",font=FA,fill=(190,194,204))
    # biome legend
    present=sorted({B[z][x] for z in range(nz) for x in range(nx)})
    lx=12;ly=topbar+nz*SCALE+30
    for b in present:
        c=BIOME_VAL.get(b,(120,120,120)); dd.rectangle([lx,ly,lx+14,ly+14],fill=c,outline=(80,80,88))
        nm=BIOME_NAME.get(b,str(b)); dd.text((lx+19,ly),nm,font=FA,fill=(210,210,218)); lx+=19+dd.textlength(nm,font=FA)+20
    dd.text((12,topbar+nz*SCALE+52),"white = region border (v3)  \u00b7  gold stipple = biome-edge/shore feature  \u00b7  yellow dot = seed  \u00b7  black tag = region name",font=FA,fill=(170,174,184))
    cv.save(out); print("saved",out,cv.size,f"on-feature={pct:.0f}%")

if __name__=="__main__": main()
