#!/usr/bin/env python3
"""Feature detection v2 on REAL port terrain. The v1 negative result: raw slope is too dense/noisy to
make a border commit to a seam. v2 EXTRACTS thin, crisp features and MEASURES their coherence before
trusting them. Features:
  - ridge skeleton   : Laplacian < 0 (concave-down crests), strong
  - valley skeleton  : Laplacian > 0 (concave-up channels) -- the swamp drainage candidate
  - shoreline        : land<->water boundary (crisp, already classified)
  - biome edge       : biome transition cells (crisp)
Outputs per-patch coherence stats + a feature-overlay render so we can SEE whether swamp channels are
real structure or noise.
"""
import struct, sys, math
from PIL import Image, ImageDraw, ImageFont

BIOME_NAME={0:"None",1:"Meadows",2:"Swamp",4:"Mountain",8:"BlackForest",16:"Plains",32:"AshLands",64:"DeepNorth",256:"Ocean",512:"Mistlands"}
SWAMP=2
def load(path):
    with open(path,"rb") as f:
        assert f.read(4)==b"WZBX"; struct.unpack("<i",f.read(4))
        ox,oz,step=struct.unpack("<fff",f.read(12)); nx,nz=struct.unpack("<ii",f.read(8))
        H=[[0.0]*nx for _ in range(nz)]; B=[[0]*nx for _ in range(nz)]
        for jz in range(nz):
            for ix in range(nx):
                h,b,_p=struct.unpack("<fHH",f.read(8)); H[jz][ix]=h;B[jz][ix]=b
    return dict(ox=ox,oz=oz,step=step,nx=nx,nz=nz,H=H,B=B)

def laplacian(H):
    nz=len(H);nx=len(H[0]);L=[[0.0]*nx for _ in range(nz)]
    for z in range(1,nz-1):
        for x in range(1,nx-1):
            L[z][x]=H[z][x-1]+H[z][x+1]+H[z-1][x]+H[z+1][x]-4*H[z][x]
    return L

def water(b): return b==256 or b==0

def detect(P):
    nz=P['nz'];nx=P['nx'];H=P['H'];B=P['B']
    L=laplacian(H)
    vals=[abs(L[z][x]) for z in range(1,nz-1) for x in range(1,nx-1)]
    vals.sort()
    thr=vals[int(0.85*len(vals))] if vals else 0  # top 15% magnitude = a "feature"
    ridge=set();valley=set();shore=set();bedge=set()
    for z in range(1,nz-1):
        for x in range(1,nx-1):
            l=L[z][x]
            if l<-thr and not water(B[z][x]): ridge.add((x,z))     # concave-down crest
            if l> thr and not water(B[z][x]): valley.add((x,z))    # concave-up channel
            # shore: land cell adjacent to water
            if not water(B[z][x]):
                for a,b in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
                    if water(B[b][a]): shore.add((x,z)); break
            # biome edge: land cell adjacent to a different LAND biome
            if not water(B[z][x]):
                for a,b in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)):
                    if not water(B[b][a]) and B[b][a]!=B[z][x]: bedge.add((x,z)); break
    return dict(L=L,ridge=ridge,valley=valley,shore=shore,bedge=bedge,thr=thr)

def coherence(feature_set, nx, nz):
    """How 'line-like' is a feature set? A coherent seam has most cells with 1-2 in-set neighbors
    (it's a curve); noise has cells with 0 or many. Return mean in-set-neighbor count + isolated frac."""
    if not feature_set: return (0.0, 1.0)
    tot=0; iso=0
    for (x,z) in feature_set:
        c=sum(1 for a,b in ((x-1,z),(x+1,z),(x,z-1),(x,z+1)) if (a,b) in feature_set)
        tot+=c
        if c==0: iso+=1
    return (tot/len(feature_set), iso/len(feature_set))

def swamp_internal(P, F):
    """Daniel's hypothesis: do swamp cells contain coherent INTERNAL valley(channel) structure?
    Restrict the valley skeleton to swamp cells and measure coherence + coverage."""
    nz=P['nz'];nx=P['nx'];B=P['B']
    swamp_cells=[(x,z) for z in range(nz) for x in range(nx) if B[z][x]==SWAMP]
    if not swamp_cells: return None
    sv=set((x,z) for (x,z) in F['valley'] if B[z][x]==SWAMP)
    sr=set((x,z) for (x,z) in F['ridge'] if B[z][x]==SWAMP)
    mean_nb,iso=coherence(sv,nx,nz)
    return dict(n_swamp=len(swamp_cells), n_valley=len(sv), n_ridge=len(sr),
                valley_cover=len(sv)/len(swamp_cells), valley_meannb=mean_nb, valley_iso=iso)

