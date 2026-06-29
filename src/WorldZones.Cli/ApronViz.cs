using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// DESIGN EXPLORATION (2026-06-28): render the "spongy coastal apron" boundary several ways on real
    /// Niflheim so Daniel can SEE which cost model carves the truest region-extent into the water, instead
    /// of arguing it abstractly. NOT shipped — emits labelled PNGs only. Each variant floods region claim
    /// outward from the COAST across water; differs only in what stops the flood:
    ///   A distance — apron = within R metres of land (the dumb buffer, baseline).
    ///   B depth    — apron = water shallower than D metres (terrain stops it, distance-blind).
    ///   C cost     — accumulated cost (cheap shallow, steep deep) under one flat budget — the sponge.
    ///   D perRegion— same cost flood, budget scales with region land mass — big regions sprawl, isles wet feet.
    /// Deep open water past every budget stays UNCLAIMED (black) — frontier, by design. Flood seeds = the
    /// region-id grid coast (detailed enough at 16 m to keep the macro 64 m lattice solver-only).
    /// </summary>
    public static class ApronViz
    {
        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Apron variants — seed '{seed}' (4 cost models, real Niflheim) ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });
            int[,] rid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            const double zone = 64.0, cell = 16.0; int F = (int)(zone / cell); // 4× finer than zones
            int W = gw * F, H = gh * F;
            double ox = min * zone - 32.0, oz = min * zone - 32.0;
            int maxLabel = -1; foreach (var r in world.Regions) if (r.TransientId > maxLabel) maxLabel = r.TransientId;
            var biomeOf = new BiomeType[maxLabel + 1];
            var landMass = new int[maxLabel + 1];
            foreach (var r in world.Regions) if (r.TransientId >= 0) biomeOf[r.TransientId] = r.DominantBiome;

            // depth + region grid at 16m
            var depth = new double[H, W]; var rgrid = new int[H, W];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
                double wx = ox + (x + 0.5) * cell, wz = oz + (y + 0.5) * cell;
                double hm = sampler.GetHeight((float)wx, (float)wz);
                depth[y, x] = CoastHaloField.SeaLevel - hm;            // >0 = water depth
                int gx = (int)Math.Round(wx / zone) - min, gy = (int)Math.Round(wz / zone) - min;
                int label = (gx >= 0 && gy >= 0 && gx < gw && gy < gh) ? rid[gy, gx] : -1;
                rgrid[y, x] = (hm >= CoastHaloField.SeaLevel) ? label : -1;
                if (label >= 0 && hm >= CoastHaloField.SeaLevel) landMass[label]++;
            }

            // ORPHAN AUDIT: connected land components per region id (8-conn) at 16m. >1 = region has chunks
            // physically detached from its main body (a separate island sharing the id). Apron inherits these.
            var seenC = new bool[H, W]; var compsByLabel = new Dictionary<int,List<int>>();
            for (int y=0;y<H;y++) for (int x=0;x<W;x++){
                int lab=rgrid[y,x]; if(lab<0||seenC[y,x])continue;
                var st=new Stack<(int,int)>(); st.Push((y,x)); seenC[y,x]=true; int n=0;
                while(st.Count>0){var(cy,cx)=st.Pop();n++;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int ny=cy+dy,nx=cx+dx;
                        if(ny<0||nx<0||ny>=H||nx>=W||seenC[ny,nx]||rgrid[ny,nx]!=lab)continue;seenC[ny,nx]=true;st.Push((ny,nx));}}
                if(!compsByLabel.TryGetValue(lab,out var l)){l=new List<int>();compsByLabel[lab]=l;}l.Add(n);
            }
            int multi=0,orphanCells=0,bigOrphans=0,tinyOrphans=0; foreach(var kv in compsByLabel){if(kv.Value.Count>1){multi++;kv.Value.Sort();kv.Value.Reverse();for(int i=1;i<kv.Value.Count;i++){orphanCells+=kv.Value[i];if(kv.Value[i]>=10)bigOrphans++;else tinyOrphans++;}}}
            Console.WriteLine($"ORPHAN AUDIT: {multi}/{compsByLabel.Count} regions have >1 land component; {orphanCells} orphaned cells; bigChunks(>=10cells)={bigOrphans} tinyChunks(<10)={tinyOrphans} (tiny ≈ diagonal-pinch/strait noise, big = real detached island sharing an id)");

            // MAIN-BODY-ONLY grid: keep each region's largest land component, strip detached chunks → -1.
            // Renders the "regions must be contiguous" world. Tag each component, pick biggest per label.
            var compId=new int[H,W]; for(int y=0;y<H;y++)for(int x=0;x<W;x++)compId[y,x]=-1;
            var cs=new List<(int lab,int sz,int id)>(); var sv2=new bool[H,W]; int cid=0;
            for(int y=0;y<H;y++)for(int x=0;x<W;x++){int lab=rgrid[y,x];if(lab<0||sv2[y,x])continue;var st=new Stack<(int,int)>();st.Push((y,x));sv2[y,x]=true;int n=0;var mc=new List<(int,int)>();while(st.Count>0){var(cy,cx)=st.Pop();n++;compId[cy,cx]=cid;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int ny=cy+dy,nx=cx+dx;if(ny<0||nx<0||ny>=H||nx>=W||sv2[ny,nx]||rgrid[ny,nx]!=lab)continue;sv2[ny,nx]=true;st.Push((ny,nx));}}cs.Add((lab,n,cid));cid++;}
            var biggest=new Dictionary<int,(int sz,int id)>();foreach(var c in cs){if(!biggest.TryGetValue(c.lab,out var b)||c.sz>b.sz)biggest[c.lab]=(c.sz,c.id);}
            var mainGrid=new int[H,W];for(int y=0;y<H;y++)for(int x=0;x<W;x++){int lab=rgrid[y,x];mainGrid[y,x]=(lab>=0&&biggest[lab].id==compId[y,x])?lab:-1;}
            RenderVariant("A-distance", world, biomeOf, rgrid, depth, W, H, cell, (d, dist) => dist <= 96 ? 1.0 : -1, landMass);
            RenderVariant("Amain-distance", world, biomeOf, mainGrid, depth, W, H, cell, (d, dist) => dist <= 96 ? 1.0 : -1, landMass);
            RenderVariant("B-depth",    world, biomeOf, rgrid, depth, W, H, cell, (d, dist) => d <= 14 ? 1.0 : -1, landMass);
            RenderVariant("C-cost",     world, biomeOf, rgrid, depth, W, H, cell, (d, dist) => CostBudget(d, dist, 96, 8), landMass);
            RenderVariant("Cmain-cost", world, biomeOf, mainGrid, depth, W, H, cell, (d, dist) => CostBudget(d, dist, 96, 8), landMass);
            RenderVariant("D-perRegion",world, biomeOf, rgrid, depth, W, H, cell, (d, dist) => CostBudget(d, dist, 96, 8), landMass, perRegion: true);
            Console.WriteLine($"Wrote 4 PNGs to {outDir}");
            return 0;
        }

        // cost ≈ dist scaled up by depth; budget = full alpha; >budget = unclaimed
        static double CostBudget(double depthM, double distM, double budget, double deepW) {
            double cost = distM * (1 + Math.Max(0, depthM) / deepW);
            return cost <= budget ? 1.0 - cost / budget : -1;
        }

        static void RenderVariant(string tag, RegionWorld world, BiomeType[] biome, int[,] rgrid, double[,] depth,
                                  int W, int H, double cell, Func<double,double,double> reach, int[] landMass, bool perRegion=false)
        {
            // multi-source BFS from coast; nearest land region + its distance; apron alpha = reach(depth,dist)
            var dist = new double[H, W]; var who = new int[H, W];
            for (int y=0;y<H;y++) for (int x=0;x<W;x++){ dist[y,x]=1e9; who[y,x]=-1; }
            var q = new Queue<(int,int)>();
            for (int y=0;y<H;y++) for (int x=0;x<W;x++) if (rgrid[y,x]>=0){
                bool coast=false; for(int d=0;d<4;d++){int ny=y+(d==0?-1:d==1?1:0),nx=x+(d==2?-1:d==3?1:0);
                    if(ny>=0&&nx>=0&&ny<H&&nx<W&&rgrid[ny,nx]<0)coast=true;}
                if(coast){dist[y,x]=0;who[y,x]=rgrid[y,x];q.Enqueue((y,x));}
            }
            while(q.Count>0){var(y,x)=q.Dequeue();double nd=dist[y,x]+cell;if(nd>200)continue;
                for(int d=0;d<4;d++){int ny=y+(d==0?-1:d==1?1:0),nx=x+(d==2?-1:d==3?1:0);
                    if(ny<0||nx<0||ny>=H||nx>=W||rgrid[ny,nx]>=0||dist[ny,nx]<=nd)continue;
                    dist[ny,nx]=nd;who[ny,nx]=who[y,x];q.Enqueue((ny,nx));}}
            var rgb=new byte[W*H*3];
            for(int y=0;y<H;y++)for(int x=0;x<W;x++){int o=(y*W+x)*3;int r=rgrid[y,x];
                if(r>=0){var(cr,cg,cb)=BiomeRenderPalette.Wash(biome[r]);rgb[o]=cr;rgb[o+1]=cg;rgb[o+2]=cb;continue;}
                int wlab=who[y,x];double bud=perRegion&&wlab>=0?Math.Min(160,60+landMass[wlab]*0.4):96;
                double a=wlab>=0?reach(depth[y,x],dist[y,x]*(96/bud)):-1;
                if(a>0){var(gr,gg,gb)=BiomeRenderPalette.Glow(biome[wlab]);rgb[o]=(byte)(gr*a);rgb[o+1]=(byte)(gg*a);rgb[o+2]=(byte)(gb*a);}
            }
            string p=$"/tmp/apron-{tag}.png";PngWriter.Write(p,W,H,rgb);
            // rogue check: claimed-water fragments disconnected from home region land + tiny land regions
            int small=0,total=0; foreach(var lm in landMass){total++;if(lm>0&&lm<6)small++;}
            int claimedWater=0; for(int y=0;y<H;y++)for(int x=0;x<W;x++){int wlab=who[y,x];if(rgrid[y,x]<0&&wlab>=0){double bud=perRegion&&wlab>=0?Math.Min(160,60+landMass[wlab]*0.4):96;if(reach(depth[y,x],dist[y,x]*(96/bud))>0)claimedWater++;}}
            // SKIRT-BRIDGE: connectivity over (land OR claimed-apron) per region. Does the apron reconnect
            // the detached islands? membership[y,x]=label if land==label OR apron-who==label. >1 comp = still orphaned.
            var mem=new int[H,W]; for(int y=0;y<H;y++)for(int x=0;x<W;x++){int r=rgrid[y,x];if(r>=0)mem[y,x]=r;else{int wl=who[y,x];double bd=perRegion&&wl>=0?Math.Min(160,60+landMass[wl]*0.4):96;mem[y,x]=(wl>=0&&reach(depth[y,x],dist[y,x]*(96/bd))>0)?wl:-1;}}
            var sv=new bool[H,W];var comp=new Dictionary<int,int>();for(int y=0;y<H;y++)for(int x=0;x<W;x++){int lab=mem[y,x];if(lab<0||sv[y,x])continue;var st=new Stack<(int,int)>();st.Push((y,x));sv[y,x]=true;while(st.Count>0){var(cy,cx)=st.Pop();for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int ny=cy+dy,nx=cx+dx;if(ny<0||nx<0||ny>=H||nx>=W||sv[ny,nx]||mem[ny,nx]!=lab)continue;sv[ny,nx]=true;st.Push((ny,nx));}}comp[lab]=comp.TryGetValue(lab,out var c)?c+1:1;}
            int stillMulti=0;foreach(var kv in comp)if(kv.Value>1)stillMulti++;
            Console.WriteLine($"  {tag} → {p}  | claimedWater={claimedWater}  STILL-ORPHANED-w/-skirt={stillMulti}/{comp.Count} (vs 150 land-only)");
        }
    }
}
