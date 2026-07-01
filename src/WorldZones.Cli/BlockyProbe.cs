using System;
using System.Collections.Generic;
using System.IO;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>THROWAWAY (2026-07-01) — isolates the "blocky fill" cause. Bakes the fork-B shared-seam fine
    /// fill mask (16 m texels), then renders it THREE ways: (1) raw fine mask (what the boundary actually is),
    /// (2) the same mask fog-gated per 64 m ZONE (what the controller shows — reveals whole zones at a time),
    /// (3) the 64 m coarse region grid for reference. If (1) is smooth but (2) steps, the blockiness is the
    /// per-ZONE FOG REVEAL, not the fill boundary.</summary>
    public static class BlockyProbe
    {
        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== Blocky-fill probe — seed '{seed}' ===");
            var sampler = new PortWorldSampler(new WorldGenerator(seed), seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            { IncludeInlandWater = false, UseFeatureAwareBorders = true, ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer() });

            int[,] grid = world.RegionIdGrid;
            int min = world.Grid.MinIndex, gh = grid.GetLength(0), gw = grid.GetLength(1);
            const double zone = 64.0; int sub = 4;   // 16 m
            var keyToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RegionInfo r in world.Regions) keyToLabel[r.RegionKey] = r.TransientId;
            var idToKey = new Dictionary<int, string>();
            foreach (RegionInfo r in world.Regions) if (!idToKey.ContainsKey(r.TransientId)) idToKey[r.TransientId] = r.RegionKey;
            var coast = new HeightScalarField(sampler, HeightScalarField.SeaLevel);
            var flip = new BiomeCategoryField(sampler);
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);

            RefinedRegionBoundary indep = RefinedRegionBoundary.Build(graph, keyToLabel,
                (wx, wz) => { int zx=(int)Math.Round(wx/zone)-min, zy=(int)Math.Round(wz/zone)-min; return (zx<0||zy<0||zx>=gw||zy>=gh)?-1:grid[zy,zx]; },
                coast, flip);
            SharedSeamSet seams = SharedSeamSet.Build(graph, coast, flip);
            RefinedRegionBoundary shared = SharedSeamBoundary.ToRefinedRegionBoundary(seams, graph, fallback: indep);
            int[,] fine = new RegionRingFillBaker(shared, keyToLabel).Bake(gh, gw, min, sub);

            int fh = fine.GetLength(0), fw = fine.GetLength(1);
            // (2) simulate the controller's per-ZONE gate: reveal whole 64m zones (assume all explored here,
            // but quantize the LABEL to the zone by majority — no: the controller copies fine labels for
            // explored zones. The staircase in-game is the fog FRONTIER. To show the boundary itself, we just
            // render the fine mask. To show what per-zone quantization would do, majority-vote each zone.)
            int[,] zoneQuant = new int[fh, fw];
            for (int zy=0; zy<gh; zy++) for (int zx=0; zx<gw; zx++)
            {
                var cnt = new Dictionary<int,int>();
                for (int dy=0; dy<sub; dy++) for (int dx=0; dx<sub; dx++)
                { int v=fine[zy*sub+dy, zx*sub+dx]; cnt[v]=cnt.GetValueOrDefault(v)+1; }
                int best=-1, bc=-1; foreach (var kv in cnt) if (kv.Value>bc){bc=kv.Value;best=kv.Key;}
                for (int dy=0; dy<sub; dy++) for (int dx=0; dx<sub; dx++) zoneQuant[zy*sub+dy, zx*sub+dx]=best;
            }

            // Count how "stepped" each is: boundary texels whose neighbours differ, on axis-aligned 64m lines.
            long fineEdge = EdgeLen(fine), quantEdge = EdgeLen(zoneQuant);
            Console.WriteLine($"fine mask {fw}x{fh} (16m texels)");
            Console.WriteLine($"  fine  boundary edge texels: {fineEdge}");
            Console.WriteLine($"  zone-quantized boundary edge texels: {quantEdge}");
            Console.WriteLine($"  → the fill MASK is {(fineEdge < quantEdge ? "SMOOTHER than" : "same as")} zone-quantized.");
            Console.WriteLine();
            Console.WriteLine("If in-game looks like the zone-quantized version but the mask is the fine one,");
            Console.WriteLine("the blockiness is DOWNSTREAM of the mask (fog reveal per-zone, or a coarse consumer).");

            // dump both as raw label PNGs via a tiny writer
            WritePng(Path.Combine(outDir, $"{seed}_fill_fine.png"), fine, world);
            WritePng(Path.Combine(outDir, $"{seed}_fill_zonequant.png"), zoneQuant, world);
            Console.WriteLine($"wrote {seed}_fill_fine.png + {seed}_fill_zonequant.png");
            return 0;
        }

        static long EdgeLen(int[,] m)
        {
            int h=m.GetLength(0), w=m.GetLength(1); long e=0;
            for (int y=0;y<h;y++) for (int x=0;x<w-1;x++) if (m[y,x]>=0 && m[y,x+1]>=0 && m[y,x]!=m[y,x+1]) e++;
            for (int y=0;y<h-1;y++) for (int x=0;x<w;x++) if (m[y,x]>=0 && m[y+1,x]>=0 && m[y,x]!=m[y+1,x]) e++;
            return e;
        }

        static void WritePng(string path, int[,] m, RegionWorld world)
        {
            int h=m.GetLength(0), w=m.GetLength(1);
            var px = new byte[w*h*3];
            for (int y=0;y<h;y++) for (int x=0;x<w;x++)
            {
                int lbl=m[h-1-y,x]; byte r,g,b;
                if (lbl<0){r=24;g=26;b=32;}
                else { var c=Hue(lbl); r=c.Item1; g=c.Item2; b=c.Item3; }
                int i=(y*w+x)*3; px[i]=r; px[i+1]=g; px[i+2]=b;
            }
            PngWriter.Write(path, w, h, px);
        }
        static (byte,byte,byte) Hue(int lbl)
        {
            double hh=(lbl*0.137)%1.0, s=0.55, v=0.9; int hi=(int)(hh*6); double f=hh*6-hi, p=v*(1-s), q=v*(1-f*s), t=v*(1-(1-f)*s), rr=0,gg=0,bb=0;
            switch(hi%6){case 0:rr=v;gg=t;bb=p;break;case 1:rr=q;gg=v;bb=p;break;case 2:rr=p;gg=v;bb=t;break;case 3:rr=p;gg=q;bb=v;break;case 4:rr=t;gg=p;bb=v;break;default:rr=v;gg=p;bb=q;break;}
            return ((byte)(rr*255),(byte)(gg*255),(byte)(bb*255));
        }
    }
}
