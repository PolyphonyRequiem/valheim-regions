#!/usr/bin/env python3
"""v3 cost field: biome-edge-driven region growth (the measured winner from feature_detect.py).
Compares three growth rules on real patches and scores each by THE RIGHT metric:
  "what fraction of the resulting border sits ON a real feature (biome edge / shore)?"
That is the actual goal — a border that hugs features — not the weak proxy (mean slope) v1 used.

Rules:
  BFS    : today's unweighted multi-source BFS (all steps cost 1, land-only)
  V1     : slope-weighted Dijkstra (the falsified one; kept for contrast)
  V3     : edge-attracted Dijkstra. Cost to ENTER a cell is LOW if that cell is on a biome edge or
           shore (so the frontier is cheap to travel along features and the two regions meet there),
           HIGH in biome interiors (so borders avoid cutting through the middle of a biome).
"""
import struct, sys, math, heapq
from collections import deque

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
    if x>0: yield x-1,z
    if x<nx-1: yield x+1,z
    if z>0: yield x,z-1
    if z<nz-1: yield x,z+1

def slope_grid(H,step):
    nz=len(H);nx=len(H[0]);S=[[0.0]*nx for _ in range(nz)]
    for z in range(nz):
        for x in range(nx):
            x0=max(0,x-1);x1=min(nx-1,x+1);z0=max(0,z-1);z1=min(nz-1,z+1)
            S[z][x]=math.hypot((H[z][x1]-H[z][x0])/((x1-x0)*step or 1),(H[z1][x]-H[z0][x])/((z1-z0)*step or 1))
    return S

def biome_edge_set(P):
    """Land cells adjacent to a different land biome, OR adjacent to water (shore). The crisp feature."""
    nz=P['nz'];nx=P['nx'];B=P['B']; E=set(); shore=set()
    for z in range(nz):
        for x in range(nx):
            if water(B[z][x]): continue
            for a,b in nb(x,z,nx,nz):
                if water(B[b][a]): shore.add((x,z))
                elif B[b][a]!=B[z][x]: E.add((x,z))
    return E, shore

def grow_bfs(P,seeds):
    nz=P['nz'];nx=P['nx'];B=P['B'];o=[[-1]*nx for _ in range(nz)];dq=deque()
    for i,(x,z) in enumerate(seeds): o[z][x]=i; dq.append((x,z))
    while dq:
        x,z=dq.popleft();ow=o[z][x]
        for a,b in nb(x,z,nx,nz):
            if o[b][a]!=-1 or water(B[b][a]): continue
            o[b][a]=ow; dq.append((a,b))
    return o

def grow_dijkstra(P,seeds,cost):
    nz=P['nz'];nx=P['nx'];B=P['B'];o=[[-1]*nx for _ in range(nz)];dist=[[1e18]*nx for _ in range(nz)];pq=[]
    for i,(x,z) in enumerate(seeds): dist[z][x]=0;o[z][x]=i;heapq.heappush(pq,(0.0,x,z,i))
    while pq:
        d,x,z,ow=heapq.heappop(pq)
        if d>dist[z][x]: continue
        for a,b in nb(x,z,nx,nz):
            if water(B[b][a]): continue
            ndd=d+cost[b][a]
            if ndd<dist[b][a]: dist[b][a]=ndd;o[b][a]=ow;heapq.heappush(pq,(ndd,a,b,ow))
    return o

def cost_v1_slope(P,S):
    smax=max(max(r) for r in S) or 1
    return [[1.0+6.0*(S[z][x]/smax) for x in range(P['nx'])] for z in range(P['nz'])]

def cost_v3_edge(P, edgeset, shoreset):
    """v3 CORRECTED — features are BARRIERS, not highways. To make two regions MEET at a feature, the
    feature must be EXPENSIVE TO CROSS (a wall): each region's growth stalls at the feature and they
    collide there. (The earlier 'cheap edge' version was sign-inverted — cheap edges became highways
    the frontier raced along, growing regions ALONG edges instead of meeting at them.)
    Cost to ENTER a cell = high if it's a biome edge / shore (crossing the seam is costly), low in
    interiors (free to fill a biome's middle)."""
    nz=P['nz'];nx=P['nx'];C=[[1.0]*nx for _ in range(nz)]  # interior baseline = cheap to fill
    for z in range(nz):
        for x in range(nx):
            if (x,z) in edgeset: C[z][x]=12.0     # biome edge = expensive wall to cross
            elif (x,z) in shoreset: C[z][x]=8.0   # shore = wall (slightly cheaper)
    return C

def edges_of(o):
    nz=len(o);nx=len(o[0]);e=set()
    for z in range(nz):
        for x in range(nx):
            if o[z][x]<0: continue
            for a,b in nb(x,z,nx,nz):
                if o[b][a]!=o[z][x]: e.add((x,z)); break
    return e

def seeds_of(P,k=4,m=20):
    nz=P['nz'];nx=P['nx'];B=P['B']
    land=[(x,z) for z in range(m,nz-m) for x in range(m,nx-m) if not water(B[z][x])]
    s=[land[len(land)//2]]
    while len(s)<k:
        best=None;bd=-1
        for (x,z) in land[::7]:
            dm=min((x-a)**2+(z-b)**2 for a,b in s)
            if dm>bd: bd=dm;best=(x,z)
        s.append(best)
    return s

def feature_hug_pct(border, edgeset, shoreset, P, tol=1):
    """THE metric: fraction of border cells within `tol` of a biome edge or shore."""
    nx=P['nx'];nz=P['nz']
    feat=edgeset|shoreset
    if not border: return 0.0
    hit=0
    for (x,z) in border:
        on=False
        for dz in range(-tol,tol+1):
            for dx in range(-tol,tol+1):
                if (x+dx,z+dz) in feat: on=True;break
            if on: break
        if on: hit+=1
    return 100*hit/len(border)

def main():
    print(f"{'patch':28} {'rule':6} {'border':>7} {'on-feature%':>11}")
    for tag,p in [("A","A_mountain_coast_swamp"),("B","B_mistlands_six"),("C","C_starter_ring")]:
        P=load(f"/tmp/wz_oracle/patches/{p}.bin")
        S=slope_grid(P['H'],P['step'])
        E,SH=biome_edge_set(P)
        seeds=seeds_of(P)
        runs={
            "BFS": grow_bfs(P,seeds),
            "V1": grow_dijkstra(P,seeds,cost_v1_slope(P,S)),
            "V3": grow_dijkstra(P,seeds,cost_v3_edge(P,E,SH)),
        }
        for rule,o in runs.items():
            b=edges_of(o)
            pct=feature_hug_pct(b,E,SH,P)
            print(f"{p:28} {rule:6} {len(b):>7} {pct:>10.1f}%")
        print()

if __name__=="__main__": main()