def font(sz,bold=False):
    for p in ["/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf"%("-Bold" if bold else""),
              "/usr/share/fonts/truetype/liberation/LiberationSans%s.ttf"%("-Bold" if bold else"")]:
        try: return ImageFont.truetype(p,sz)
        except Exception: pass
    return ImageFont.load_default()

def hillshade(H,step):
    nz=len(H);nx=len(H[0]);out=[[0.0]*nx for _ in range(nz)]
    az=math.radians(315);al=math.radians(45);lx=math.cos(al)*math.cos(az);ly=math.cos(al)*math.sin(az);lz=math.sin(al)
    for z in range(nz):
        for x in range(nx):
            x0=max(0,x-1);x1=min(nx-1,x+1);z0=max(0,z-1);z1=min(nz-1,z+1)
            dx=(H[z][x1]-H[z][x0])*2000/((x1-x0)*step or 1);dz=(H[z1][x]-H[z0][x])*2000/((z1-z0)*step or 1)
            n=math.sqrt(dx*dx+dz*dz+1);out[z][x]=max(0,min(1,0.5+0.6*(-dx*lx-dz*ly+lz)/n))
    return out

def render(P,F,out,title):
    nz=P['nz'];nx=P['nx'];B=P['B'];H=P['H']
    hs=hillshade(H,P['step']);SCALE=3
    im=Image.new("RGB",(nx,nz));px=im.load()
    for z in range(nz):
        for x in range(nx):
            g=int(40+150*hs[z][x])
            if water(B[z][x]): px[x,z]=(20,30,55)
            elif B[z][x]==SWAMP: px[x,z]=(int(60*hs[z][x]+30),int(70*hs[z][x]+35),int(45*hs[z][x]+25))
            else: px[x,z]=(g,g,g)
    im=im.resize((nx*SCALE,nz*SCALE),Image.NEAREST)
    d=ImageDraw.Draw(im)
    def dots(s,color,sz=1):
        for (x,z) in s:
            cx=x*SCALE+SCALE//2;cy=z*SCALE+SCALE//2
            d.rectangle([cx-sz,cy-sz,cx+sz,cy+sz],fill=color)
    dots(F['valley'],(90,150,255))   # channels - blue
    dots(F['ridge'],(255,150,60))    # ridges - orange
    dots(F['shore'],(255,255,255))   # shore - white
    # frame + legend bar
    W=nx*SCALE; Hh=nz*SCALE+96
    canvas=Image.new("RGB",(W,Hh),(24,24,28)); canvas.paste(im,(0,0))
    dd=ImageDraw.Draw(canvas); FT=font(20,True); FA=font(15)
    dd.text((10,nz*SCALE+8),title,font=FT,fill=(238,238,244))
    leg=[("ridge skeleton (concave-down crest)",(255,150,60)),
         ("valley/channel skeleton (concave-up)",(90,150,255)),
         ("shoreline (land/water edge)",(255,255,255))]
    lx=10;ly=nz*SCALE+40
    for txt,col in leg:
        dd.rectangle([lx,ly,lx+14,ly+14],fill=col); dd.text((lx+20,ly),txt,font=FA,fill=(220,220,228))
        lx+=20+dd.textlength(txt,font=FA)+26
    dd.text((10,nz*SCALE+66),"dark-olive shaded = SWAMP cells. Question: do blue channels form coherent lines INSIDE the swamp?",font=FA,fill=(180,184,194))
    canvas.save(out); print("saved",out,canvas.size)

def main():
    for tag,p in [("A","A_mountain_coast_swamp"),("B","B_mistlands_six"),("C","C_starter_ring")]:
        P=load(f"/tmp/wz_oracle/patches/{p}.bin")
        F=detect(P)
        print(f"\n== {tag} {p} ==")
        for fn in ("ridge","valley","shore","bedge"):
            mnb,iso=coherence(F[fn],P['nx'],P['nz'])
            print(f"   {fn:7}: {len(F[fn]):5} cells  mean-neighbors={mnb:.2f}  isolated={100*iso:.0f}%  ({'LINE-LIKE' if mnb>=1.3 and iso<0.25 else 'diffuse/noisy'})")
        sw=swamp_internal(P,F)
        if sw:
            print(f"   SWAMP internal: {sw['n_swamp']} swamp cells | valley-cover={100*sw['valley_cover']:.0f}% mean-nb={sw['valley_meannb']:.2f} isolated={100*sw['valley_iso']:.0f}%  -> {'COHERENT CHANNELS' if sw['valley_meannb']>=1.2 and sw['valley_iso']<0.3 else 'incoherent/sparse'}")
        render(P,F,f"/tmp/wz_oracle/features_{tag}.png",f"Feature detection \u2014 Case {tag} ({p})")

if __name__=="__main__": main()
