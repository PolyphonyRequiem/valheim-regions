using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY decision-render (2026-06-30) for the interior-seam SHAPE fork. The shared-border root
    /// fix (one refined primitive per region pair, consumed by ink+both fills) is SETTLED; the only open
    /// fork is what that single border looks like on the ~76% of interior seams with NO biome feature
    /// under them (measured by seamdiag). This renders the three candidate shapes 3-up on the REAL walk
    /// seed so Daniel directs from the picture, not prose:
    ///   A = CURRENT (live RefineBiomeSeams): snap to biome flip within 40 m, then despike+Chaikin →
    ///       on a featureless seam this rounds the 64 m zone staircase into a soft WIGGLE (artifact).
    ///   B = GEODESIC (proposed): keep the same hug anchors where a feature exists, but run a STRAIGHT
    ///       line between anchors (and between junctions) where there is none — honest "decreed border".
    ///   C = LONGER-REACH HUG: same as A but snap reach 40 m → 120 m, to see whether reaching further
    ///       finds a real feature to follow or just darts to an unrelated distant biome edge.
    /// Auto-picks the longest MOSTLY-FEATURELESS interior seam (the wiggle Daniel pointed at), crops to
    /// it, and paints a biome backdrop so "is there actually a feature here" is visible under each line.
    /// Emits a 3-up PNG (PngWriter) + metrics; a Python post-step labels + thumbnails before vision.
    /// </summary>
    public static class SeamFork
    {
        // ── tunables for the render ──
        const double Zone = 64.0, Half = 32.0;
        const double SnapReachA = 40.0;     // live default (SegmentRefineOptions.MaxDisplacement)
        const double SnapReachC = 120.0;    // longer-reach experiment (3× zone)
        const double MarchStep = 4.0;
        const int PanelPx = 560;            // larger-axis target per panel (upscaled)
        const double PadMeters = 80.0;

        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== Seam-shape FORK render — seed '{seed}' (A=wiggle / B=geodesic / C=reach120) ===");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
            });
            int[,] grid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = grid.GetLength(0), gw = grid.GetLength(1);
            Console.WriteLine($"regions={world.Regions.Count} grid={gw}x{gh} minIndex={min}");

            var idToKey = new Dictionary<int, string>();
            foreach (ProtoRegion r in world.ProtoResult.Regions)
                if (!idToKey.ContainsKey(r.Id)) idToKey[r.Id] = r.RegionKey;
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);
            var biomeField = new BiomeCategoryField(sampler);

            var keyToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RegionInfo r in world.Regions) keyToLabel[r.RegionKey] = r.TransientId;

            // ── Variant A (faithful ship path) and C (longer reach) from PRODUCTION refiner ──
            var arcsA = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField);
            var optC = new SegmentRefineOptions { MaxDisplacement = SnapReachC };
            var arcsC = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField, optC);
            // ── Variant B (proposed geodesic) from a local chainer over the SAME grouping ──
            var arcsB = GeodesicSeams(graph, biomeField, SnapReachA);

            Console.WriteLine($"interior arcs:  A(ship)={CountInterior(arcsA)}  B(geodesic)={CountInterior(arcsB)}  C(reach120)={CountInterior(arcsC)}");

            // ── Pick the seam: per region-pair, sum featureless length (same biome both sides) ──
            // This is the wiggle Daniel pointed at — a long seam with mostly nothing to hug.
            var pairLen = new Dictionary<(string, string), double>();
            var pairFeatureless = new Dictionary<(string, string), double>();
            foreach (BorderSegment seg in graph.Segments)
            {
                if (seg.IsCoastline) continue;
                var key = (seg.KeyA, seg.KeyB);
                double mx = (seg.A.X + seg.B.X) * 0.5, mz = (seg.A.Z + seg.B.Z) * 0.5;
                double dx = seg.B.X - seg.A.X, dz = seg.B.Z - seg.A.Z;
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-9) continue;
                double nx = -dz / l, nz = dx / l;
                int cP = biomeField.CategoryAt(mx + nx * Half, mz + nz * Half);
                int cN = biomeField.CategoryAt(mx - nx * Half, mz - nz * Half);
                pairLen[key] = pairLen.GetValueOrDefault(key) + l;
                if (cP == cN) pairFeatureless[key] = pairFeatureless.GetValueOrDefault(key) + l;
            }

            (string, string) bestPair = default; double bestScore = -1; bool found = false;
            foreach (var kv in pairLen)
            {
                double total = kv.Value;
                double featureless = pairFeatureless.GetValueOrDefault(kv.Key);
                if (total < 350) continue;                       // a real, visible seam (≥~5 zones)
                if (featureless / total < 0.55) continue;        // mostly featureless → shows the thesis
                if (featureless > bestScore) { bestScore = featureless; bestPair = kv.Key; found = true; }
            }
            if (!found)
            {
                // fallback: max featureless length regardless of ratio
                foreach (var kv in pairFeatureless)
                    if (kv.Value > bestScore) { bestScore = kv.Value; bestPair = kv.Key; found = true; }
            }
            if (!found) { Console.Error.WriteLine("no interior seam found"); return 1; }

            double totLen = pairLen.GetValueOrDefault(bestPair), fLen = pairFeatureless.GetValueOrDefault(bestPair);
            string nameA = world.Regions.FirstOrDefault(r => r.RegionKey == bestPair.Item1)?.Name ?? bestPair.Item1;
            string nameB = world.Regions.FirstOrDefault(r => r.RegionKey == bestPair.Item2)?.Name ?? bestPair.Item2;
            Console.WriteLine();
            Console.WriteLine($"PICKED seam: {bestPair.Item1} | {bestPair.Item2}  (\"{nameA}\" ↔ \"{nameB}\")");
            Console.WriteLine($"  total seam length {totLen:F0} m, featureless (same biome both sides) {fLen:F0} m = {100.0*fLen/totLen:F0}%");

            // ── Gather the picked pair's arcs in each variant ──
            List<RefinedBorder> SelPair(IReadOnlyList<RefinedBorder> arcs) =>
                arcs.Where(a => a.KeyA == bestPair.Item1 && a.KeyB == bestPair.Item2).ToList();
            var selA = SelPair(arcsA); var selB = SelPair(arcsB); var selC = SelPair(arcsC);

            // ── Genuine feature anchors: re-run the snap on the RAW (pre-smooth) chain for this pair,
            //    recording the world points where a biome flip was actually found within reach 40 m.
            //    These are the ONLY places any variant truly hugs terrain; drawn identically on all three
            //    panels so the viewer sees real features are sparse and the A/C wiggle is between them.
            var pairSegs = graph.Segments.Where(s => !s.IsCoastline && s.KeyA == bestPair.Item1 && s.KeyB == bestPair.Item2).ToList();
            var anchors = new List<WzVec2>();
            foreach (List<WzVec2> chain in Chain(pairSegs))
            {
                int n = chain.Count;
                for (int i = 1; i < n - 1; i++)
                {
                    WzVec2 prev = chain[i - 1], next = chain[i + 1];
                    double tx = next.X - prev.X, tz = next.Z - prev.Z;
                    double tl = Math.Sqrt(tx * tx + tz * tz);
                    if (tl < 1e-9) continue;
                    double nx = -tz / tl, nz = tx / tl;
                    if (TrySnapBiome(chain[i].X, chain[i].Z, nx, nz, biomeField, SnapReachA, out double s) && Math.Abs(s) > 1e-6)
                        anchors.Add(new WzVec2(chain[i].X + s * nx, chain[i].Z + s * nz));
                }
            }
            Console.WriteLine($"  genuine biome-flip anchors on this seam (reach {SnapReachA:F0} m): {anchors.Count}");

            // wiggliness metric: total arc length / total chord length (straight = 1.0)
            void Metric(string tag, List<RefinedBorder> sel)
            {
                double arc = 0, chord = 0; int verts = 0, hugs = 0;
                foreach (var a in sel)
                {
                    verts += a.Polyline.Count; if (a.Hugged) hugs++;
                    for (int i = 1; i < a.Polyline.Count; i++)
                        arc += Dist(a.Polyline[i - 1], a.Polyline[i]);
                    if (a.Polyline.Count >= 2) chord += Dist(a.Polyline[0], a.Polyline[^1]);
                }
                Console.WriteLine($"  {tag}: arcs={sel.Count} verts={verts} hugged={hugs}  arcLen={arc:F0}m chord={chord:F0}m  wiggle(arc/chord)={(chord>0?arc/chord:0):F3}");
            }
            Console.WriteLine();
            Console.WriteLine("── per-variant shape of the picked seam ──");
            Metric("A wiggle  ", selA);
            Metric("B geodesic", selB);
            Metric("C reach120", selC);

            // ── crop window = union bbox of all three variants' polylines for this pair ──
            double wx0 = double.MaxValue, wx1 = double.MinValue, wz0 = double.MaxValue, wz1 = double.MinValue;
            foreach (var sel in new[] { selA, selB, selC })
                foreach (var a in sel)
                    foreach (WzVec2 v in a.Polyline)
                    { wx0 = Math.Min(wx0, v.X); wx1 = Math.Max(wx1, v.X); wz0 = Math.Min(wz0, v.Z); wz1 = Math.Max(wz1, v.Z); }
            wx0 -= PadMeters; wx1 += PadMeters; wz0 -= PadMeters; wz1 += PadMeters;
            double spanX = wx1 - wx0, spanZ = wz1 - wz0, span = Math.Max(spanX, spanZ);
            double scale = PanelPx / span;                 // px per metre
            int pw = (int)Math.Ceiling(spanX * scale), ph = (int)Math.Ceiling(spanZ * scale);
            Console.WriteLine();
            Console.WriteLine($"crop {spanX:F0}×{spanZ:F0} m → panel {pw}×{ph}px (scale {scale:F3} px/m)");

            int labA = keyToLabel.GetValueOrDefault(bestPair.Item1, -1);
            int labB = keyToLabel.GetValueOrDefault(bestPair.Item2, -1);

            // ── render 3-up ──
            const int gap = 22;
            int W = pw * 3 + gap * 2, H = ph;
            byte[] img = new byte[W * H * 3];
            // backdrop fill = dark
            for (int i = 0; i < img.Length; i++) img[i] = 16;

            RenderPanel(img, W, H, 0,            sampler, grid, min, gw, gh, labA, labB, wx0, wz1, scale, pw, ph, selA, anchors);
            RenderPanel(img, W, H, pw + gap,     sampler, grid, min, gw, gh, labA, labB, wx0, wz1, scale, pw, ph, selB, anchors);
            RenderPanel(img, W, H, 2*(pw + gap), sampler, grid, min, gw, gh, labA, labB, wx0, wz1, scale, pw, ph, selC, anchors);
            // dividers
            for (int g = 0; g < 2; g++)
                for (int y = 0; y < H; y++) for (int x = pw + g*(pw+gap); x < pw + g*(pw+gap) + gap; x++)
                { int o=(y*W+x)*3; img[o]=10; img[o+1]=11; img[o+2]=14; }

            string outPath = Path.Combine(outDir, $"{seed}_seamfork.png");
            PngWriter.Write(outPath, W, H, img);
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length/1024} KB, {W}×{H})");

            // metrics sidecar for the Python labeller
            string meta = Path.Combine(outDir, $"{seed}_seamfork.txt");
            File.WriteAllText(meta,
                $"seed={seed}\npair={bestPair.Item1}|{bestPair.Item2}\nnames={nameA} <-> {nameB}\n" +
                $"featureless_pct={100.0*fLen/totLen:F0}\npanel_w={pw}\npanel_h={ph}\ngap={gap}\n");
            return 0;
        }

        // ───────────────────────── rendering ─────────────────────────

        static void RenderPanel(byte[] img, int W, int H, int xoff, IWorldSampler sampler,
            int[,] grid, int min, int gw, int gh, int labA, int labB,
            double wx0, double wz1, double scale, int pw, int ph,
            List<RefinedBorder> arcs, List<WzVec2> anchors)
        {
            // biome backdrop + faint region tint
            for (int py = 0; py < ph; py++)
            {
                for (int px = 0; px < pw; px++)
                {
                    double wx = wx0 + (px + 0.5) / scale;
                    double wz = wz1 - (py + 0.5) / scale;            // flip Z: north up
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    (byte r, byte g, byte b) c = h < HeightScalarField.SeaLevel
                        ? ((byte)40, (byte)60, (byte)98)             // water
                        : BiomeColor(sampler.GetBiome((float)wx, (float)wz));
                    // faint tint of the two seam regions so the territory reads
                    int zx = (int)Math.Round(wx / Zone) - min, zy = (int)Math.Round(wz / Zone) - min;
                    int lab = (zx < 0 || zy < 0 || zx >= gw || zy >= gh) ? -1 : grid[zy, zx];
                    if (lab == labA) c = Mix(c, (255, 200, 90), 0.18);
                    else if (lab == labB) c = Mix(c, (120, 160, 255), 0.18);
                    int o = ((py) * W + (xoff + px)) * 3;
                    img[o] = c.r; img[o + 1] = c.g; img[o + 2] = c.b;
                }
            }
            // the seam line: white casing + black core so it reads over any backdrop
            foreach (RefinedBorder a in arcs)
            {
                for (int i = 1; i < a.Polyline.Count; i++)
                {
                    var p0 = ToPx(a.Polyline[i - 1], wx0, wz1, scale);
                    var p1 = ToPx(a.Polyline[i], wx0, wz1, scale);
                    DrawLine(img, W, H, xoff, pw, ph, p0, p1, (245,245,245), 3); // casing
                }
                for (int i = 1; i < a.Polyline.Count; i++)
                {
                    var p0 = ToPx(a.Polyline[i - 1], wx0, wz1, scale);
                    var p1 = ToPx(a.Polyline[i], wx0, wz1, scale);
                    DrawLine(img, W, H, xoff, pw, ph, p0, p1, (10,10,10), 1);    // core
                }
            }
            // GENUINE feature anchors (same set on every panel): the only places terrain is actually hugged.
            foreach (WzVec2 v in anchors)
            {
                var pp = ToPx(v, wx0, wz1, scale);
                Dot(img, W, H, xoff, pw, ph, pp, (235,40,40), 3);
            }
        }

        static (int x, int y) ToPx(WzVec2 v, double wx0, double wz1, double scale)
            => ((int)Math.Round((v.X - wx0) * scale), (int)Math.Round((wz1 - v.Z) * scale));

        static void DrawLine(byte[] img, int W, int H, int xoff, int pw, int ph,
            (int x, int y) a, (int x, int y) b, (int r, int g, int bl) col, int rad)
        {
            int x0=a.x,y0=a.y,x1=b.x,y1=b.y;
            int dx=Math.Abs(x1-x0), dy=Math.Abs(y1-y0), sx=x0<x1?1:-1, sy=y0<y1?1:-1, err=dx-dy;
            while (true)
            {
                for (int oy=-rad;oy<=rad;oy++) for (int ox=-rad;ox<=rad;ox++)
                {
                    if (ox*ox+oy*oy>rad*rad+1) continue;
                    int X=x0+ox, Y=y0+oy;
                    if (X<0||Y<0||X>=pw||Y>=ph) continue;
                    int o=((Y)*W+(xoff+X))*3; img[o]=(byte)col.r; img[o+1]=(byte)col.g; img[o+2]=(byte)col.bl;
                }
                if (x0==x1 && y0==y1) break;
                int e2=2*err; if (e2>-dy){err-=dy;x0+=sx;} if (e2<dx){err+=dx;y0+=sy;}
            }
        }

        static void Dot(byte[] img, int W, int H, int xoff, int pw, int ph, (int x,int y) p, (int r,int g,int b) col, int rad)
        {
            for (int oy=-rad;oy<=rad;oy++) for (int ox=-rad;ox<=rad;ox++)
            {
                if (ox*ox+oy*oy>rad*rad+1) continue;
                int X=p.x+ox, Y=p.y+oy; if (X<0||Y<0||X>=pw||Y>=ph) continue;
                int o=((Y)*W+(xoff+X))*3; img[o]=(byte)col.r; img[o+1]=(byte)col.g; img[o+2]=(byte)col.b;
            }
        }

        static (byte r, byte g, byte b) BiomeColor(BiomeType bt) => bt switch
        {
            BiomeType.Meadows     => (96, 124, 64),
            BiomeType.Swamp       => (84, 80, 54),
            BiomeType.Mountain    => (188, 192, 200),
            BiomeType.BlackForest => (52, 84, 60),
            BiomeType.Plains      => (164, 150, 88),
            BiomeType.AshLands    => (138, 64, 50),
            BiomeType.DeepNorth   => (200, 214, 226),
            BiomeType.Mistlands   => (104, 90, 114),
            _                     => (70, 70, 76),
        };

        static (byte r, byte g, byte b) Mix((byte r, byte g, byte b) a, (int r, int g, int b) t, double k)
            => ((byte)(a.r + (t.r - a.r) * k), (byte)(a.g + (t.g - a.g) * k), (byte)(a.b + (t.b - a.b) * k));

        static double Dist(WzVec2 a, WzVec2 b) { double dx=a.X-b.X, dz=a.Z-b.Z; return Math.Sqrt(dx*dx+dz*dz); }
        static int CountInterior(IReadOnlyList<RefinedBorder> arcs) => arcs.Count(a => a.KeyA != null && a.KeyB != null);

        // ─────────────────── variant B: geodesic seams ───────────────────
        // Mirrors production grouping+chaining, but on each chain: keep the same hug ANCHORS where a
        // biome feature exists, and run a STRAIGHT line between anchors (and between the fixed junction
        // endpoints) where there is none. Junctions (chain endpoints) stay put — they're shared.
        static IReadOnlyList<RefinedBorder> GeodesicSeams(RegionBoundaryGraph graph, ICategoryField field, double reach)
        {
            var byPair = new Dictionary<(string, string), List<BorderSegment>>();
            foreach (BorderSegment seg in graph.Segments)
            {
                if (seg.IsCoastline) continue;
                var key = (seg.KeyA, seg.KeyB);
                if (!byPair.TryGetValue(key, out var list)) { list = new List<BorderSegment>(); byPair[key] = list; }
                list.Add(seg);
            }
            var result = new List<RefinedBorder>();
            foreach (var kv in byPair)
            {
                foreach (List<WzVec2> chain in Chain(kv.Value))
                {
                    int n = chain.Count;
                    var anchors = new List<WzVec2> { chain[0] };           // junction start (fixed)
                    bool anyHug = false;
                    for (int i = 1; i < n - 1; i++)
                    {
                        WzVec2 prev = chain[i - 1], next = chain[i + 1];
                        double tx = next.X - prev.X, tz = next.Z - prev.Z;
                        double tl = Math.Sqrt(tx * tx + tz * tz);
                        if (tl < 1e-9) continue;
                        double nx = -tz / tl, nz = tx / tl;
                        if (TrySnapBiome(chain[i].X, chain[i].Z, nx, nz, field, reach, out double s) && Math.Abs(s) > 1e-6)
                        { anchors.Add(new WzVec2(chain[i].X + s * nx, chain[i].Z + s * nz)); anyHug = true; }
                    }
                    anchors.Add(chain[n - 1]);                             // junction end (fixed)
                    // straight between anchors == just the anchor list as the polyline
                    result.Add(new RefinedBorder(anchors, kv.Key.Item1, kv.Key.Item2, anyHug));
                }
            }
            return result;
        }

        static bool TrySnapBiome(double px, double pz, double nx, double nz, ICategoryField field, double reach, out double bestS)
        {
            bestS = 0; int c0 = field.CategoryAt(px, pz); double best = double.MaxValue; bool found = false;
            foreach (int dir in new[] { 1, -1 })
            {
                int prevCat = c0; double prevS = 0;
                for (double step = MarchStep; step <= reach + 1e-9; step += MarchStep)
                {
                    double s = dir * step;
                    int cat = field.CategoryAt(px + s * nx, pz + s * nz);
                    if (cat != prevCat)
                    {
                        double lo = prevS, hi = s;
                        for (int it = 0; it < 18; it++)
                        { double mid = 0.5 * (lo + hi); int cm = field.CategoryAt(px + mid * nx, pz + mid * nz); if (cm == c0) lo = mid; else hi = mid; }
                        double sc = 0.5 * (lo + hi);
                        if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; }
                        break;
                    }
                    prevCat = cat; prevS = s;
                }
            }
            return found;
        }

        // local copy of the production endpoint chainer (deterministic, matches grouping order)
        static IEnumerable<List<WzVec2>> Chain(List<BorderSegment> segs)
        {
            (long, long) Key(WzVec2 p) => ((long)Math.Round(p.X * 100), (long)Math.Round(p.Z * 100));
            var adj = new Dictionary<(long, long), List<int>>();
            var used = new bool[segs.Count];
            for (int i = 0; i < segs.Count; i++)
                foreach (var k in new[] { Key(segs[i].A), Key(segs[i].B) })
                { if (!adj.TryGetValue(k, out var l)) { l = new List<int>(); adj[k] = l; } l.Add(i); }

            for (int start = 0; start < segs.Count; start++)
            {
                if (used[start]) continue;
                used[start] = true;
                var chain = new LinkedList<WzVec2>();
                chain.AddLast(segs[start].A); chain.AddLast(segs[start].B);
                Extend(chain, adj, segs, used, true); Extend(chain, adj, segs, used, false);
                yield return new List<WzVec2>(chain);
            }

            static void Extend(LinkedList<WzVec2> chain, Dictionary<(long, long), List<int>> adj, List<BorderSegment> segs, bool[] used, bool tail)
            {
                (long, long) Key(WzVec2 p) => ((long)Math.Round(p.X * 100), (long)Math.Round(p.Z * 100));
                while (true)
                {
                    WzVec2 endp = tail ? chain.Last.Value : chain.First.Value;
                    if (!adj.TryGetValue(Key(endp), out var cand)) break;
                    int next = -1; foreach (int si in cand) if (!used[si]) { next = si; break; }
                    if (next < 0) break;
                    used[next] = true;
                    WzVec2 a = segs[next].A, b = segs[next].B;
                    WzVec2 far = (Key(a) == Key(endp)) ? b : a;
                    if (tail) chain.AddLast(far); else chain.AddFirst(far);
                }
            }
        }
    }
}
