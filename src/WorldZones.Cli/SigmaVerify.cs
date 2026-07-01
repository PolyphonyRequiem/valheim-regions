using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY verification (2026-06-30) that the σ smoother is WIRED THROUGH THE REAL SHIPPED PATH,
    /// not just the isolated primitive. Runs the production <see cref="RegionBoundaryRefiner.RefineBiomeSeams"/>
    /// TWICE on the real seed — once with the legacy default (SmoothingSigmaMeters=0 → despike+Chaikin) and
    /// once with SmoothingSigmaMeters=30 — picks the same mostly-featureless interior seam SeamFork picked
    /// (the wiggle Daniel pointed at), and renders 2-up over the biome backdrop so σ=30's effect is visible
    /// on the exact production output. Left = current (Chaikin), Right = σ=30 m. This is the render that
    /// gates "flip the default" (step 2 in docs/design/region-boundary-negotiation.md).
    /// </summary>
    public static class SigmaVerify
    {
        const double Zone = 64.0, Half = 32.0;
        const int PanelPx = 620;
        const double PadMeters = 80.0;
        const double Sigma = 30.0;

        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== σ-smoother WIRED-PATH verify — seed '{seed}' (LEFT Chaikin default / RIGHT σ={Sigma:F0} m) ===");

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

            var idToKey = new Dictionary<int, string>();
            foreach (ProtoRegion r in world.ProtoResult.Regions)
                if (!idToKey.ContainsKey(r.Id)) idToKey[r.Id] = r.RegionKey;
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);
            var biomeField = new BiomeCategoryField(sampler);

            var keyToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var r in world.Regions) keyToLabel[r.RegionKey] = r.TransientId;

            // ── the two PRODUCTION refiner runs: legacy default vs σ=30, everything else identical ──
            var optLegacy = new SegmentRefineOptions();                                  // SmoothingSigmaMeters = 0
            var optSigma  = new SegmentRefineOptions { SmoothingSigmaMeters = Sigma };
            var arcsLegacy = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField, optLegacy);
            var arcsSigma  = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField, optSigma);
            Console.WriteLine($"interior arcs: legacy={arcsLegacy.Count(a => a.KeyB != null)}  σ={Sigma:F0}={arcsSigma.Count(a => a.KeyB != null)}");

            // ── pick the longest mostly-featureless interior seam (same rule as SeamFork) ──
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
            (string, string) best = default; double bestScore = -1; bool found = false;
            foreach (var kv in pairLen)
            {
                double total = kv.Value, fl = pairFeatureless.GetValueOrDefault(kv.Key);
                if (total < 350) continue;
                if (fl / total < 0.55) continue;
                if (fl > bestScore) { bestScore = fl; best = kv.Key; found = true; }
            }
            if (!found) foreach (var kv in pairFeatureless) if (kv.Value > bestScore) { bestScore = kv.Value; best = kv.Key; found = true; }
            if (!found) { Console.Error.WriteLine("no interior seam found"); return 1; }

            string nameA = world.Regions.FirstOrDefault(r => r.RegionKey == best.Item1)?.Name ?? best.Item1;
            string nameB = world.Regions.FirstOrDefault(r => r.RegionKey == best.Item2)?.Name ?? best.Item2;
            double totLen = pairLen[best], fLen = pairFeatureless.GetValueOrDefault(best);
            Console.WriteLine($"PICKED seam: \"{nameA}\" ↔ \"{nameB}\"  ({totLen:F0} m, {100.0 * fLen / totLen:F0}% featureless)");

            List<RefinedBorder> Sel(IReadOnlyList<RefinedBorder> a) =>
                a.Where(x => x.KeyA == best.Item1 && x.KeyB == best.Item2).ToList();
            var selLegacy = Sel(arcsLegacy); var selSigma = Sel(arcsSigma);

            // wiggle metric (arc / chord; straight = 1.0) on the picked seam
            void Metric(string tag, List<RefinedBorder> sel)
            {
                double arc = 0, chord = 0; int verts = 0;
                foreach (var a in sel)
                {
                    verts += a.Polyline.Count;
                    for (int i = 1; i < a.Polyline.Count; i++) arc += Dist(a.Polyline[i - 1], a.Polyline[i]);
                    if (a.Polyline.Count >= 2) chord += Dist(a.Polyline[0], a.Polyline[^1]);
                }
                Console.WriteLine($"  {tag}: arcs={sel.Count} verts={verts} arcLen={arc:F0}m chord={chord:F0}m wiggle={(chord > 0 ? arc / chord : 0):F3}");
            }
            Metric($"legacy(Chaikin)", selLegacy);
            Metric($"σ={Sigma:F0}m       ", selSigma);

            // ── crop = union bbox of both variants ──
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            foreach (var sel in new[] { selLegacy, selSigma })
                foreach (var a in sel) foreach (WzVec2 v in a.Polyline)
                { x0 = Math.Min(x0, v.X); x1 = Math.Max(x1, v.X); z0 = Math.Min(z0, v.Z); z1 = Math.Max(z1, v.Z); }
            x0 -= PadMeters; x1 += PadMeters; z0 -= PadMeters; z1 += PadMeters;
            double spanX = x1 - x0, spanZ = z1 - z0, span = Math.Max(spanX, spanZ);
            double scale = PanelPx / span;
            int pw = (int)Math.Ceiling(spanX * scale), ph = (int)Math.Ceiling(spanZ * scale);

            int labA = keyToLabel.GetValueOrDefault(best.Item1, -1), labB = keyToLabel.GetValueOrDefault(best.Item2, -1);
            const int gap = 22;
            int W = pw * 2 + gap, H = ph;
            byte[] img = new byte[W * H * 3];
            for (int i = 0; i < img.Length; i++) img[i] = 16;

            Panel(img, W, H, 0,        sampler, grid, min, gw, gh, labA, labB, x0, z1, scale, pw, ph, selLegacy);
            Panel(img, W, H, pw + gap, sampler, grid, min, gw, gh, labA, labB, x0, z1, scale, pw, ph, selSigma);
            for (int y = 0; y < H; y++) for (int x = pw; x < pw + gap; x++) { int o = (y * W + x) * 3; img[o] = 10; img[o + 1] = 11; img[o + 2] = 14; }

            string outPath = Path.Combine(outDir, $"{seed}_sigmaverify.png");
            PngWriter.Write(outPath, W, H, img);
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length / 1024} KB, {W}×{H})");
            File.WriteAllText(Path.Combine(outDir, $"{seed}_sigmaverify.txt"),
                $"seed={seed}\nnames={nameA} <-> {nameB}\nfeatureless_pct={100.0 * fLen / totLen:F0}\npanel_w={pw}\npanel_h={ph}\ngap={gap}\nsigma={Sigma:F0}\n");
            return 0;
        }

        static void Panel(byte[] img, int W, int H, int xoff, IWorldSampler sampler,
            int[,] grid, int min, int gw, int gh, int labA, int labB,
            double x0, double z1, double scale, int pw, int ph, List<RefinedBorder> arcs)
        {
            for (int py = 0; py < ph; py++)
                for (int px = 0; px < pw; px++)
                {
                    double wx = x0 + (px + 0.5) / scale, wz = z1 - (py + 0.5) / scale;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    (byte r, byte g, byte b) c = h < HeightScalarField.SeaLevel
                        ? ((byte)40, (byte)60, (byte)98)
                        : BiomeColor(sampler.GetBiome((float)wx, (float)wz));
                    int zx = (int)Math.Round(wx / Zone) - min, zy = (int)Math.Round(wz / Zone) - min;
                    int lab = (zx < 0 || zy < 0 || zx >= gw || zy >= gh) ? -1 : grid[zy, zx];
                    if (lab == labA) c = Mix(c, (255, 200, 90), 0.18);
                    else if (lab == labB) c = Mix(c, (120, 160, 255), 0.18);
                    int o = (py * W + (xoff + px)) * 3;
                    img[o] = c.r; img[o + 1] = c.g; img[o + 2] = c.b;
                }
            foreach (RefinedBorder a in arcs)
            {
                for (int i = 1; i < a.Polyline.Count; i++)
                    Line(img, W, H, xoff, pw, ph, ToPx(a.Polyline[i - 1], x0, z1, scale), ToPx(a.Polyline[i], x0, z1, scale), (245, 245, 245), 3);
                for (int i = 1; i < a.Polyline.Count; i++)
                    Line(img, W, H, xoff, pw, ph, ToPx(a.Polyline[i - 1], x0, z1, scale), ToPx(a.Polyline[i], x0, z1, scale), (10, 10, 10), 1);
            }
        }

        static (int x, int y) ToPx(WzVec2 v, double x0, double z1, double scale)
            => ((int)Math.Round((v.X - x0) * scale), (int)Math.Round((z1 - v.Z) * scale));

        static void Line(byte[] img, int W, int H, int xoff, int pw, int ph, (int x, int y) a, (int x, int y) b, (int r, int g, int bl) col, int rad)
        {
            int x0 = a.x, y0 = a.y, x1 = b.x, y1 = b.y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            while (true)
            {
                for (int oy = -rad; oy <= rad; oy++) for (int ox = -rad; ox <= rad; ox++)
                {
                    if (ox * ox + oy * oy > rad * rad + 1) continue;
                    int X = x0 + ox, Y = y0 + oy; if (X < 0 || Y < 0 || X >= pw || Y >= ph) continue;
                    int o = (Y * W + (xoff + X)) * 3; img[o] = (byte)col.r; img[o + 1] = (byte)col.g; img[o + 2] = (byte)col.bl;
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err; if (e2 > -dy) { err -= dy; x0 += sx; } if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        static (byte r, byte g, byte b) BiomeColor(BiomeType bt) => bt switch
        {
            BiomeType.Meadows => (96, 124, 64), BiomeType.Swamp => (84, 80, 54), BiomeType.Mountain => (188, 192, 200),
            BiomeType.BlackForest => (52, 84, 60), BiomeType.Plains => (164, 150, 88), BiomeType.AshLands => (138, 64, 50),
            BiomeType.DeepNorth => (200, 214, 226), BiomeType.Mistlands => (104, 90, 114), _ => (70, 70, 76),
        };
        static (byte r, byte g, byte b) Mix((byte r, byte g, byte b) a, (int r, int g, int b) t, double k)
            => ((byte)(a.r + (t.r - a.r) * k), (byte)(a.g + (t.g - a.g) * k), (byte)(a.b + (t.b - a.b) * k));
        static double Dist(WzVec2 a, WzVec2 b) { double dx = a.X - b.X, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dz * dz); }
    }
}
