using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) — quantify the UNINCORPORATED-LAND root cause. Region growth
    /// (ProtoRegionGenerator step 3) floods ONLY across DepthClass.Land; the shallow fringe (step 5)
    /// only claims ONE ring of Shallow WATER adjacent to assigned land. So land separated from its
    /// nearest region by any water gap is never assigned → the "grey gap" magenta. This counts:
    ///   - total assigned-land vs unincorporated-land zones (world-wide)
    ///   - of the unincorporated land, how much is BRIDGEABLE (within N shallow-water zones of a region)
    ///     vs ISOLATED (only reachable across deep water) — the fix's blast radius.
    /// Pure zone-grid analysis (64 m), classified the same way the generator does.
    /// </summary>
    public static class UnincLandViz
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== Unincorporated-land diagnosis — seed '{seed}' ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });
            int[,] rid = world.RegionIdGrid; int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            const double zone = 64.0, sea = HeightScalarField.SeaLevel, shelf = 10.0; // ShelfMaxDepth

            // Classify each zone by depth, exactly as ZoneClassifier does (depth-only).
            // Land: h>=30, Shallow: h>=20, Deep: else.
            var depth = new byte[gh, gw];   // 2=land 1=shallow 0=deep
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            {
                double wx=(gx+min)*zone, wz=(gy+min)*zone; double h=sampler.GetHeight((float)wx,(float)wz);
                depth[gy,gx] = (byte)(h>=sea ? 2 : h>=sea-shelf ? 1 : 0);
            }

            long landZones=0, landAssigned=0, landUninc=0;
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            {
                if (depth[gy,gx]!=2) continue;   // land only
                landZones++;
                if (rid[gy,gx]>=0) landAssigned++; else landUninc++;
            }
            Console.WriteLine($"LAND zones (64m): {landZones}");
            Console.WriteLine($"  assigned to a region:   {landAssigned} ({100.0*landAssigned/landZones:F1}%)");
            Console.WriteLine($"  UNINCORPORATED:         {landUninc} ({100.0*landUninc/landZones:F1}%)  ← the grey gap, world-wide");

            // For each unincorporated land zone, BFS outward over water (shallow/deep) to the nearest
            // assigned zone; classify by how it would be reached.
            //   bridgeable-shallow: nearest assigned reachable crossing ONLY shallow water within K zones
            //   gap-deep: only reachable crossing deep water (or beyond K)
            const int K=4;   // 4 zones = 256m bridging reach
            long bridgeShallow=0, gapDeep=0, adjacentDirect=0;
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            {
                if (depth[gy,gx]!=2 || rid[gy,gx]>=0) continue;   // unincorporated land only
                // BFS up to K steps over NON-land; success if we touch an assigned land zone.
                var seen=new HashSet<(int,int)>(); var q=new Queue<(int gx,int gy,int steps,bool crossedDeep)>();
                q.Enqueue((gx,gy,0,false)); seen.Add((gx,gy));
                bool foundShallow=false, foundAny=false, direct=false;
                while(q.Count>0)
                {
                    var (cx,cy,st,cd)=q.Dequeue();
                    foreach (var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)})
                    {
                        int nx=cx+dx, ny=cy+dy;
                        if (nx<0||ny<0||nx>=gw||ny>=gh||seen.Contains((nx,ny))) continue;
                        // reached an assigned land zone of ANOTHER region?
                        if (depth[ny,nx]==2 && rid[ny,nx]>=0)
                        {
                            foundAny=true; if (st==0) direct=true; if (!cd) foundShallow=true;
                            continue;
                        }
                        if (depth[ny,nx]==2) continue; // other unincorporated land: don't path through (separate body)
                        if (st>=K) continue;
                        bool nd = cd || depth[ny,nx]==0;   // crossed deep water?
                        seen.Add((nx,ny)); q.Enqueue((nx,ny,st+1,nd));
                    }
                }
                if (direct) adjacentDirect++;
                else if (foundShallow) bridgeShallow++;
                else if (foundAny) gapDeep++;
                else gapDeep++;   // nothing within K → isolated, counts as gap
            }
            Console.WriteLine($"  of the {landUninc} unincorporated land zones, reachability to a region:");
            Console.WriteLine($"    DIRECTLY land-adjacent to a region (should've grown!): {adjacentDirect}");
            Console.WriteLine($"    bridgeable over SHALLOW water (≤{K} zones): {bridgeShallow}");
            Console.WriteLine($"    only across DEEP water / isolated (>{K} or deep): {gapDeep}");
            Console.WriteLine();
            Console.WriteLine($"  READING: 'adjacent' = a fringe/tie-break miss (cheap fix). 'shallow-bridgeable' =");
            Console.WriteLine($"  archipelago the growth can't cross but a shallow-hop pass could claim. 'deep' = genuinely");
            Console.WriteLine($"  separate islands (Daniel's call: stay unincorporated, like the gold-glow decision).");
            return 0;
        }
    }
}
