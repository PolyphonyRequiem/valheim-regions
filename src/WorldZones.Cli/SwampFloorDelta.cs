using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) — test Daniel's hypothesis: the swamp zones that FLIP from Land→water when
    /// the swamp floor rises 22→28.5 m are mostly COASTAL (near ocean), not scattered inland. If true,
    /// raising the floor sheds the wet coastal-swamp speckle (which should read as water/fade) while
    /// preserving solid inland swamp. Classifies every 64 m swamp zone that is Land@22 but NOT Land@28.5,
    /// and tags it coastal (within N zones of ocean water) vs inland.
    /// </summary>
    public static class SwampFloorDelta
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== Swamp floor 22→28.5 delta — seed '{seed}' ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            var grid = new ZoneGrid();
            int min = grid.MinIndex, size = grid.Size;
            const double zone = 64.0, sea = 30.0, shelf = 10.0;

            // Classify zones by depth, and capture swamp + height at each zone centre.
            var isSwamp = new bool[size, size];
            var hAt = new float[size, size];
            var depth = new byte[size, size];   // 2 land(≥30) 1 shallow(≥20) 0 deep
            for (int gy = 0; gy < size; gy++)
                for (int gx = 0; gx < size; gx++)
                {
                    double wx = (gx + min) * zone, wz = (gy + min) * zone;
                    float h = sampler.GetHeight((float)wx, (float)wz);
                    hAt[gy, gx] = h;
                    isSwamp[gy, gx] = sampler.GetBiome((float)wx, (float)wz) == BiomeType.Swamp;
                    depth[gy, gx] = (byte)(h >= sea ? 2 : h >= sea - shelf ? 1 : 0);
                }

            // Land test at a floor F: height ≥ 30 OR (swamp AND height ≥ F).
            bool LandAt(int gy, int gx, double f) => hAt[gy, gx] >= sea || (isSwamp[gy, gx] && hAt[gy, gx] >= f);

            // Ocean = deep/shallow water connected to the world edge (4-conn flood) — for coastal tagging.
            var ocean = new bool[size, size];
            var oq = new Queue<(int,int)>();
            void Seed(int gy,int gx){ if(gy<0||gx<0||gy>=size||gx>=size)return; if(depth[gy,gx]==2||ocean[gy,gx])return; ocean[gy,gx]=true; oq.Enqueue((gy,gx)); }
            for(int i=0;i<size;i++){Seed(0,i);Seed(size-1,i);Seed(i,0);Seed(i,size-1);}
            while(oq.Count>0){var(cy,cx)=oq.Dequeue();Seed(cy-1,cx);Seed(cy+1,cx);Seed(cy,cx-1);Seed(cy,cx+1);}

            // Distance (in zones) from each zone to the nearest ocean — for "coastal within K".
            const int K = 2;   // within 2 zones (128 m) of ocean = coastal
            bool CoastalNearOcean(int gy,int gx)
            {
                for (int dy=-K; dy<=K; dy++) for (int dx=-K; dx<=K; dx++)
                { int ny=gy+dy, nx=gx+dx; if(ny<0||nx<0||ny>=size||nx>=size)continue; if(ocean[ny,nx])return true; }
                return false;
            }

            long land22=0, land285=0, flipped=0, flipCoastal=0, flipInland=0;
            long swampLand22=0, swampLand285=0;
            foreach (var _ in new[]{0})
            for (int gy=0; gy<size; gy++)
                for (int gx=0; gx<size; gx++)
                {
                    bool l22 = LandAt(gy,gx,22.0), l285 = LandAt(gy,gx,28.5);
                    if (l22) land22++; if (l285) land285++;
                    if (isSwamp[gy,gx] && l22) swampLand22++;
                    if (isSwamp[gy,gx] && l285) swampLand285++;
                    if (l22 && !l285)   // flipped Land→not-Land (only swamp zones can: ≥30 never flips)
                    {
                        flipped++;
                        if (CoastalNearOcean(gy,gx)) flipCoastal++; else flipInland++;
                    }
                }

            Console.WriteLine($"LAND zones @floor 22: {land22}   @floor 28.5: {land285}   (Δ = {land22-land285} fewer)");
            Console.WriteLine($"SWAMP-rescued land zones @22: {swampLand22}   @28.5: {swampLand285}   (Δ = {swampLand22-swampLand285})");
            Console.WriteLine();
            Console.WriteLine($"ZONES THAT FLIP Land→water (22→28.5): {flipped}");
            if (flipped > 0)
            {
                Console.WriteLine($"    COASTAL (≤{K} zones / {K*64}m from ocean): {flipCoastal} ({100.0*flipCoastal/flipped:F1}%)");
                Console.WriteLine($"    INLAND  (deeper than that):              {flipInland} ({100.0*flipInland/flipped:F1}%)");
                Console.WriteLine();
                Console.WriteLine($"    VERDICT: {(flipCoastal > flipInland ? "Daniel's hypothesis CONFIRMED — the lost swamp is mostly COASTAL (wet coast → reads as water, good)" : "NOT mostly coastal — the floor raise also cuts inland swamp body")}");
            }
            return 0;
        }
    }
}
