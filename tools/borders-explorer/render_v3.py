#!/usr/bin/env python3
"""Render BFS vs V3-barrier side by side on a patch, on hillshade+biome basemap, so we can SEE
whether v3's borders hug biome edges. Colorblind-safe (lightness ramp + text legend)."""
import struct, sys, math, heapq
from collections import deque
from PIL import Image, ImageDraw, ImageFont

BIOME_NAME={0:"None",1:"Meadows",2:"Swamp",4:"Mountain",8:"BlackForest",16:"Plains",32:"AshLands",64:"DeepNorth",256:"Ocean",512:"Mistlands"}
BIOME_VAL={256:(28,40,66),0:(20,20,24),2:(78,86,70),8:(54,86,60),4:(225,228,235),64:(180,205,225),1:(150,180,96),16:(196,186,96),512:(120,116,140),32:(170,70,48)}
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
def seeds_of(P,k=4,m=20):
    nz=P['nz'];nx=P['nx'];B=P['B'];land=[(x,z) for z in range(m,nz-m) for x in range(m,nx-m) if not water(B[z][x])]
    s=[land[len(land)//2]]
    while len(s)<k:
        best=None;bd=-1
        for (x,z) in land[::7]:
            dm=min((x-a)**2+(z-b)**2 for a,b in s)
            if dm>bd:bd=dm;best=(x,z)
        s.append(best)
    return s
def bfs(P,seeds):
    nz=P['nz'];nx=P['nx'];B=P['B'];o=[[-1]*nx for _ in range(nz)];dq=deque()
    for i,(x,z) in enumerate(seeds):o[z][x]=i;dq.append((x,z))
    while dq:
        x,z=dq.popleft();ow=o[z][x]
        for a,b in nb(x,z,nx,nz):
            if o[b][a]!=-1 or water(B[b][a]):continue
            o[b][a]=ow;dq.append((a,b))
    return o
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
def font(sz,bold=False):
    for p in ["/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"%("-Bold" if bold else""),"/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf"%("-Bold" if bold else"")]:
        try:return ImageFont.truetype(p,sz)
        except Exception:pass
    return ImageFont.load_default()
def main():
    tag=sys.argv[1]; patch=sys.argv[2]; out=sys.argv[3]
    P=load(patch);E,SH=edges_sets(P);seeds=seeds_of(P)
    feat=E|SH
    C=[[ (12.0 if (x,z) in E else 8.0 if (x,z) in SH else 1.0) for x in range(P['nx'])] for z in range(P['nz'])]
    ob=bfs(P,seeds);od=dijk(P,seeds,C)
    eb=edges_of(ob);ed=edges_of(od)
    nz=P['nz'];nx=P['nx'];B=P['B'];H=P['H'];hs=hillshade(H,P['step']);SCALE=3
    def base():
        im=Image.new("RGB",(nx,nz));px=im.load()
        for z in range(nz):
            for x in range(nx):
                c=BIOME_VAL.get(B[z][x],(120,120,120));sh=0.45+0.55*hs[z][x]
                px[x,z]=(int(c[0]*sh),int(c[1]*sh),int(c[2]*sh))
        return im.resize((nx*SCALE,nz*SCALE),Image.NEAREST)
    pw=nx*SCALE;ph=nz*SCALE;pad=22;top=66;bot=120
    W=pw*2+pad*3;Hh=ph+top+bot
    cv=Image.new("RGB",(W,Hh),(24,24,28));d=ImageDraw.Draw(cv)
    def stamp(edge,ox,oy,col):
        cv.paste(base(),(ox,oy));dd=ImageDraw.Draw(cv)
        # faint feature cells (gold) so we can see if borders land on them
        for (x,z) in feat:
            cx=ox+x*SCALE+SCALE//2;cy=oy+z*SCALE+SCALE//2
            dd.point([(cx,cy)],fill=(120,100,40))
        for (x,z) in edge:
            cx=ox+x*SCALE+SCALE//2;cy=oy+z*SCALE+SCALE//2
            dd.rectangle([cx-SCALE//2,cy-SCALE//2,cx+SCALE//2,cy+SCALE//2],fill=col)
        for (sx,sz) in seeds:
            cx=ox+sx*SCALE+SCALE//2;cy=oy+sz*SCALE+SCALE//2
            dd.ellipse([cx-5,cy-5,cx+5,cy+5],fill=(255,210,80),outline=(0,0,0))
    ox1=pad;ox2=pad*2+pw;oy=top
    stamp(eb,ox1,oy,(255,255,255));stamp(ed,ox2,oy,(120,230,140))
    def feat_pct(edge):
        if not edge: return 0
        hit=sum(1 for (x,z) in edge if any((x+dx,z+dz) in feat for dx in(-1,0,1) for dz in(-1,0,1)))
        return 100*hit/len(edge)
    FT=font(24,True);FL=font(16,True);FA=font(14)
    def ctr(t,cx,y,fn,fl):
        w=d.textlength(t,font=fn);d.text((cx-w/2,y),t,font=fn,fill=fl)
    ctr(f"BFS vs V3-barrier on real terrain \u2014 {tag}",W/2,16,FT,(238,238,244))
    ctr(f"TODAY (unweighted BFS) \u2014 {feat_pct(eb):.0f}% on-feature",ox1+pw/2,top-22,FL,(235,235,240))
    ctr(f"V3 (biome-edge BARRIER cost) \u2014 {feat_pct(ed):.0f}% on-feature",ox2+pw/2,top-22,FL,(150,240,170))
    d.text((pad,top+ph+12),"gold stipple = biome-edge/shore feature cells.  white/green = region border.  Does the border sit ON the gold?",font=FA,fill=(180,184,194))
    d.text((pad,top+ph+34),"V3 makes biome edges EXPENSIVE TO CROSS (walls) so regions MEET at them \u2014 not cheap (which made tendrils).",font=FA,fill=(180,184,194))
    d.text((pad,top+ph+56),"Where V3's green border departs BFS's white: it has detoured to sit on a biome boundary / shoreline.",font=FA,fill=(180,184,194))
    cv.save(out);print("saved",out,cv.size,"bfs%",round(feat_pct(eb)),"v3%",round(feat_pct(ed)))
if __name__=="__main__":main()
