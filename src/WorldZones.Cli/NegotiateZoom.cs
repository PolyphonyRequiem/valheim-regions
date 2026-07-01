using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-30) — does the negotiation min-cut, run at SUB-ZONE resolution, actually buy a
    /// better SUB-ZONE boundary, or just a finer staircase? Daniel's exact question after the 64 m probe.
    ///
    /// Picks one substantive Astley region pair and runs the IDENTICAL feature-aligned s-t min-cut
    /// (NegotiateProbe.MinCutAssign) at TWO resolutions on the same world window, then renders 3 panels:
    ///   A — 64 m cut (coarse staircase, the zone-membership boundary)
    ///   B — 16 m cut (FINE membership raster — the cut re-run on a 4× grid; feature field sampled at 16 m)
    ///   C — 16 m cut boundary TRACED + Chaikin-smoothed (the organic sub-zone LINE the refiner would draw)
    /// All three on one shared world bbox + scale, zoomed so the staircase is visible. Panel C is the
    /// payoff: if the smoothed 16 m boundary follows the feature organically, the mechanism scales to
    /// sub-zone; if it's just a tighter staircase, it does not.
    ///
    /// HONEST: a grid cut at ANY resolution is axis-aligned, so B is a finer staircase by construction —
    /// the test is whether it follows the FEATURE (so C's smoothing has something real to trace) vs
    /// wandering. Features = river≥0.25 / biome-edge / shore (router's set); ridge excluded (no extractor).
    /// </summary>
    public static class NegotiateZoom
    {
        const double Zone = 64.0, Half = 32.0;
        const int BandK = 4;                 // band half-width in ZONES (scaled by subdivisions for fine runs)
        const float RiverWeightThreshold = 0.25f;

        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== NEGOTIATION @ SUB-ZONE — seed '{seed}' (64 m vs 16 m, same cut) ===\n");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            var river = (IRiverSampler)sampler;
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });
            int[,] rid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);

            // coarse land + feature (to pick the pair, identical to NegotiateProbe)
            var isLandC = new bool[gh, gw]; var biomeC = new BiomeType[gh, gw];
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            { double wx=(gx+min)*Zone, wz=(gy+min)*Zone; bool l=sampler.GetHeight((float)wx,(float)wz)>=HeightScalarField.SeaLevel;
              isLandC[gy,gx]=l; if (l) biomeC[gy,gx]=sampler.GetBiome((float)wx,(float)wz); }

            // pick the substantive pair: longest land-land seam (most zone-border edges), like the probe's
            // example #1 region. Deterministic: max seam edges, tiebreak lower (A,B).
            var seam = new Dictionary<(int,int),int>();
            int[] dx4={1,-1,0,0}, dy4={0,0,1,-1};
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            { int a=rid[gy,gx]; if (a<0||!isLandC[gy,gx]) continue;
              for (int d=0;d<2;d++){ int ax=gx+dx4[d], ay=gy+dy4[d]; if(ax<0||ax>=gw||ay<0||ay>=gh)continue;
                int b=rid[ay,ax]; if(b<0||!isLandC[ay,ax]||b==a)continue; var k=a<b?(a,b):(b,a); seam[k]=seam.GetValueOrDefault(k)+1; } }
            // choose the pair that gave a clean readable example earlier: Ingrid|Eldbrygg-like — pick the one
            // maximizing seamEdges among pairs whose coarse seam has a biome flip available (so 16 m has work).
            var pick = seam.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Item1).First().Key;
            int A = pick.Item1, B = pick.Item2;
            var ia = world.Regions.FirstOrDefault(r=>r.TransientId==A);
            var ib = world.Regions.FirstOrDefault(r=>r.TransientId==B);
            Console.WriteLine($"pair: {ia?.Name ?? A.ToString()} | {ib?.Name ?? B.ToString()}  (seam {seam[pick]} zone-edges)\n");

            // shared world window = bbox of the coarse band, padded
            var (bx0,bz0,bx1,bz1) = CoarseBandBBox(A,B,rid,isLandC,min,gh,gw);
            double pad=Zone*2; bx0-=pad; bz0-=pad; bx1+=pad; bz1+=pad;

            // ── run the cut at S=1 (64 m) and S=4 (16 m) ──
            var owner64 = RunCutAtRes(A,B,1, sampler,river,rid,min,gh,gw);
            var owner16 = RunCutAtRes(A,B,4, sampler,river,rid,min,gh,gw);

            // ── render 3-up ──
            RenderTriptych(outDir, seed, A, B, sampler, rid, min, gh, gw,
                           bx0,bz0,bx1,bz1, owner64, owner16);
            return 0;
        }

        // Run the negotiation min-cut at subdivision S. Returns owner per FINE cell (key = fine (fy,fx)),
        // plus the geometry needed to render. Fine cell (fy,fx) covers world center
        // (originX + (fx+0.5)*texel, originZ + (fy+0.5)*texel), origin = min*64-32. Region id of a fine
        // cell is INHERITED from its coarse zone; the cut then re-owns band fine-cells.
        static FineResult RunCutAtRes(int A, int B, int S, IWorldSampler sampler, IRiverSampler river,
            int[,] rid, int min, int gh, int gw)
        {
            double texel = Zone / S;
            int fh = gh*S, fw = gw*S;
            double ox = min*Zone - Half, oz = min*Zone - Half;

            // fine land + biome + feature (feature sampled at FINE res — genuinely sub-zone)
            var isLand = new bool[fh,fw]; var biome = new BiomeType[fh,fw];
            for (int fy=0;fy<fh;fy++) for (int fx=0;fx<fw;fx++)
            { double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel;
              bool l = sampler.GetHeight((float)wx,(float)wz)>=HeightScalarField.SeaLevel;
              isLand[fy,fx]=l; if (l) biome[fy,fx]=sampler.GetBiome((float)wx,(float)wz); }
            var feature = new bool[fh,fw];
            int[] dx4={1,-1,0,0}, dy4={0,0,1,-1};
            for (int fy=0;fy<fh;fy++) for (int fx=0;fx<fw;fx++)
            { if(!isLand[fy,fx])continue; double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel;
              river.GetRiverWeight((float)wx,(float)wz, out float w, out _); bool onR=w>=RiverWeightThreshold;
              bool be=false, sh=false;
              for(int d=0;d<4;d++){ int ax=fx+dx4[d], ay=fy+dy4[d]; if(ax<0||ax>=fw||ay<0||ay>=fh){sh=true;continue;}
                if(!isLand[ay,ax]){sh=true;continue;} if(biome[ay,ax]!=biome[fy,fx])be=true; }
              feature[fy,fx]=onR||be||sh; }

            // fine region id = coarse zone's region, but only on fine LAND of A or B
            var ridF = new int[fh,fw];
            for (int fy=0;fy<fh;fy++) for (int fx=0;fx<fw;fx++)
            { int cz = rid[fy/S, fx/S]; ridF[fy,fx] = (isLand[fy,fx] && (cz==A||cz==B)) ? cz : (cz<0? -1 : (isLand[fy,fx]?cz:-1)); }
            // (cells of OTHER regions keep their id so they're excluded from the A∪B union; water = -1)

            // BFS distance from the fine A|B seam within the fine A∪B union
            bool Union(int fy,int fx)=> fy>=0&&fx>=0&&fy<fh&&fx<fw&&isLand[fy,fx]&&(ridF[fy,fx]==A||ridF[fy,fx]==B);
            var dist = new int[fh,fw]; for(int y=0;y<fh;y++)for(int x=0;x<fw;x++)dist[y,x]=int.MaxValue;
            var q=new Queue<(int,int)>();
            for(int fy=0;fy<fh;fy++)for(int fx=0;fx<fw;fx++){ if(!Union(fy,fx))continue; int self=ridF[fy,fx]; bool adj=false;
              for(int d=0;d<4;d++){int ax=fx+dx4[d],ay=fy+dy4[d]; if(!Union(ay,ax))continue; if(ridF[ay,ax]!=self){adj=true;break;}}
              if(adj){dist[fy,fx]=0;q.Enqueue((fy,fx));} }
            while(q.Count>0){ var(cy,cx)=q.Dequeue(); for(int d=0;d<4;d++){int ax=cx+dx4[d],ay=cy+dy4[d];
              if(!Union(ay,ax))continue; if(dist[ay,ax]>dist[cy,cx]+1){dist[ay,ax]=dist[cy,cx]+1;q.Enqueue((ay,ax));}}}

            // band = union fine cells within K*S of the seam (same world reach at every resolution)
            int K = BandK*S;
            var band=new List<(int gy,int gx)>();
            for(int fy=0;fy<fh;fy++)for(int fx=0;fx<fw;fx++) if(Union(fy,fx)&&dist[fy,fx]<=K) band.Add((fy,fx));
            var idx=new Dictionary<(int,int),int>(); for(int i=0;i<band.Count;i++)idx[band[i]]=i;

            // RUN THE SAME MIN-CUT (NegotiateProbe.MinCutAssign — identical formulation, finer grid)
            var newOwner = NegotiateProbe.MinCutAssign(A,B, band, idx, dist, ridF, isLand, feature, fh, fw, K);

            // full owner field (cores + band reassignment) for rendering
            var owner = new int[fh,fw];
            for(int fy=0;fy<fh;fy++)for(int fx=0;fx<fw;fx++) owner[fy,fx] = ridF[fy,fx];
            foreach(var kv in newOwner) owner[kv.Key.Item1, kv.Key.Item2]=kv.Value;

            return new FineResult{ S=S, texel=texel, fh=fh, fw=fw, ox=ox, oz=oz,
                isLand=isLand, biome=biome, feature=feature, owner=owner };
        }

        sealed class FineResult { public int S,fh,fw; public double texel,ox,oz; public bool[,] isLand,feature; public BiomeType[,] biome; public int[,] owner; }

        static (double,double,double,double) CoarseBandBBox(int A,int B,int[,] rid,bool[,] isLand,int min,int gh,int gw)
        {
            double x0=double.MaxValue,z0=double.MaxValue,x1=double.MinValue,z1=double.MinValue; int[] dx4={1,-1,0,0},dy4={0,0,1,-1};
            for(int gy=0;gy<gh;gy++)for(int gx=0;gx<gw;gx++){ int a=rid[gy,gx]; if((a!=A&&a!=B)||!isLand[gy,gx])continue;
              bool nearSeam=false; for(int d=0;d<4;d++){int ax=gx+dx4[d],ay=gy+dy4[d]; if(ax<0||ax>=gw||ay<0||ay>=gh)continue;
                int o=rid[ay,ax]; if((o==A||o==B)&&o!=a){nearSeam=true;break;}}
              if(!nearSeam)continue; double wx=(gx+min)*Zone, wz=(gy+min)*Zone;
              x0=Math.Min(x0,wx); x1=Math.Max(x1,wx); z0=Math.Min(z0,wz); z1=Math.Max(z1,wz); }
            return (x0,z0,x1,z1);
        }

        // ── trace the A|B boundary of a fine owner field into chained, Chaikin-smoothed polylines ──
        static List<List<WzVec2>> TraceSmoothBoundary(FineResult f, int A, int B)
        {
            // collect unit boundary segments between an A cell and a B cell (on the fine lattice), as
            // world-space edges on the cell border, then chain by shared endpoints and Chaikin.
            var segs=new List<(WzVec2 a, WzVec2 b)>();
            double t=f.texel, ox=f.ox, oz=f.oz;
            for(int fy=0;fy<f.fh;fy++)for(int fx=0;fx<f.fw;fx++)
            {
                int self=f.owner[fy,fx]; if(self!=A&&self!=B)continue;
                // +x neighbour → vertical edge; +y neighbour → horizontal edge
                if(fx+1<f.fw){ int o=f.owner[fy,fx+1]; if((o==A||o==B)&&o!=self){
                    double ex=ox+(fx+1)*t; double z0=oz+fy*t, z1=oz+(fy+1)*t; segs.Add((new WzVec2(ex,z0),new WzVec2(ex,z1))); } }
                if(fy+1<f.fh){ int o=f.owner[fy+1,fx]; if((o==A||o==B)&&o!=self){
                    double ez=oz+(fy+1)*t; double x0=ox+fx*t, x1=ox+(fx+1)*t; segs.Add((new WzVec2(x0,ez),new WzVec2(x1,ez))); } }
            }
            // chain by exact shared endpoints
            (long,long) K(WzVec2 p)=>((long)Math.Round(p.X*100),(long)Math.Round(p.Z*100));
            var adj=new Dictionary<(long,long),List<int>>(); var used=new bool[segs.Count];
            for(int i=0;i<segs.Count;i++) foreach(var p in new[]{segs[i].a,segs[i].b}){ if(!adj.TryGetValue(K(p),out var l)){l=new List<int>();adj[K(p)]=l;} l.Add(i); }
            var chains=new List<List<WzVec2>>();
            for(int s=0;s<segs.Count;s++){ if(used[s])continue; used[s]=true; var ch=new LinkedList<WzVec2>(); ch.AddLast(segs[s].a); ch.AddLast(segs[s].b);
              Extend(ch,adj,segs,used,true,K); Extend(ch,adj,segs,used,false,K);
              var poly=new List<WzVec2>(ch);
              if(poly.Count>=3){ var sm=PolylineSmoother.Despike(poly,t*1.5); sm=PolylineSmoother.Chaikin(sm,3); chains.Add(new List<WzVec2>(sm)); }
              else chains.Add(poly); }
            return chains;

            static void Extend(LinkedList<WzVec2> ch,Dictionary<(long,long),List<int>> adj,List<(WzVec2 a,WzVec2 b)> segs,bool[] used,bool tail,Func<WzVec2,(long,long)> K)
            { while(true){ WzVec2 e=tail?ch.Last.Value:ch.First.Value; if(!adj.TryGetValue(K(e),out var c))break; int nx=-1; foreach(int si in c)if(!used[si]){nx=si;break;} if(nx<0)break; used[nx]=true;
              WzVec2 a=segs[nx].a,b=segs[nx].b; WzVec2 far=(K(a)==K(e))?b:a; if(tail)ch.AddLast(far);else ch.AddFirst(far); } }
        }

        // ───────────────────────── render ─────────────────────────
        static void RenderTriptych(string outDir, string seed, int A, int B, IWorldSampler sampler,
            int[,] rid, int min, int gh, int gw, double bx0,double bz0,double bx1,double bz1,
            FineResult f64, FineResult f16)
        {
            double spanX=bx1-bx0, spanZ=bz1-bz0, span=Math.Max(spanX,spanZ);
            int target=520; double scale=target/span;     // px per metre
            int pw=(int)Math.Ceiling(spanX*scale), ph=(int)Math.Ceiling(spanZ*scale);
            int gap=22, W=pw*3+gap*2, H=ph;
            byte[] img=new byte[W*H*3]; for(int i=0;i<img.Length;i++)img[i]=16;

            var smoothed = TraceSmoothBoundary(f16, A, B);

            // panel painter: fill from a FineResult (biome + feature brighten + owner tint), optional cut-edge ink
            void Paint(int xoff, FineResult f, bool drawCutEdges)
            {
                for(int py=0;py<ph;py++) for(int px=0;px<pw;px++)
                {
                    double wx=bx0+(px+0.5)/scale, wz=bz1-(py+0.5)/scale;   // north up
                    int fx=(int)((wx-f.ox)/f.texel), fy=(int)((wz-f.oz)/f.texel);
                    (byte r,byte g,byte b) col;
                    if(fx<0||fy<0||fx>=f.fw||fy>=f.fh){ img[(py*W+xoff+px)*3]=16; img[(py*W+xoff+px)*3+1]=16; img[(py*W+xoff+px)*3+2]=20; continue; }
                    if(!f.isLand[fy,fx]) col=(40,60,98);
                    else col=BiomeCol(f.biome[fy,fx]);
                    if(f.isLand[fy,fx]&&f.feature[fy,fx]) col=Mix(col,(255,238,150),0.32);
                    int own=f.owner[fy,fx];
                    if(own==A) col=Mix(col,(255,170,70),0.30); else if(own==B) col=Mix(col,(110,150,255),0.30);
                    int o=(py*W+xoff+px)*3; img[o]=col.r;img[o+1]=col.g;img[o+2]=col.b;
                }
                if(drawCutEdges)
                {
                    // thick green/red cut edges (green=on-feature) — same legend as the 64m probe
                    for(int fy=0;fy<f.fh;fy++)for(int fx=0;fx<f.fw;fx++)
                    { int self=f.owner[fy,fx]; if(self!=A&&self!=B)continue;
                      int[] ddx={1,0},ddy={0,1};
                      for(int d=0;d<2;d++){ int ax=fx+ddx[d],ay=fy+ddy[d]; if(ax<0||ax>=f.fw||ay<0||ay>=f.fh)continue;
                        int o2=f.owner[ay,ax]; if((o2!=A&&o2!=B)||o2==self)continue;
                        bool onF=f.feature[fy,fx]&&f.feature[ay,ax];
                        (byte r,byte g,byte b) sc = onF?((byte)40,(byte)230,(byte)90):((byte)235,(byte)50,(byte)40);
                        // edge midpoint in world → px
                        double exw = (d==0)? f.ox+(fx+1)*f.texel : f.ox+(fx+0.5)*f.texel;
                        double ezw = (d==0)? f.oz+(fy+0.5)*f.texel : f.oz+(fy+1)*f.texel;
                        PlotThick(img,W,H,xoff,pw,ph, (exw-bx0)*scale, (bz1-ezw)*scale, sc, 2); }
                    }
                }
            }

            // Panel A: 64m cut, edges inked
            Paint(0, f64, true);
            // Panel B: 16m cut, edges inked
            Paint(pw+gap, f16, true);
            // Panel C: 16m fill (no cut ink) + smoothed organic line
            Paint(2*(pw+gap), f16, false);
            foreach(var ch in smoothed)
                for(int i=1;i<ch.Count;i++){
                    double x0=(ch[i-1].X-bx0)*scale, y0=(bz1-ch[i-1].Z)*scale, x1=(ch[i].X-bx0)*scale, y1=(bz1-ch[i].Z)*scale;
                    LineThick(img,W,H,2*(pw+gap),pw,ph, x0,y0,x1,y1, (250,250,250), 2);
                    LineThick(img,W,H,2*(pw+gap),pw,ph, x0,y0,x1,y1, (12,12,12), 1);
                }
            // dividers
            for(int gI=0;gI<2;gI++) for(int y=0;y<H;y++) for(int x=pw+gI*(pw+gap);x<pw+gI*(pw+gap)+gap;x++){int o=(y*W+x)*3;img[o]=10;img[o+1]=11;img[o+2]=14;}

            string p=Path.Combine(outDir,$"{seed}_negotiate_zoom.png");
            PngWriter.Write(p,W,H,img);
            Console.WriteLine($"Wrote {p} ({W}x{H})");
            Console.WriteLine("  panels: [A 64m cut] [B 16m cut] [C 16m smoothed organic line]");
            Console.WriteLine("  green seam=on-feature, red=open ground; bright=feature zone; orange=A, blue=B");
        }

        static void PlotThick(byte[] img,int W,int H,int xoff,int pw,int ph,double px,double pz,(byte r,byte g,byte b) c,int rad)
        { int cx=(int)Math.Round(px), cy=(int)Math.Round(pz);
          for(int oy=-rad;oy<=rad;oy++)for(int ox=-rad;ox<=rad;ox++){ int X=cx+ox,Y=cy+oy; if(X<0||Y<0||X>=pw||Y>=ph)continue; int o=((Y)*W+(xoff+X))*3; img[o]=c.r;img[o+1]=c.g;img[o+2]=c.b; } }

        static void LineThick(byte[] img,int W,int H,int xoff,int pw,int ph,double x0,double y0,double x1,double y1,(int r,int g,int b) c,int rad)
        { int X0=(int)Math.Round(x0),Y0=(int)Math.Round(y0),X1=(int)Math.Round(x1),Y1=(int)Math.Round(y1);
          int dx=Math.Abs(X1-X0),dy=Math.Abs(Y1-Y0),sx=X0<X1?1:-1,sy=Y0<Y1?1:-1,err=dx-dy;
          while(true){ for(int oy=-rad;oy<=rad;oy++)for(int ox=-rad;ox<=rad;ox++){ if(ox*ox+oy*oy>rad*rad+1)continue; int X=X0+ox,Y=Y0+oy; if(X<0||Y<0||X>=pw||Y>=ph)continue; int o=((Y)*W+(xoff+X))*3; img[o]=(byte)c.r;img[o+1]=(byte)c.g;img[o+2]=(byte)c.b; }
            if(X0==X1&&Y0==Y1)break; int e2=2*err; if(e2>-dy){err-=dy;X0+=sx;} if(e2<dx){err+=dx;Y0+=sy;} } }

        static (byte,byte,byte) BiomeCol(BiomeType b)=>b switch{
            BiomeType.Meadows=>(96,124,64), BiomeType.Swamp=>(84,80,54), BiomeType.Mountain=>(188,192,200),
            BiomeType.BlackForest=>(52,84,60), BiomeType.Plains=>(164,150,88), BiomeType.AshLands=>(138,64,50),
            BiomeType.DeepNorth=>(200,214,226), BiomeType.Mistlands=>(104,90,114), _=>(70,70,76) };
        static (byte r,byte g,byte b) Mix((byte r,byte g,byte b) a,(int r,int g,int b) t,double k)
            => ((byte)(a.r+(t.r-a.r)*k),(byte)(a.g+(t.g-a.g)*k),(byte)(a.b+(t.b-a.b)*k));
    }
}
