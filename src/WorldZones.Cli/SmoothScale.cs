using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-30) — the SMOOTHING-SCALE dial. The contour trace proved the boundary is a
    /// continuous curve on the real biome edge, but the raw edge is Perlin-noisy ("hairy"). Daniel: "this
    /// is where our smoothing functions should apply." This renders the SAME Askaadal|Blackhold seam at a
    /// spectrum of filter strengths so the eye picks where noise→organic→over-rounded.
    ///
    /// The knob is a GAUSSIAN low-pass along the curve's ARC LENGTH, parameterised in METRES (σ): a point
    /// is averaged with its neighbours weighted by a Gaussian of width σ. Physical meaning: wiggles smaller
    /// than ~σ m are smoothed away (pixel/Perlin noise), features bigger than ~σ m survive (real headlands).
    /// This is the honest "filter at the scale of real terrain, not the scale of the grid" knob — NOT a
    /// number of Chaikin passes (that's a grid-flavoured proxy). Junction endpoints are PINNED so borders
    /// still meet. Panels: raw trace (σ=0) → σ=8 → σ=16 → σ=32 m, over the continuous biome backdrop so
    /// over-smoothing is visible as the line departing the colour transition (the banned "invented curve").
    /// </summary>
    public static class SmoothScale
    {
        const double Resample = 4.0, SnapReach = 56.0, MarchStep = 3.0, Uniform = 2.0;

        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== SMOOTHING-SCALE dial — seed '{seed}' (raw / 8 / 16 / 32 m) ===\n");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            { IncludeInlandWater=false, UseFeatureAwareBorders=true, ComputeRegionInfo=true, Namer=new MultiSchemaRegionNamer() });
            int[,] rid=world.RegionIdGrid; int min=world.Grid.MinIndex;

            var idToKey=new Dictionary<int,string>();
            foreach(ProtoRegion r in world.ProtoResult.Regions) if(!idToKey.ContainsKey(r.Id)) idToKey[r.Id]=r.RegionKey;
            RegionBoundaryGraph graph=RegionBoundaryExtractor.Extract(rid,min,idToKey);
            var biomeField=new BiomeCategoryField(sampler);

            // same picker as the contour trace → same seam Daniel just saw
            var pairHug=new Dictionary<(string,string),double>(); var pairLen=new Dictionary<(string,string),double>();
            foreach(BorderSegment s in graph.Segments){ if(s.IsCoastline)continue; var key=(s.KeyA,s.KeyB);
              double mx=(s.A.X+s.B.X)*.5,mz=(s.A.Z+s.B.Z)*.5,dx=s.B.X-s.A.X,dz=s.B.Z-s.A.Z,l=Math.Sqrt(dx*dx+dz*dz); if(l<1e-9)continue;
              double nx=-dz/l,nz=dx/l; pairLen[key]=pairLen.GetValueOrDefault(key)+l;
              int c0=biomeField.CategoryAt(mx,mz); bool flip=false;
              foreach(int dir in new[]{1,-1}){int prev=c0;for(double t=MarchStep;t<=SnapReach;t+=MarchStep){int c=biomeField.CategoryAt(mx+dir*t*nx,mz+dir*t*nz);if(c!=prev){flip=true;break;}prev=c;}if(flip)break;}
              if(flip)pairHug[key]=pairHug.GetValueOrDefault(key)+l; }
            var pick=pairHug.Where(kv=>pairLen[kv.Key]>=400).OrderByDescending(kv=>kv.Value).First().Key;
            var ria=world.Regions.FirstOrDefault(r=>r.RegionKey==pick.Item1); var rib=world.Regions.FirstOrDefault(r=>r.RegionKey==pick.Item2);
            Console.WriteLine($"pair: {ria?.Name??pick.Item1} | {rib?.Name??pick.Item2}  (seam {pairLen[pick]:F0} m)\n");

            var segs=graph.Segments.Where(s=>!s.IsCoastline&&s.KeyA==pick.Item1&&s.KeyB==pick.Item2).ToList();
            var chains=Chain(segs).Where(c=>c.Count>=2).ToList();

            // trace each chain to the raw (despiked, UNsmoothed) contour once
            var rawTraced = chains.Select(c=>(IReadOnlyList<WzVec2>)Snap(c,biomeField)).ToList();

            // three filter strengths, 2× geometric sweep (metres) — light / medium / heavy. The raw hairy
            // trace Daniel already saw is σ→0; these bracket the noise→organic→over-rounded transition.
            double[] sigmas = { 10, 20, 40 };
            var variants = sigmas.Select(sg => rawTraced.Select(p => GaussianSmooth(p, sg)).ToList()).ToList();

            // shared window
            double x0=double.MaxValue,z0=double.MaxValue,x1=double.MinValue,z1=double.MinValue;
            foreach(var c in chains)foreach(var p in c){x0=Math.Min(x0,p.X);x1=Math.Max(x1,p.X);z0=Math.Min(z0,p.Z);z1=Math.Max(z1,p.Z);}
            double pad=90;x0-=pad;z0-=pad;x1+=pad;z1+=pad;

            Render(outDir,seed,sampler,x0,z0,x1,z1,variants,sigmas);
            return 0;
        }

        // snap dense points to nearest biome flip (continuous, sub-metre via bisection), despike spurs.
        static List<WzVec2> Snap(List<WzVec2> chain, ICategoryField field)
        {
            var dense=new List<WzVec2>();
            for(int i=1;i<chain.Count;i++){ WzVec2 a=chain[i-1],b=chain[i]; double dx=b.X-a.X,dz=b.Z-a.Z,len=Math.Sqrt(dx*dx+dz*dz);
              int steps=Math.Max(1,(int)(len/Resample)); for(int s=0;s<steps;s++){double t=(double)s/steps;dense.Add(new WzVec2(a.X+t*dx,a.Z+t*dz));} }
            dense.Add(chain[^1]);
            var snapped=new List<WzVec2>(dense.Count);
            for(int i=0;i<dense.Count;i++){ WzVec2 prev=dense[Math.Max(0,i-1)],next=dense[Math.Min(dense.Count-1,i+1)];
              double tx=next.X-prev.X,tz=next.Z-prev.Z,tl=Math.Sqrt(tx*tx+tz*tz); if(tl<1e-9){snapped.Add(dense[i]);continue;}
              double nx=-tz/tl,nz=tx/tl; if(TrySnap(dense[i].X,dense[i].Z,nx,nz,field,out double s)) snapped.Add(new WzVec2(dense[i].X+s*nx,dense[i].Z+s*nz)); else snapped.Add(dense[i]); }
            return new List<WzVec2>(PolylineSmoother.Despike(snapped,18.0));
        }

        // arc-length Gaussian low-pass, σ in metres. Endpoints pinned (junctions meet). σ=0 → identity.
        static IReadOnlyList<WzVec2> GaussianSmooth(IReadOnlyList<WzVec2> poly, double sigma)
        {
            if(sigma<=0||poly.Count<3) return poly;
            var uni=ResampleUniform(poly,Uniform); int n=uni.Count; if(n<3) return poly;
            int half=(int)Math.Ceiling(3*sigma/Uniform);
            var k=new double[2*half+1]; double ksum=0;
            for(int j=-half;j<=half;j++){ double d=j*Uniform; double w=Math.Exp(-d*d/(2*sigma*sigma)); k[j+half]=w; ksum+=w; }
            var outp=new List<WzVec2>(n);
            for(int i=0;i<n;i++){
                if(i==0||i==n-1){ outp.Add(uni[i]); continue; }
                double sx=0,sz=0,wsum=0;
                for(int j=-half;j<=half;j++){ int q=i+j; if(q<0||q>=n)continue; double w=k[j+half]; sx+=uni[q].X*w; sz+=uni[q].Z*w; wsum+=w; }
                outp.Add(new WzVec2(sx/wsum,sz/wsum));
            }
            return outp;
        }

        static List<WzVec2> ResampleUniform(IReadOnlyList<WzVec2> poly, double step)
        {
            var outp=new List<WzVec2>{ poly[0] }; double acc=0;
            for(int i=1;i<poly.Count;i++){ WzVec2 a=poly[i-1],b=poly[i]; double dx=b.X-a.X,dz=b.Z-a.Z,seg=Math.Sqrt(dx*dx+dz*dz); if(seg<1e-9)continue;
              double pos=0; while(acc+seg-pos>=step){ double need=step-acc; pos+=need; double t=pos/seg; outp.Add(new WzVec2(a.X+t*dx,a.Z+t*dz)); acc=0; }
              acc+=seg-pos; }
            outp.Add(poly[^1]); return outp;
        }

        static bool TrySnap(double px,double pz,double nx,double nz,ICategoryField field,out double bestS)
        { bestS=0; int c0=field.CategoryAt(px,pz); double best=double.MaxValue; bool found=false;
          foreach(int dir in new[]{1,-1}){ int prev=c0; double prevS=0;
            for(double step=MarchStep;step<=SnapReach+1e-9;step+=MarchStep){ double s=dir*step; int c=field.CategoryAt(px+s*nx,pz+s*nz);
              if(c!=prev){ double lo=prevS,hi=s; for(int it=0;it<20;it++){double mid=.5*(lo+hi);int cm=field.CategoryAt(px+mid*nx,pz+mid*nz);if(cm==c0)lo=mid;else hi=mid;} double sc=.5*(lo+hi); if(Math.Abs(sc)<best){best=Math.Abs(sc);bestS=sc;found=true;} break; } prev=c; prevS=s; } }
          return found; }

        static void Render(string outDir,string seed,IWorldSampler sampler,double x0,double z0,double x1,double z1,
            List<List<IReadOnlyList<WzVec2>>> variants, double[] sigmas)
        {
            double spanX=x1-x0,spanZ=z1-z0,span=Math.Max(spanX,spanZ);
            int target=480; double scale=target/span;
            int pw=(int)Math.Ceiling(spanX*scale), ph=(int)Math.Ceiling(spanZ*scale), gap=18;
            int P=variants.Count, W=pw*P+gap*(P-1), H=ph; byte[] img=new byte[W*H*3]; for(int i=0;i<img.Length;i++)img[i]=16;

            for(int v=0;v<P;v++)
            {
                int xoff=v*(pw+gap);
                for(int py=0;py<ph;py++)for(int px=0;px<pw;px++){ double wx=x0+(px+0.5)/scale,wz=z1-(py+0.5)/scale; double h=sampler.GetHeight((float)wx,(float)wz);
                  (byte r,byte g,byte b) c=h<HeightScalarField.SeaLevel?((byte)40,(byte)60,(byte)98):BiomeCol(sampler.GetBiome((float)wx,(float)wz));
                  int o=(py*W+xoff+px)*3; img[o]=c.r;img[o+1]=c.g;img[o+2]=c.b; }
                foreach(var poly in variants[v]) for(int i=1;i<poly.Count;i++){ var p0=Px(poly[i-1],x0,z1,scale); var p1=Px(poly[i],x0,z1,scale);
                  Line(img,W,H,xoff,pw,ph,p0,p1,(245,245,245),3); Line(img,W,H,xoff,pw,ph,p0,p1,(14,14,14),1); }
            }
            for(int v=0;v<P-1;v++) for(int y=0;y<H;y++) for(int x=pw+v*(pw+gap);x<pw+v*(pw+gap)+gap;x++){int o=(y*W+x)*3;img[o]=10;img[o+1]=11;img[o+2]=14;}

            string p=Path.Combine(outDir,$"{seed}_smoothscale.png");
            PngWriter.Write(p,W,H,img);
            Console.WriteLine($"Wrote {p} ({W}x{H})  panels: " + string.Join(" / ", sigmas.Select(s=>s==0?"raw":$"σ={s:F0}m")));
        }

        static (int x,int y) Px(WzVec2 v,double x0,double z1,double scale)=>((int)Math.Round((v.X-x0)*scale),(int)Math.Round((z1-v.Z)*scale));
        static void Line(byte[] img,int W,int H,int xoff,int pw,int ph,(int x,int y)a,(int x,int y)b,(int r,int g,int bl)c,int rad)
        { int x0=a.x,y0=a.y,x1=b.x,y1=b.y,dx=Math.Abs(x1-x0),dy=Math.Abs(y1-y0),sx=x0<x1?1:-1,sy=y0<y1?1:-1,err=dx-dy;
          while(true){ for(int oy=-rad;oy<=rad;oy++)for(int ox=-rad;ox<=rad;ox++){ if(ox*ox+oy*oy>rad*rad+1)continue; int X=x0+ox,Y=y0+oy; if(X<0||Y<0||X>=pw||Y>=ph)continue; int o=((Y)*W+(xoff+X))*3; img[o]=(byte)c.r;img[o+1]=(byte)c.g;img[o+2]=(byte)c.bl; }
            if(x0==x1&&y0==y1)break; int e2=2*err; if(e2>-dy){err-=dy;x0+=sx;} if(e2<dx){err+=dx;y0+=sy;} } }
        static (byte,byte,byte) BiomeCol(BiomeType b)=>b switch{
            BiomeType.Meadows=>(96,124,64), BiomeType.Swamp=>(84,80,54), BiomeType.Mountain=>(188,192,200),
            BiomeType.BlackForest=>(52,84,60), BiomeType.Plains=>(164,150,88), BiomeType.AshLands=>(138,64,50),
            BiomeType.DeepNorth=>(200,214,226), BiomeType.Mistlands=>(104,90,114), _=>(70,70,76) };

        static IEnumerable<List<WzVec2>> Chain(List<BorderSegment> segs)
        { (long,long) K(WzVec2 p)=>((long)Math.Round(p.X*100),(long)Math.Round(p.Z*100));
          var adj=new Dictionary<(long,long),List<int>>(); var used=new bool[segs.Count];
          for(int i=0;i<segs.Count;i++)foreach(var p in new[]{segs[i].A,segs[i].B}){if(!adj.TryGetValue(K(p),out var l)){l=new List<int>();adj[K(p)]=l;}l.Add(i);}
          for(int s=0;s<segs.Count;s++){ if(used[s])continue; used[s]=true; var ch=new LinkedList<WzVec2>(); ch.AddLast(segs[s].A); ch.AddLast(segs[s].B);
            Ext(ch,adj,segs,used,true,K); Ext(ch,adj,segs,used,false,K); yield return new List<WzVec2>(ch); }
          static void Ext(LinkedList<WzVec2> ch,Dictionary<(long,long),List<int>> adj,List<BorderSegment> segs,bool[] used,bool tail,Func<WzVec2,(long,long)> K)
          { while(true){ WzVec2 e=tail?ch.Last.Value:ch.First.Value; if(!adj.TryGetValue(K(e),out var c))break; int nx=-1; foreach(int si in c)if(!used[si]){nx=si;break;} if(nx<0)break; used[nx]=true;
            WzVec2 a=segs[nx].A,b=segs[nx].B; WzVec2 far=(K(a)==K(e))?b:a; if(tail)ch.AddLast(far);else ch.AddFirst(far); } } }
    }
}
