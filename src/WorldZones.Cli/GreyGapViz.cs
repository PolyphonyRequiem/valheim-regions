using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) — visualise the "grey gaps": LAND (terrain ≥ 30 m) that NO region
    /// incorporated, sitting between a region's fill and the water. Renders the Astley swamp region
    /// window with four explicit classes so Daniel can see exactly what "grey gap" means:
    ///   OLIVE   = this region's ring fill
    ///   MAGENTA = UNINCORPORATED land (≥30 m land that no region's grid claims) ← the "grey gap"
    ///   dim grey= other regions' land
    ///   blue    = water
    /// Also reports how much unincorporated land touches this region's coast.
    /// </summary>
    public static class GreyGapViz
    {
        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Grey-gap viz — seed '{seed}' ===");
            System.IO.Directory.CreateDirectory(outDir);
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });
            int[,] rid = world.RegionIdGrid; int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            const double zone = 64.0, texel = 16.0; const int sub = 4;
            int[,] mainGrid = Strip(rid, sampler, min);
            double ox = min * zone - 32.0, oz = min * zone - 32.0;
            int ridW = mainGrid.GetLength(1), ridH = mainGrid.GetLength(0);

            // Pick the same Astley swamp region.
            double worldCx = (min + gw / 2.0) * zone, worldCz = (min + gh / 2.0) * zone;
            RegionInfo pick = null; long best = -1;
            foreach (var r in world.Regions.Where(r => r.DominantBiome == BiomeType.Swamp))
            {
                if (r.CentroidX > worldCx || r.CentroidZ > worldCz) continue;
                long a = 0; for (int gy=0;gy<ridH;gy++) for (int gx=0;gx<ridW;gx++) if (mainGrid[gy,gx]==r.TransientId) a++;
                if (a > best) { best = a; pick = r; }
            }
            if (pick == null) pick = world.Regions.First(r => r.DominantBiome == BiomeType.Swamp);
            int label = pick.TransientId;
            Console.WriteLine($"region: key={pick.RegionKey} name=\"{pick.Name}\" centroid=({pick.CentroidX:F0},{pick.CentroidZ:F0})");

            int fh = ridH * sub, fw = ridW * sub;
            Func<int,int,int> zoneAt = (fx, fy) =>
            { double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel; int zx=(int)Math.Round(wx/zone)-min, zy=(int)Math.Round(wz/zone)-min;
              return (zx<0||zy<0||zx>=ridW||zy>=ridH)?-1:mainGrid[zy,zx]; };
            bool IsLand(int fx,int fy){ double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel; return sampler.GetHeight((float)wx,(float)wz)>=HeightScalarField.SeaLevel; }

            // bbox of this region's zones, padded.
            int minfx=fw,minfy=fh,maxfx=0,maxfy=0; bool any=false;
            for (int fy=0;fy<fh;fy++) for (int fx=0;fx<fw;fx++) if (zoneAt(fx,fy)==label){any=true; if(fx<minfx)minfx=fx; if(fx>maxfx)maxfx=fx; if(fy<minfy)minfy=fy; if(fy>maxfy)maxfy=fy;}
            if (!any) { Console.Error.WriteLine("region not in grid"); return 1; }
            int pad=14; minfx=Math.Max(0,minfx-pad); minfy=Math.Max(0,minfy-pad); maxfx=Math.Min(fw-1,maxfx+pad); maxfy=Math.Min(fh-1,maxfy+pad);
            int Wt=maxfx-minfx+1, Ht=maxfy-minfy+1;
            int scale=Math.Max(1, 880/Math.Max(Wt,Ht));
            int W=Wt*scale, H=Ht*scale;
            byte[] img=new byte[W*H*3];

            long unincCoast=0, unincTotal=0;
            for (int ty=0;ty<Ht;ty++)
                for (int tx=0;tx<Wt;tx++)
                {
                    int fx=minfx+tx, fy=minfy+ty;
                    bool land=IsLand(fx,fy);
                    int z=zoneAt(fx,fy);
                    byte r,g,b;
                    if (!land)
                    {   // water
                        double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel; double h=sampler.GetHeight((float)wx,(float)wz);
                        double d=Math.Min(1,(HeightScalarField.SeaLevel-h)/40.0); r=(byte)(22+12*(1-d)); g=(byte)(42+30*(1-d)); b=(byte)(82+60*(1-d));
                    }
                    else if (z==label) { r=150; g=175; b=80; }              // this region's fill (olive)
                    else if (z<0)
                    {   // LAND but no region owns this zone → the GREY GAP. Highlight magenta.
                        r=235; g=40; b=200; unincTotal++;
                        // does it touch this region? (4-neighbour zone is our label)
                        bool touches=false;
                        foreach (var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}) { int nz=zoneAt(fx+dx,fy+dy); if (nz==label){touches=true;break;} }
                        if (touches) unincCoast++;
                    }
                    else { r=78; g=78; b=92; }                              // other region's land (dim grey)
                    int py0=(Ht-1-ty)*scale, px0=tx*scale;
                    for (int dy=0;dy<scale;dy++) for (int dx=0;dx<scale;dx++){ int o=((py0+dy)*W+(px0+dx))*3; img[o]=r; img[o+1]=g; img[o+2]=b; }
                }

            Console.WriteLine($"  UNINCORPORATED land texels in window: {unincTotal}  (touching THIS region's coast: {unincCoast})");
            // Diagnose WHAT the magenta zones are: depth class + assigned-neighbour count, to tell
            // 'island across water' from 'skipped interior zone' from 'fine-texel-below-30m speckle'.
            int magShallowZone=0, magDeepZone=0, magLandZone=0, magHasAssignedLandNbr=0;
            for (int ty=0;ty<Ht;ty++) for (int tx=0;tx<Wt;tx++)
            {
                int fx=minfx+tx, fy=minfy+ty;
                if (!IsLand(fx,fy)) continue; int z=zoneAt(fx,fy); if (z>=0) continue;
                // this is a magenta texel — what's its ZONE depth + does a region's land touch its zone?
                double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel;
                int zx=(int)Math.Round(wx/zone)-min, zy=(int)Math.Round(wz/zone)-min;
                double zwx=(zx+min)*zone, zwz=(zy+min)*zone; double zh=sampler.GetHeight((float)zwx,(float)zwz);
                if (zh>=HeightScalarField.SeaLevel) magLandZone++; else if (zh>=20) magShallowZone++; else magDeepZone++;
                // assigned-land zone neighbour?
                bool nbr=false; foreach (var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}) { int n=zoneAt(fx+dx*4,fy+dy*4); if(n>=0){nbr=true;break;} }
                if (nbr) magHasAssignedLandNbr++;
            }
            Console.WriteLine($"  magenta texel ZONE-CENTRE depth: land(≥30)={magLandZone} shallow(20-30)={magShallowZone} deep(<20)={magDeepZone}");
            Console.WriteLine($"  magenta texels whose zone has an ASSIGNED-LAND zone neighbour: {magHasAssignedLandNbr}");
            Console.WriteLine($"  → magenta = land ≥30m that NO region claims = the 'grey gap'");
            PngWriter.Write(System.IO.Path.Combine(outDir,"greygap.png"), W, H, img);
            Console.WriteLine($"  → {outDir}/greygap.png");
            return 0;
        }

        static int[,] Strip(int[,] rid, IWorldSampler sampler, int min)
        {
            int gh=rid.GetLength(0), gw=rid.GetLength(1); const double zone=64.0;
            var land=new int[gh,gw];
            for(int gy=0;gy<gh;gy++)for(int gx=0;gx<gw;gx++){int lab=rid[gy,gx];double wx=(gx+min)*zone,wz=(gy+min)*zone;land[gy,gx]=(lab>=0&&sampler.GetHeight((float)wx,(float)wz)>=HeightScalarField.SeaLevel)?lab:-1;}
            var comp=new int[gh,gw];for(int y=0;y<gh;y++)for(int x=0;x<gw;x++)comp[y,x]=-1;
            var sizes=new List<(int lab,int sz,int id)>();int cid=0;var seen=new bool[gh,gw];
            for(int y=0;y<gh;y++)for(int x=0;x<gw;x++){int lab=land[y,x];if(lab<0||seen[y,x])continue;var st=new Stack<(int,int)>();st.Push((y,x));seen[y,x]=true;int n=0;while(st.Count>0){var(cy,cx)=st.Pop();n++;comp[cy,cx]=cid;for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int ny=cy+dy,nx=cx+dx;if(ny<0||nx<0||ny>=gh||nx>=gw||seen[ny,nx]||land[ny,nx]!=lab)continue;seen[ny,nx]=true;st.Push((ny,nx));}}sizes.Add((lab,n,cid));cid++;}
            var big=new Dictionary<int,(int sz,int id)>();foreach(var c in sizes)if(!big.TryGetValue(c.lab,out var bb)||c.sz>bb.sz)big[c.lab]=(c.sz,c.id);
            var outg=new int[gh,gw];for(int y=0;y<gh;y++)for(int x=0;x<gw;x++){int lab=rid[y,x];outg[y,x]=(lab>=0&&big.TryGetValue(lab,out var bb)&&comp[y,x]==bb.id)?lab:-1;}
            return outg;
        }
    }
}
