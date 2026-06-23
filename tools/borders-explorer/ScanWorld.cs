using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;

// World scanner: sample seed coarsely, find INTERESTING junctions for border case-studies —
// cells where many biomes meet within a radius AND/OR coast+relief coincide. Emits a ranked TSV.
class ScanWorld
{
    static readonly Dictionary<int,string> NAME = new Dictionary<int,string>{
        {0,"None"},{1,"Meadows"},{2,"Swamp"},{4,"Mountain"},{8,"BlackForest"},
        {16,"Plains"},{32,"AshLands"},{64,"DeepNorth"},{256,"Ocean"},{512,"Mistlands"}};

    static void Main(string[] args)
    {
        string seed = args.Length > 0 ? args[0] : "HHcLC5acQt";
        float half = args.Length > 1 ? float.Parse(args[1]) : 10000f;
        float step = args.Length > 2 ? float.Parse(args[2]) : 128f;   // coarse scan grid
        float radius = 192f;                                          // neighborhood for diversity
        var gen = new WorldGenerator(seed);

        int n = (int)(2 * half / step);
        // Precompute biome grid
        var biome = new int[n, n];
        var height = new float[n, n];
        for (int jz = 0; jz < n; jz++)
        {
            float wz = -half + jz * step;
            for (int ix = 0; ix < n; ix++)
            {
                float wx = -half + ix * step;
                biome[jz, ix] = (int)gen.GetBiome(wx, wz);
                height[jz, ix] = gen.GetHeight(wx, wz);
            }
        }

        int rad = (int)(radius / step);
        var rows = new List<(double score, int ix, int jz, string detail)>();
        for (int jz = rad; jz < n - rad; jz++)
        {
            for (int ix = rad; ix < n - rad; ix++)
            {
                var present = new HashSet<int>();
                bool hasLand = false, hasOcean = false;
                float hmin = float.MaxValue, hmax = float.MinValue;
                for (int dz = -rad; dz <= rad; dz++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        int b = biome[jz+dz, ix+dx];
                        present.Add(b);
                        if (b == 256) hasOcean = true; else hasLand = true;
                        float h = height[jz+dz, ix+dx];
                        if (h < hmin) hmin = h; if (h > hmax) hmax = h;
                    }
                int landBiomes = present.Count(b => b != 256 && b != 0);
                // Score: reward biome diversity, coast presence, and relief (height range)
                double relief = hmax - hmin;
                double score = landBiomes * 2.0 + (hasLand && hasOcean ? 3.0 : 0.0) + Math.Min(relief * 4.0, 4.0);
                // only keep genuinely mixed spots
                if (present.Count(b => b != 0) >= 3)
                {
                    string names = string.Join("+", present.Where(b=>b!=0).Select(b => NAME[b]));
                    float wx = -half + ix * step, wz = -half + jz * step;
                    rows.Add((score, ix, jz, $"({wx:F0},{wz:F0})\t{names}\trelief={relief:F2}\tcoast={(hasLand&&hasOcean)}"));
                }
            }
        }

        // Deduplicate spatially: greedily pick top-scoring, suppress anything within 600m
        rows.Sort((a,b) => b.score.CompareTo(a.score));
        var picked = new List<(double,int,int,string)>();
        int supRad = (int)(700f / step);
        var taken = new bool[n, n];
        foreach (var r in rows)
        {
            bool clash = false;
            for (int dz=-supRad; dz<=supRad && !clash; dz++)
                for (int dx=-supRad; dx<=supRad && !clash; dx++)
                {
                    int zz=r.jz+dz, xx=r.ix+dx;
                    if (zz>=0&&zz<n&&xx>=0&&xx<n&&taken[zz,xx]) clash=true;
                }
            if (clash) continue;
            taken[r.jz, r.ix] = true;
            picked.Add((r.score, r.ix, r.jz, r.detail));
            if (picked.Count >= 25) break;
        }

        Console.WriteLine("score\tworldXZ\tbiomes\trelief\tcoast");
        foreach (var p in picked)
            Console.WriteLine($"{p.Item1:F1}\t{p.Item4}");
    }
}
