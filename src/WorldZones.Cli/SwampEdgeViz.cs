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
    /// THROWAWAY (2026-06-29) diagnosing Daniel's "swamp fill is blocky + doesn't agree with the coast".
    /// For a swamp coastal region it classifies EVERY fill-edge texel by WHY its outward neighbour isn't
    /// filled — distinguishing the two candidate causes:
    ///   ZONE-LIMITED  = neighbour's 64 m ZONE isn't in the region (the fill can't claim land the coarse
    ///                   zone grid never assigned → a 64 m staircase, the likely "blocky").
    ///   COAST-LIMITED = neighbour's zone IS in-region but the neighbour texel is water by the 16 m height
    ///                   test (genuine 16 m coastline detail).
    /// Plus: how much filled area is SUB-WATERLINE (swamp rescue, terrain <30 m) — i.e. fill extending
    /// past the visible 30 m coast. Renders a zoomed window: base height shading + fill + true 30 m coast.
    /// </summary>
    public static class SwampEdgeViz
    {
        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Swamp edge diagnosis — seed '{seed}' ===");
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
            int[,] mainGrid = FillFadeViz_Strip(rid, sampler, min);
            var baker = new RegionFillMaskBaker(sampler, HeightScalarField.SeaLevel, 22.0);
            int[,] fill = baker.Bake(mainGrid, min, sub);
            int fh = fill.GetLength(0), fw = fill.GetLength(1);
            double ox = min * zone - 32.0, oz = min * zone - 32.0;

            // Pick a SWAMP region with the most coastal fill-edge, to render its worst window.
            int maxLabel = -1; foreach (var r in world.Regions) if (r.TransientId > maxLabel) maxLabel = r.TransientId;
            var biomeOf = new BiomeType[maxLabel + 1];
            foreach (var r in world.Regions) if (r.TransientId >= 0) biomeOf[r.TransientId] = r.DominantBiome;

            // Global edge classification over ALL swamp fill texels.
            long zoneLimited = 0, coastLimited = 0, subWaterFill = 0, swampSubWaterFill = 0, totalSwampFill = 0;
            // also bucket the STEP sizes of zone-limited runs by measuring contiguous edge straightness.
            int ridW = mainGrid.GetLength(1), ridH = mainGrid.GetLength(0);
            Func<int,int,int> zoneAt = (fx, fy) =>
            {
                double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                int zx = (int)Math.Round(wx / zone) - min, zy = (int)Math.Round(wz / zone) - min;
                return (zx < 0 || zy < 0 || zx >= ridW || zy >= ridH) ? -1 : mainGrid[zy, zx];
            };
            bool IsLand(int fx, int fy)
            {
                double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                double h = sampler.GetHeight((float)wx, (float)wz);
                if (h >= HeightScalarField.SeaLevel) return true;
                return h >= 22.0 && sampler.GetBiome((float)wx, (float)wz) == BiomeType.Swamp;
            }

            for (int fy = 0; fy < fh; fy++)
                for (int fx = 0; fx < fw; fx++)
                {
                    int lab = fill[fy, fx];
                    if (lab < 0) continue;
                    BiomeType biome = lab <= maxLabel ? biomeOf[lab] : BiomeType.Ocean;
                    if (biome != BiomeType.Swamp) continue;
                    totalSwampFill++;
                    double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    if (h < HeightScalarField.SeaLevel) { subWaterFill++; swampSubWaterFill++; }
                    // is this an edge texel? (4-neighbour not filled)
                    foreach (var (dx, dy) in new[] { (1,0),(-1,0),(0,1),(0,-1) })
                    {
                        int nx = fx + dx, ny = fy + dy;
                        if (nx < 0 || ny < 0 || nx >= fw || ny >= fh) continue;
                        if (fill[ny, nx] >= 0) continue;     // neighbour filled → not an edge here
                        // neighbour empty: WHY?
                        int nzone = zoneAt(nx, ny);
                        bool nLand = IsLand(nx, ny);
                        if (nzone != lab) zoneLimited++;          // neighbour's zone not in THIS region → 64m limit
                        else if (!nLand) coastLimited++;          // neighbour in-region but water → 16m coast
                        // (if nzone==lab && nLand the baker would have filled it; unreachable)
                        break; // count each edge texel once
                    }
                }

            Console.WriteLine($"SWAMP fill texels: {totalSwampFill}");
            Console.WriteLine($"  sub-waterline (<30m, rescued): {swampSubWaterFill} ({(totalSwampFill>0?100.0*swampSubWaterFill/totalSwampFill:0):F1}%) ← fill PAST the visible 30m coast");
            long edge = zoneLimited + coastLimited;
            Console.WriteLine($"  edge texels: {edge}");
            Console.WriteLine($"    ZONE-LIMITED  (64m zone membership): {zoneLimited} ({(edge>0?100.0*zoneLimited/edge:0):F1}%)  ← blocky 64m staircase");
            Console.WriteLine($"    COAST-LIMITED (16m height test):     {coastLimited} ({(edge>0?100.0*coastLimited/edge:0):F1}%)  ← genuine 16m coast");
            Console.WriteLine($"  VERDICT: {(zoneLimited > coastLimited ? "BLOCKY EDGE IS 64m ZONE-MEMBERSHIP — fine fill can't help; the region's COARSE zones end here" : "edge is mostly 16m coast detail — staircase is texel-res, bilinear/8m would smooth")}");

            // ── Render the WORST swamp region in the lower-left quadrant (Daniel: "mid bottom left") ──
            // Pick the swamp region whose centroid is in the lower-left and which has the most fill.
            var swampRegions = world.Regions.Where(r => r.DominantBiome == BiomeType.Swamp).ToList();
            RegionInfo pick = null; long pickArea = -1;
            // world spans roughly [min*64, (min+gw)*64]; lower-left = wx<centerX, wz<centerZ (Valheim Z up = north,
            // so "bottom" = low Z). Use centroid in world metres.
            double worldCx = (min + gw / 2.0) * zone, worldCz = (min + gh / 2.0) * zone;
            foreach (var r in swampRegions)
            {
                if (r.CentroidX > worldCx || r.CentroidZ > worldCz) continue;   // lower-left quadrant only
                long a = 0;
                for (int gy = 0; gy < ridH; gy++) for (int gx = 0; gx < ridW; gx++) if (mainGrid[gy, gx] == r.TransientId) a++;
                if (a > pickArea) { pickArea = a; pick = r; }
            }
            if (pick == null && swampRegions.Count > 0) pick = swampRegions[0];   // fallback: any swamp
            if (pick != null)
            {
                RenderRegionWindow(outDir, sampler, fill, mainGrid, min, ox, oz, texel, zone, sub, pick, biomeOf, maxLabel);
                Console.WriteLine($"  rendered region: key={pick.RegionKey} name=\"{pick.Name}\" centroid=({pick.CentroidX:F0},{pick.CentroidZ:F0}) → {outDir}/swampedge_window.png");
            }
            return 0;
        }

        // Render a zoomed window of one region: base terrain, fill (olive), 30m coast (white), 64m zone grid (red).
        static void RenderRegionWindow(string outDir, IWorldSampler sampler, int[,] fill, int[,] mainGrid,
            int min, double ox, double oz, double texel, double zone, int sub, RegionInfo region,
            BiomeType[] biomeOf, int maxLabel)
        {
            int fh = fill.GetLength(0), fw = fill.GetLength(1);
            int label = region.TransientId;
            // bbox of this region's fill texels, padded.
            int minfx = fw, minfy = fh, maxfx = 0, maxfy = 0; bool any = false;
            for (int fy = 0; fy < fh; fy++) for (int fx = 0; fx < fw; fx++)
                if (fill[fy, fx] == label) { any = true; if (fx < minfx) minfx = fx; if (fx > maxfx) maxfx = fx; if (fy < minfy) minfy = fy; if (fy > maxfy) maxfy = fy; }
            if (!any) return;
            int pad = 12;
            minfx = Math.Max(0, minfx - pad); minfy = Math.Max(0, minfy - pad);
            maxfx = Math.Min(fw - 1, maxfx + pad); maxfy = Math.Min(fh - 1, maxfy + pad);
            int Wt = maxfx - minfx + 1, Ht = maxfy - minfy + 1;
            int scale = Math.Max(1, 900 / Math.Max(Wt, Ht));   // upscale so texels are visible
            int W = Wt * scale, H = Ht * scale;
            byte[] img = new byte[W * H * 3];

            for (int ty = 0; ty < Ht; ty++)
                for (int tx = 0; tx < Wt; tx++)
                {
                    int fx = minfx + tx, fy = minfy + ty;
                    double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    int lab = fill[fy, fx];
                    byte r, g, b;
                    // base: water shades by depth, land grey
                    if (h < HeightScalarField.SeaLevel) { double dep = Math.Min(1, (HeightScalarField.SeaLevel - h) / 40.0); r = (byte)(20 + 12*(1-dep)); g = (byte)(40 + 30*(1-dep)); b = (byte)(80 + 60*(1-dep)); }
                    else { r = g = b = 70; }
                    // overlay: THIS region's fill = olive (swamp wash); other regions dim
                    if (lab == label) { r = 150; g = 175; b = 80; }
                    else if (lab >= 0) { r = 80; g = 80; b = 95; }
                    // paint the upscaled block
                    int py0 = (Ht - 1 - ty) * scale, px0 = tx * scale;   // flip Y north-up
                    for (int dy = 0; dy < scale; dy++) for (int dx = 0; dx < scale; dx++)
                    { int o = ((py0 + dy) * W + (px0 + dx)) * 3; img[o] = r; img[o+1] = g; img[o+2] = b; }
                }

            // overlay 64m ZONE grid lines (red) so the blocky edge's cause is visible
            double zoneTexels = zone / texel;   // 4
            for (int ty = 0; ty < Ht; ty++)
                for (int tx = 0; tx < Wt; tx++)
                {
                    int fx = minfx + tx, fy = minfy + ty;
                    // a fine texel on a zone boundary: its zone differs from the neighbour's zone
                    bool zoneEdge = false;
                    foreach (var (dx,dy) in new[]{(1,0),(0,1)})
                    {
                        int nx=fx+dx, ny=fy+dy; if (nx>=fw||ny>=fh) continue;
                        int za = ((fx/sub)), zb=((nx/sub)); int zc=((fy/sub)), zd=((ny/sub));
                        if (za!=zb || zc!=zd) { zoneEdge = true; }
                    }
                    if (!zoneEdge) continue;
                    int py0 = (Ht - 1 - ty) * scale, px0 = tx * scale;
                    for (int k = 0; k < scale; k++)
                    { int o1 = ((py0) * W + (px0 + k)) * 3; if (o1+2 < img.Length) { img[o1]=110; img[o1+1]=40; img[o1+2]=40; } }
                }

            PngWriter.Write(System.IO.Path.Combine(outDir, "swampedge_window.png"), W, H, img);
        }

        static int[,] FillFadeViz_Strip(int[,] rid, IWorldSampler sampler, int min)
        {
            int gh = rid.GetLength(0), gw = rid.GetLength(1); const double zone = 64.0;
            var land = new int[gh, gw];
            for (int gy = 0; gy < gh; gy++) for (int gx = 0; gx < gw; gx++)
            { int lab = rid[gy, gx]; double wx = (gx + min) * zone, wz = (gy + min) * zone;
              land[gy, gx] = (lab >= 0 && sampler.GetHeight((float)wx, (float)wz) >= HeightScalarField.SeaLevel) ? lab : -1; }
            var comp = new int[gh, gw]; for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++) comp[y, x] = -1;
            var sizes = new List<(int lab, int sz, int id)>(); int cid = 0; var seen = new bool[gh, gw];
            for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++)
            { int lab = land[y, x]; if (lab < 0 || seen[y, x]) continue;
              var st = new Stack<(int, int)>(); st.Push((y, x)); seen[y, x] = true; int n = 0;
              while (st.Count > 0) { var (cy, cx) = st.Pop(); n++; comp[cy, cx] = cid;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int ny = cy + dy, nx = cx + dx;
                  if (ny < 0 || nx < 0 || ny >= gh || nx >= gw || seen[ny, nx] || land[ny, nx] != lab) continue; seen[ny, nx] = true; st.Push((ny, nx)); } }
              sizes.Add((lab, n, cid)); cid++; }
            var biggest = new Dictionary<int, (int sz, int id)>();
            foreach (var c in sizes) if (!biggest.TryGetValue(c.lab, out var bb) || c.sz > bb.sz) biggest[c.lab] = (c.sz, c.id);
            var outg = new int[gh, gw];
            for (int y = 0; y < gh; y++) for (int x = 0; x < gw; x++) { int lab = rid[y, x]; outg[y, x] = (lab >= 0 && biggest.TryGetValue(lab, out var bb) && comp[y, x] == bb.id) ? lab : -1; }
            return outg;
        }
    }
}
