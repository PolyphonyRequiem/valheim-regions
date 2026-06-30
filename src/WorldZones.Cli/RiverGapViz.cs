using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) — test Daniel's hypothesis: the remaining unincorporated magenta land lies
    /// on RIVER lines. A Valheim river is a narrow sub-waterline channel cutting through land; its 64 m
    /// zone centre reads as water, so region growth (land-only) skips it AND the 16 m land strips flanking
    /// it get orphaned. Classifies each unincorporated land texel by the nearest water TYPE:
    ///   OCEAN  = nearest water is edge-connected ocean (true coast)
    ///   RIVER  = nearest water is INLAND (not ocean-connected) and NARROW (land within K on the far side)
    ///   LAKE   = inland water but not narrow (a wider enclosed body)
    /// If RIVER dominates the non-ocean magenta, Daniel's right and the fix is river-aware, not a floor.
    /// </summary>
    public static class RiverGapViz
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== River-gap hypothesis — seed '{seed}' ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });
            int[,] rid = world.RegionIdGrid; int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            const double zone = 64.0, sea = 30.0;

            // 64m zone depth + ocean flood (edge-connected water).
            var isWater = new bool[gh, gw];
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            { double wx=(gx+min)*zone, wz=(gy+min)*zone; isWater[gy,gx]=sampler.GetHeight((float)wx,(float)wz)<sea; }
            var ocean = new bool[gh, gw];
            var q=new Queue<(int,int)>();
            void Seed(int gy,int gx){ if(gy<0||gx<0||gy>=gh||gx>=gw)return; if(!isWater[gy,gx]||ocean[gy,gx])return; ocean[gy,gx]=true; q.Enqueue((gy,gx)); }
            for(int i=0;i<gw;i++){Seed(0,i);Seed(gh-1,i);} for(int i=0;i<gh;i++){Seed(i,0);Seed(i,gw-1);}
            while(q.Count>0){var(cy,cx)=q.Dequeue();Seed(cy-1,cx);Seed(cy+1,cx);Seed(cy,cx-1);Seed(cy,cx+1);}

            // Narrow-channel (river) test for a water zone: land reachable within K zones on BOTH of an
            // opposing axis (so it's a strait/channel, not an open body). K=3 zones ≈ 192 m (rivers are thin).
            const int K = 3;
            bool IsNarrowChannel(int gy, int gx)
            {
                // horizontal: land within K to the left AND right?
                bool lL=false,lR=false,lU=false,lD=false;
                for (int d=1; d<=K; d++){ if(gx-d>=0 && !isWater[gy,gx-d]) lL=true; if(gx+d<gw && !isWater[gy,gx+d]) lR=true;
                                          if(gy-d>=0 && !isWater[gy-d,gx]) lD=true; if(gy+d<gh && !isWater[gy+d,gx]) lU=true; }
                return (lL && lR) || (lU && lD);   // pinched on an opposing axis = channel
            }

            // For each UNINCORPORATED land zone, find nearest water zone (BFS) and classify it.
            long unincLand=0, nearOcean=0, nearRiver=0, nearLake=0, nearNone=0;
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            {
                double wx=(gx+min)*zone, wz=(gy+min)*zone;
                if (sampler.GetHeight((float)wx,(float)wz) < sea) continue;   // land only
                if (rid[gy,gx] >= 0) continue;                                // unincorporated only
                unincLand++;
                // BFS out to nearest water (≤6 zones); classify it.
                var seen=new HashSet<(int,int)>{(gx,gy)}; var bq=new Queue<(int,int,int)>(); bq.Enqueue((gx,gy,0));
                int kind=-1;   // 0 ocean 1 river 2 lake
                while(bq.Count>0 && kind<0)
                {
                    var(cx,cy,st)=bq.Dequeue(); if(st>6)break;
                    foreach (var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    {
                        int nx=cx+dx, ny=cy+dy; if(nx<0||ny<0||nx>=gw||ny>=gh||seen.Contains((nx,ny)))continue;
                        seen.Add((nx,ny));
                        if (isWater[ny,nx])
                        { kind = ocean[ny,nx] ? 0 : (IsNarrowChannel(ny,nx) ? 1 : 2); break; }
                        bq.Enqueue((nx,ny,st+1));
                    }
                }
                if (kind==0) nearOcean++; else if (kind==1) nearRiver++; else if (kind==2) nearLake++; else nearNone++;
            }

            Console.WriteLine($"UNINCORPORATED land zones (64m): {unincLand}");
            Console.WriteLine($"  nearest water is OCEAN (coastal):       {nearOcean} ({pct(nearOcean,unincLand)})");
            Console.WriteLine($"  nearest water is RIVER (narrow inland):  {nearRiver} ({pct(nearRiver,unincLand)})  ← Daniel's hypothesis");
            Console.WriteLine($"  nearest water is LAKE (wider inland):    {nearLake} ({pct(nearLake,unincLand)})");
            Console.WriteLine($"  no water within 6 zones:                 {nearNone} ({pct(nearNone,unincLand)})");
            long inland = nearRiver + nearLake;
            Console.WriteLine();
            Console.WriteLine($"  VERDICT: {(nearRiver >= nearOcean && nearRiver >= nearLake ? "RIVER-DOMINANT — Daniel's right, the gaps track river lines" : nearOcean > inland ? "still mostly COASTAL" : "mixed inland (river+lake)")}");
            return 0;
        }
        static string pct(long a, long b) => b>0 ? $"{100.0*a/b:F1}%" : "0%";
    }
}
