using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) diagnosing Daniel's two phase-2 walk issues: (1) "holes" where no overlay
    /// renders, (2) swamp has an "extra layer". Classifies every fine (16 m) texel by what the LIVE
    /// overlay paints there — FILL (RegionFillMaskBaker) and/or FADE (CoastHaloField, same params the
    /// plugin uses) — and counts the conflict cases:
    ///   BOTH = fill AND fade on the same texel (the swamp double-layer)
    ///   HOLE = an in-region zone's water texel with NEITHER fill nor fade (the holes)
    /// Both fine grids are index-aligned (same origin minIndex·64−32, same 16 m cell, same dims), so a
    /// texel maps 1:1 between them. Renders a classification PNG + per-biome breakdown.
    /// </summary>
    public static class FillFadeViz
    {
        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Fill/Fade conflict viz — seed '{seed}' ===");
            System.IO.Directory.CreateDirectory(outDir);
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
            const double zone = 64.0, texel = 16.0; const int sub = 4;

            int[,] mainGrid = FillMaskViz_StripToMain(rid, sampler, min);   // reuse the strip

            // FILL: fine mask exactly as the plugin bakes it (30 m clip, swamp floor 22).
            var fillBaker = new RegionFillMaskBaker(sampler, HeightScalarField.SeaLevel, 22.0);
            int[,] fill = fillBaker.Bake(mainGrid, min, sub);
            int fh = fill.GetLength(0), fw = fill.GetLength(1);

            // FADE: CoastHaloField with the SAME params the plugin uses (band 96, depthFade 14, cost-flood 8).
            double ox = min * zone - 32.0, oz = min * zone - 32.0;
            int hW = (int)(gw * zone / texel), hH = (int)(gh * zone / texel);
            var haloHeight = new HeightScalarField(sampler, CoastHaloField.SeaLevel);
            int ridH = mainGrid.GetLength(0), ridW = mainGrid.GetLength(1);
            Func<double, double, int> regionIdAt = (wx, wz) =>
            {
                int gx = (int)Math.Round(wx / zone) - min, gy = (int)Math.Round(wz / zone) - min;
                return (gx < 0 || gy < 0 || gx >= ridW || gy >= ridH) ? -1 : mainGrid[gy, gx];
            };
            CoastHaloField halo = CoastHaloField.Build(haloHeight, ox, oz, texel, hW, hH,
                bandMeters: CoastHaloField.DefaultBandMeters, depthFadeMeters: 14.0,
                regionIdAt: regionIdAt, costFloodDeepWeight: 8.0, includeLakes: true,
                swampLandFloor: 22.0, isSwamp: (wx, wz) => sampler.GetBiome((float)wx, (float)wz) == BiomeType.Swamp);

            // Per-biome label lookup.
            int maxLabel = -1; foreach (var r in world.Regions) if (r.TransientId > maxLabel) maxLabel = r.TransientId;
            var biomeOf = new BiomeType[maxLabel + 1];
            foreach (var r in world.Regions) if (r.TransientId >= 0) biomeOf[r.TransientId] = r.DominantBiome;

            long cFill = 0, cBoth = 0, cFadeReg = 0, cHole = 0, cOcean = 0, cFadeNon = 0;
            long holeLand = 0, holeSwampShallow = 0, holeSwampDeep = 0, holeNonSwampWater = 0;
            long bothLand = 0, bothWater = 0;
            var bothByBiome = new Dictionary<BiomeType, long>();
            var holeByBiome = new Dictionary<BiomeType, long>();
            byte[] img = new byte[fw * fh * 3];

            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    int o = ((fh - 1 - fy) * fw + fx) * 3;
                    double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                    int zx = (int)Math.Round(wx / zone) - min, zy = (int)Math.Round(wz / zone) - min;
                    int zoneLab = (zx < 0 || zy < 0 || zx >= ridW || zy >= ridH) ? -1 : mainGrid[zy, zx];
                    bool filled = fill[fy, fx] >= 0;
                    // Apply the SHIPPED partition: BakeBiome forces fade alpha 0 where the fill paints.
                    bool faded = !filled && halo.Alpha(CoastHaloMode.Seaward, fy, fx) > 0.0;
                    BiomeType biome = zoneLab >= 0 && zoneLab <= maxLabel ? biomeOf[zoneLab] : BiomeType.Ocean;

                    byte r, g, b;
                    if (filled && faded) { cBoth++; bothByBiome[biome] = bothByBiome.GetValueOrDefault(biome) + 1; r = 255; g = 0; b = 255;        // MAGENTA = double layer
                        double hh = sampler.GetHeight((float)wx, (float)wz);
                        if (hh >= HeightScalarField.SeaLevel) bothLand++;        // fill painted LAND, fade also fired here
                        else bothWater++;                                        // fill painted sub-waterline (swamp), fade also
                    }
                    else if (filled) { cFill++; r = 40; g = 130; b = 40; }                                                                          // green = clean land fill
                    else if (faded && zoneLab >= 0) { cFadeReg++; r = 40; g = 160; b = 200; }                                                       // cyan = region coastal fade
                    else if (!filled && !faded && zoneLab >= 0) {
                        cHole++; holeByBiome[biome] = holeByBiome.GetValueOrDefault(biome) + 1; r = 255; g = 40; b = 40;                             // RED = HOLE (in-region, no cover)
                        double hh = sampler.GetHeight((float)wx, (float)wz);
                        bool isSwamp = sampler.GetBiome((float)wx, (float)wz) == BiomeType.Swamp;
                        if (hh >= HeightScalarField.SeaLevel) holeLand++;          // above water but unpainted (shouldn't happen)
                        else if (hh >= 22.0 && isSwamp) holeSwampShallow++;        // swamp 22-30 — rescue SHOULD have filled
                        else if (isSwamp) holeSwampDeep++;                         // swamp <22 — below rescue floor
                        else holeNonSwampWater++;                                  // non-swamp water in-region (the expected hole)
                    }
                    else if (faded) { cFadeNon++; r = 30; g = 60; b = 90; }                                                                          // dim = fade over non-region water
                    else { cOcean++; r = 12; g = 18; b = 36; }                                                                                       // navy = open ocean
                    img[o] = r; img[o + 1] = g; img[o + 2] = b;
                }

            long total = (long)fw * fh;
            Console.WriteLine($"fine grid {fw}x{fh}; classification:");
            Console.WriteLine($"  FILL (clean land):        {cFill}");
            Console.WriteLine($"  BOTH (fill+fade overlap): {cBoth}  ← issue 2: the swamp 'extra layer'");
            Console.WriteLine($"     both breakdown: fill-painted-LAND(≥30m)={bothLand}  fill-painted-water(swamp<30m)={bothWater}");
            Console.WriteLine($"  FADE in-region (coast):   {cFadeReg}");
            Console.WriteLine($"  HOLE (in-region, no cov): {cHole}  ← issue 1: the holes");
            Console.WriteLine($"     hole breakdown: above-water(BUG)={holeLand}  swamp22-30(rescue-miss)={holeSwampShallow}  swamp<22(below-floor)={holeSwampDeep}  non-swamp-water={holeNonSwampWater}");
            Console.WriteLine($"  fade over non-region:     {cFadeNon}");
            Console.WriteLine($"  open ocean:               {cOcean}");
            Console.WriteLine();
            Console.WriteLine("BOTH (double-layer) by region biome:");
            foreach (var kv in SortDesc(bothByBiome)) Console.WriteLine($"    {kv.Key,-12} {kv.Value}");
            Console.WriteLine("HOLE by region biome:");
            foreach (var kv in SortDesc(holeByBiome)) Console.WriteLine($"    {kv.Key,-12} {kv.Value}");

            string path = System.IO.Path.Combine(outDir, "fillfade_classify.png");
            PngWriter.Write(path, fw, fh, img);
            Console.WriteLine($"\n  png → {path}  (MAGENTA=double-layer, RED=hole, green=land, cyan=coastfade)");
            return 0;
        }

        static IEnumerable<KeyValuePair<BiomeType, long>> SortDesc(Dictionary<BiomeType, long> d)
        {
            var l = new List<KeyValuePair<BiomeType, long>>(d);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            return l;
        }

        // duplicate of FillMaskViz.StripToMain so this stays a standalone throwaway.
        static int[,] FillMaskViz_StripToMain(int[,] rid, IWorldSampler sampler, int min)
        {
            int gh = rid.GetLength(0), gw = rid.GetLength(1); const double zone = 64.0;
            var land = new int[gh, gw];
            for (int gy = 0; gy < gh; gy++) for (int gx = 0; gx < gw; gx++)
            {
                int lab = rid[gy, gx]; double wx = (gx + min) * zone, wz = (gy + min) * zone;
                land[gy, gx] = (lab >= 0 && sampler.GetHeight((float)wx, (float)wz) >= HeightScalarField.SeaLevel) ? lab : -1;
            }
            var comp = new int[gh, gw]; for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++) comp[y, x] = -1;
            var sizes = new List<(int lab, int sz, int id)>(); int cid = 0; var seen = new bool[gh, gw];
            for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++)
            {
                int lab = land[y, x]; if (lab < 0 || seen[y, x]) continue;
                var st = new Stack<(int, int)>(); st.Push((y, x)); seen[y, x] = true; int n = 0;
                while (st.Count > 0) { var (cy, cx) = st.Pop(); n++; comp[cy, cx] = cid;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int ny = cy + dy, nx = cx + dx;
                        if (ny < 0 || nx < 0 || ny >= gh || nx >= gw || seen[ny, nx] || land[ny, nx] != lab) continue; seen[ny, nx] = true; st.Push((ny, nx)); } }
                sizes.Add((lab, n, cid)); cid++;
            }
            var biggest = new Dictionary<int, (int sz, int id)>();
            foreach (var c in sizes) if (!biggest.TryGetValue(c.lab, out var bb) || c.sz > bb.sz) biggest[c.lab] = (c.sz, c.id);
            var outg = new int[gh, gw];
            for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++) { int lab = rid[y, x]; outg[y, x] = (lab >= 0 && biggest.TryGetValue(lab, out var bb) && comp[y, x] == bb.id) ? lab : -1; }
            return outg;
        }
    }
}
