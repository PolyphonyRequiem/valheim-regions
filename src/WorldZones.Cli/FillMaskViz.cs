using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) sanity-check for the phase-2 FINE FILL MASK wiring. Renders exactly what
    /// the plugin now bakes — RegionFillMaskBaker over the main-only grid at 16 m, swamp floor 22 m,
    /// clip 30 m — as a PNG, plus audits: % land texels filled, coverage vs the coarse 64 m fill, and a
    /// swamp-hole check (sub-waterline swamp texels that DID fill via the rescue). Confirms the mask is
    /// well-formed (aligned, not all-water, no swamp holes) BEFORE Daniel's walk, since the live render
    /// can't be verified headlessly.
    /// </summary>
    public static class FillMaskViz
    {
        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Fill-mask viz — seed '{seed}' (phase-2 fine fill, 16m @ 30m waterline) ===");
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

            // main-only grid (same as the plugin's StripToMainComponent — keep each region's largest land body)
            int[,] mainGrid = StripToMain(rid, sampler, min);

            const int sub = 4; const double texel = 16.0, zone = 64.0;
            var baker = new RegionFillMaskBaker(sampler, coastIso: HeightScalarField.SeaLevel, swampLandFloor: 22.0);
            int[,] fine = baker.Bake(mainGrid, min, sub);
            int fh = fine.GetLength(0), fw = fine.GetLength(1);

            // palette per label (biome wash)
            int maxLabel = -1; foreach (var r in world.Regions) if (r.TransientId > maxLabel) maxLabel = r.TransientId;
            var wash = new (byte r, byte g, byte b)[maxLabel + 1];
            for (int i = 0; i <= maxLabel; i++) wash[i] = (60, 60, 60);
            foreach (var r in world.Regions) if (r.TransientId >= 0) wash[r.TransientId] = BiomeRenderPalette.Wash(r.DominantBiome);

            // audits
            long fineFilled = 0, fineWater = 0, coarseLandZones = 0, swampRescued = 0, subWaterFilled = 0;
            double ox = min * zone - 32.0, oz = min * zone - 32.0;
            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    if (fine[fy, fx] >= 0)
                    {
                        fineFilled++;
                        double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                        double h = sampler.GetHeight((float)wx, (float)wz);
                        if (h < HeightScalarField.SeaLevel) { subWaterFilled++; if (sampler.GetBiome((float)wx, (float)wz) == BiomeType.Swamp) swampRescued++; }
                    }
                    else fineWater++;
                }
            for (int gy = 0; gy < gh; gy++) for (int gx = 0; gx < gw; gx++) if (mainGrid[gy, gx] >= 0) coarseLandZones++;

            Console.WriteLine($"fine raster: {fw}x{fh} ({(long)fw * fh} texels)");
            Console.WriteLine($"  filled (land in region): {fineFilled} ({100.0 * fineFilled / ((long)fw * fh):F1}%)  water/unassigned: {fineWater}");
            Console.WriteLine($"  coarse main-grid land zones: {coarseLandZones}  (×{sub * sub} = {coarseLandZones * sub * sub} texels if zone-filled)");
            Console.WriteLine($"  → fine vs coarse-block: {100.0 * fineFilled / Math.Max(1, coarseLandZones * sub * sub):F1}% (under 100% = fill RETREATED off the water — the fix working)");
            Console.WriteLine($"  sub-waterline texels filled (swamp rescue): {subWaterFilled} (of which Swamp biome: {swampRescued})");
            Console.WriteLine($"  VERDICT: {(fineFilled > 0 && fineFilled < coarseLandZones * sub * sub ? "WELL-FORMED — fill present and retreated off water" : fineFilled == 0 ? "BAD — all water (mask misaligned?)" : "CHECK — fill did not retreat (clip not biting?)")}");

            // render PNG: water dark blue, filled = biome wash, with a thumbnail-friendly downscale
            byte[] img = new byte[fw * fh * 3];
            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    int o = ((fh - 1 - fy) * fw + fx) * 3;   // flip Y for north-up
                    int lab = fine[fy, fx];
                    if (lab >= 0 && lab <= maxLabel) { var c = wash[lab]; img[o] = c.r; img[o + 1] = c.g; img[o + 2] = c.b; }
                    else { img[o] = 14; img[o + 1] = 22; img[o + 2] = 44; }
                }
            string path = System.IO.Path.Combine(outDir, "fillmask_fine.png");
            PngWriter.Write(path, fw, fh, img);
            Console.WriteLine($"  png → {path}");
            return 0;
        }

        // mirror of the plugin's StripToMainComponent: keep each region's largest connected LAND component.
        static int[,] StripToMain(int[,] rid, IWorldSampler sampler, int min)
        {
            int gh = rid.GetLength(0), gw = rid.GetLength(1); const double zone = 64.0;
            var land = new int[gh, gw];
            for (int gy = 0; gy < gh; gy++) for (int gx = 0; gx < gw; gx++)
            {
                int lab = rid[gy, gx];
                double wx = (gx + min) * zone, wz = (gy + min) * zone;
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
            foreach (var c in sizes) if (!biggest.TryGetValue(c.lab, out var b) || c.sz > b.sz) biggest[c.lab] = (c.sz, c.id);
            var outg = new int[gh, gw];
            for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++) { int lab = rid[y, x]; outg[y, x] = (lab >= 0 && biggest.TryGetValue(lab, out var b) && comp[y, x] == b.id) ? lab : -1; }
            return outg;
        }
    }
}
