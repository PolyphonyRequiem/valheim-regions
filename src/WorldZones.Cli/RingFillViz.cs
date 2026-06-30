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
    /// SPIKE (2026-06-29) — fill from the AUTHORITATIVE REFINED RING instead of the 64 m zone grid.
    /// Daniel's swamp screenshot showed the raster fill edge is a 64 m zone-membership staircase (72%
    /// zone-limited), NOT a fill-resolution problem — the fix is to rasterize the refined ring polygon
    /// (smooth, contour-hugging, sub-zone) as the fill mask. This renders a real region 2-up:
    /// LEFT  = current raster fill (RegionFillMaskBaker, 64 m zone membership + 16 m height clip)
    /// RIGHT = ring fill (point-in-refined-ring, holes subtracted)
    /// on the same window so Daniel can judge whether the ring edge is the win. Honest about what the
    /// ring does NOT fix: land the REGION never incorporated (membership) stays unpainted either way.
    /// </summary>
    public static class RingFillViz
    {
        public static int Run(string seed, string outDir)
        {
            Console.WriteLine($"=== Ring-fill spike — seed '{seed}' ===");
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

            // Current raster fill (the deployed path).
            var baker = new RegionFillMaskBaker(sampler, HeightScalarField.SeaLevel, 22.0);
            int[,] rasterFill = baker.Bake(mainGrid, min, sub);
            int fh = rasterFill.GetLength(0), fw = rasterFill.GetLength(1);

            // Authoritative refined ring boundary (the thing we built + proved watertight this session).
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(mainGrid, min, BuildIdToKey(world));
            var idToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in world.Regions) idToLabel[r.RegionKey] = r.TransientId;
            int ridW = mainGrid.GetLength(1), ridH = mainGrid.GetLength(0);
            RegionRingRefiner.RegionIdAt ridAt = (wx, wz) =>
            { int zx = (int)Math.Round(wx / zone) - min, zy = (int)Math.Round(wz / zone) - min;
              return (zx < 0 || zy < 0 || zx >= ridW || zy >= ridH) ? -1 : mainGrid[zy, zx]; };
            // Snap coastal ring edges to the 30 m waterline (so the ring fill stops at the real coast),
            // seam edges to the biome flip. (Default refiner uses 25 m CoastIso; here we want the waterline.)
            var coastField = new HeightScalarField(sampler, HeightScalarField.SeaLevel);  // iso = 30
            var seamField = new BiomeCategoryField(sampler);
            RefinedRegionBoundary boundary = RefinedRegionBoundary.Build(graph, idToLabel, ridAt, coastField, seamField);
            Console.WriteLine($"refined rings: {boundary.Rings.Count} (rolledToRaw={boundary.RolledBackToRawCount}, skippedSmall={boundary.SkippedSmallCount})");

            // Rasterize the ring fill: PRODUCTION baker (RegionRingFillBaker) so the spike verifies the
            // exact code that ships, not a parallel copy.
            var prodBaker = new RegionRingFillBaker(boundary, idToLabel);
            int[,] ringFill = prodBaker.Bake(ridH, ridW, min, sub);

            // Pick the same Astley swamp region Daniel saw (lower-left, biggest swamp).
            RegionInfo pick = PickLowerLeftSwamp(world, mainGrid, min, zone, gw, gh, ridW, ridH);
            if (pick == null) { Console.Error.WriteLine("no swamp region found"); return 1; }
            Console.WriteLine($"region: key={pick.RegionKey} name=\"{pick.Name}\" biome={pick.DominantBiome} centroid=({pick.CentroidX:F0},{pick.CentroidZ:F0})");

            // Audit: how many texels does each fill paint for this region?
            int label = pick.TransientId;
            long rasterN = 0, ringN = 0, both = 0, ringOnly = 0, rasterOnly = 0;
            for (int fy = 0; fy < fh; fy++) for (int fx = 0; fx < fw; fx++)
            {
                bool a = rasterFill[fy, fx] == label, b = ringFill[fy, fx] == label;
                if (a) rasterN++; if (b) ringN++;
                if (a && b) both++; else if (b) ringOnly++; else if (a) rasterOnly++;
            }
            Console.WriteLine($"  raster fill texels: {rasterN}");
            Console.WriteLine($"  ring   fill texels: {ringN}");
            Console.WriteLine($"  agree: {both}  ring-only(ring claims, raster didn't): {ringOnly}  raster-only(raster claimed, ring didn't): {rasterOnly}");

            RenderTwoUp(outDir, sampler, rasterFill, ringFill, label, ox, oz, texel, fw, fh);
            Console.WriteLine($"  → {outDir}/ringfill_2up.png (LEFT raster fill, RIGHT ring fill; olive=region, blue=water, grey=other land)");
            return 0;
        }

        static IReadOnlyDictionary<int, string> BuildIdToKey(RegionWorld world)
        {
            var d = new Dictionary<int, string>();
            foreach (var r in world.Regions) if (!d.ContainsKey(r.TransientId)) d[r.TransientId] = r.RegionKey;
            return d;
        }

        // Point-in-polygon fill: a texel is in region R if inside R's outer ring AND not inside any hole.
        static int[,] RasterizeRings(RefinedRegionBoundary boundary, Dictionary<string,int> idToLabel,
            double ox, double oz, double texel, int fw, int fh)
        {
            var outg = new int[fh, fw];
            for (int y = 0; y < fh; y++) for (int x = 0; x < fw; x++) outg[y, x] = -1;

            // group rings by region key
            var byKey = new Dictionary<string, List<RefinedRing>>(StringComparer.Ordinal);
            foreach (var rr in boundary.Rings)
            {
                if (!byKey.TryGetValue(rr.RegionKey, out var l)) { l = new List<RefinedRing>(); byKey[rr.RegionKey] = l; }
                l.Add(rr);
            }

            foreach (var kv in byKey)
            {
                if (!idToLabel.TryGetValue(kv.Key, out int label)) continue;
                var outers = kv.Value.Where(r => !r.IsHole).ToList();
                var holes = kv.Value.Where(r => r.IsHole).ToList();
                foreach (var outer in outers)
                {
                    // bbox of this outer ring in texel coords
                    double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                    foreach (var p in outer.Vertices) { if (p.X<minX)minX=p.X; if (p.X>maxX)maxX=p.X; if (p.Z<minZ)minZ=p.Z; if (p.Z>maxZ)maxZ=p.Z; }
                    int fx0 = Math.Max(0, (int)((minX - ox) / texel) - 1), fx1 = Math.Min(fw-1, (int)((maxX - ox) / texel) + 1);
                    int fy0 = Math.Max(0, (int)((minZ - oz) / texel) - 1), fy1 = Math.Min(fh-1, (int)((maxZ - oz) / texel) + 1);
                    for (int fy = fy0; fy <= fy1; fy++)
                        for (int fx = fx0; fx <= fx1; fx++)
                        {
                            double wx = ox + (fx + 0.5) * texel, wz = oz + (fy + 0.5) * texel;
                            if (!PointInRing(outer.Vertices, wx, wz)) continue;
                            bool inHole = false;
                            foreach (var hole in holes) if (PointInRing(hole.Vertices, wx, wz)) { inHole = true; break; }
                            if (!inHole) outg[fy, fx] = label;
                        }
                }
            }
            return outg;
        }

        static bool PointInRing(IReadOnlyList<WzVec2> v, double px, double pz)
        {
            bool inside = false; int n = v.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = v[i].X, zi = v[i].Z, xj = v[j].X, zj = v[j].Z;
                if (((zi > pz) != (zj > pz)) && (px < (xj - xi) * (pz - zi) / (zj - zi) + xi)) inside = !inside;
            }
            return inside;
        }

        static RegionInfo PickLowerLeftSwamp(RegionWorld world, int[,] mainGrid, int min, double zone, int gw, int gh, int ridW, int ridH)
        {
            double worldCx = (min + gw / 2.0) * zone, worldCz = (min + gh / 2.0) * zone;
            RegionInfo pick = null; long best = -1;
            foreach (var r in world.Regions.Where(r => r.DominantBiome == BiomeType.Swamp))
            {
                if (r.CentroidX > worldCx || r.CentroidZ > worldCz) continue;
                long a = 0; for (int gy=0;gy<ridH;gy++) for (int gx=0;gx<ridW;gx++) if (mainGrid[gy,gx]==r.TransientId) a++;
                if (a > best) { best = a; pick = r; }
            }
            return pick ?? world.Regions.FirstOrDefault(r => r.DominantBiome == BiomeType.Swamp);
        }

        static void RenderTwoUp(string outDir, IWorldSampler sampler, int[,] raster, int[,] ring, int label,
            double ox, double oz, double texel, int fw, int fh)
        {
            // bbox over union of both fills for this region
            int minfx=fw,minfy=fh,maxfx=0,maxfy=0; bool any=false;
            for (int fy=0;fy<fh;fy++) for (int fx=0;fx<fw;fx++)
                if (raster[fy,fx]==label || ring[fy,fx]==label) { any=true; if(fx<minfx)minfx=fx; if(fx>maxfx)maxfx=fx; if(fy<minfy)minfy=fy; if(fy>maxfy)maxfy=fy; }
            if (!any) return;
            int pad=10; minfx=Math.Max(0,minfx-pad); minfy=Math.Max(0,minfy-pad); maxfx=Math.Min(fw-1,maxfx+pad); maxfy=Math.Min(fh-1,maxfy+pad);
            int Wt=maxfx-minfx+1, Ht=maxfy-minfy+1;
            int scale=Math.Max(1, 760/Math.Max(Wt,Ht));
            int pw=Wt*scale, ph=Ht*scale, gapW=24;
            int W=pw*2+gapW, H=ph;
            byte[] img=new byte[W*H*3];

            void Panel(int[,] fillGrid, int xoff)
            {
                for (int ty=0;ty<Ht;ty++) for (int tx=0;tx<Wt;tx++)
                {
                    int fx=minfx+tx, fy=minfy+ty;
                    double wx=ox+(fx+0.5)*texel, wz=oz+(fy+0.5)*texel;
                    double h=sampler.GetHeight((float)wx,(float)wz);
                    byte r,g,b;
                    if (h<HeightScalarField.SeaLevel){ double d=Math.Min(1,(HeightScalarField.SeaLevel-h)/40.0); r=(byte)(20+12*(1-d)); g=(byte)(40+30*(1-d)); b=(byte)(80+60*(1-d)); }
                    else { r=g=b=70; }
                    if (fillGrid[fy,fx]==label){ r=150; g=175; b=80; }
                    else if (fillGrid[fy,fx]>=0){ r=80; g=80; b=95; }
                    int py0=(Ht-1-ty)*scale, px0=xoff+tx*scale;
                    for (int dy=0;dy<scale;dy++) for (int dx=0;dx<scale;dx++){ int o=((py0+dy)*W+(px0+dx))*3; img[o]=r; img[o+1]=g; img[o+2]=b; }
                }
            }
            Panel(raster, 0);
            Panel(ring, pw+gapW);
            // gap divider
            for (int y=0;y<H;y++) for (int x=pw;x<pw+gapW;x++){ int o=(y*W+x)*3; img[o]=14; img[o+1]=15; img[o+2]=20; }
            PngWriter.Write(System.IO.Path.Combine(outDir,"ringfill_2up.png"), W, H, img);
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
