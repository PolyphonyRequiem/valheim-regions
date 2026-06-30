using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THE THREAD-2 GATE (2026-06-30) — same-window swamp-floor A/B for Daniel's felt-judgment. Renders
    /// N (default 4) swamp-HEAVY, non-runt regions, each 2-up: LEFT = current floor 22 m, RIGHT = proposed
    /// 28.5 m, on the IDENTICAL world window so the floor is the only variable (the handoff's gate: the
    /// earlier ambiguous render was the 17-zone runt, magnified — NOT representative; see
    /// render-debugging-method.md "CHECK IF YOUR RENDER WINDOW IS A RUNT").
    ///
    /// Per-texel terrain classification at 16 m (NOT region fill — this isolates the classification change
    /// the floor actually drives; region growth is downstream and would confound a fill A/B):
    ///   land test @F:  height ≥ 30  OR  (isSwamp ∧ height ≥ F)     (mirrors ZoneClassifier.ClassifyWithSwampFloor)
    ///   GREEN   = land at this floor, non-swamp
    ///   OLIVE   = land at this floor, swamp (the rescued bog)
    ///   AMBER   = land@22 but WATER@28.5 — the DELTA (only appears on the right panel) = what 28.5 sheds
    ///   BLUE    = water (depth-shaded)
    /// Amber over the RIGHT (28.5) panel is the whole question: if it hugs the coast = clean trim (deploy);
    /// if it eats the inland body = too aggressive (revert to 22). A coastal-vs-inland tally per region is
    /// printed so the picture and the numbers agree (don't trust one signal — the session's hard lesson).
    /// </summary>
    public static class SwampFloorAB
    {
        const double Zone = 64.0, Texel = 16.0, Sea = 30.0;
        const int Sub = 4;                 // 16 m texels per 64 m zone
        const double Cur = 22.0;
        static double Prop = 28.5;         // proposed floor under test (set from CLI arg)

        public static int Run(string seed, string outDir, int count, double proposedFloor = 28.5)
        {
            Prop = proposedFloor;
            Console.WriteLine($"=== swamp-floor A/B (current {Cur} | proposed {Prop}) — seed '{seed}' ===");
            System.IO.Directory.CreateDirectory(outDir);

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);

            // Build once (live-overlay opts) just to get the region SET + their zone bboxes for window
            // selection + size/rank. The floors are applied per-TEXEL below, not via separate builds — the
            // A/B is a classification overlay on a fixed window, so one build is correct and faster.
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false, UseFeatureAwareBorders = true,
                ComputeRegionInfo = true, Namer = new MultiSchemaRegionNamer(),
            });

            // World size distribution (for the rank line — proves the picked region is NOT a runt).
            var areasSorted = world.Regions.Select(r => r.LandZones).OrderBy(n => n).ToList();
            int median = areasSorted.Count > 0 ? areasSorted[areasSorted.Count / 2] : 0;

            // Rank swamp regions by SWAMP ZONE COUNT (heaviest swamp first), require non-runt (≥ median/2)
            // so we judge representative bodies, not magnified speckle.
            int floorReq = Math.Max(25, median / 2);
            var swampHeavy = world.Regions
                .Where(r => r.DominantBiome == BiomeType.Swamp)
                .Select(r => (r, swamp: r.BiomeZoneCounts.TryGetValue(BiomeType.Swamp, out int sc) ? sc : 0))
                .Where(t => t.r.LandZones >= floorReq)
                .OrderByDescending(t => t.swamp)
                .Take(count)
                .ToList();

            if (swampHeavy.Count == 0)
            {
                Console.Error.WriteLine("no swamp-heavy non-runt regions found");
                return 1;
            }

            Console.WriteLine($"world: {world.Regions.Count} regions, median land {median} zones. " +
                              $"Picked {swampHeavy.Count} swamp-heavy (≥{floorReq} land zones):\n");

            var outPaths = new List<string>();
            foreach (var (r, swamp) in swampHeavy)
            {
                int rank = areasSorted.Count - areasSorted.FindIndex(a => a >= r.LandZones); // 1 = biggest
                Console.WriteLine($"  {r.RegionKey} \"{r.Name}\"  land={r.LandZones}z swamp={swamp}z " +
                                  $"({100.0 * swamp / Math.Max(1, r.SampledLandZones):F0}% swamp)  rank ~{rank}/{world.Regions.Count}");
                string path = RenderRegion(seed, outDir, sampler, r, swamp);
                outPaths.Add(path);
            }

            Console.WriteLine("\nRendered:");
            foreach (var p in outPaths) Console.WriteLine($"  {p}");
            return 0;
        }

        static string RenderRegion(string seed, string outDir, PortWorldSampler sampler, RegionInfo r, int swampZones)
        {
            // Window = region zone bbox (+pad), in world metres. Same window for both panels.
            double wMinX = (r.MinZoneX - 2) * Zone, wMaxX = (r.MaxZoneX + 2) * Zone;
            double wMinZ = (r.MinZoneZ - 2) * Zone, wMaxZ = (r.MaxZoneZ + 2) * Zone;

            int Wt = (int)Math.Round((wMaxX - wMinX) / Texel);
            int Ht = (int)Math.Round((wMaxZ - wMinZ) / Texel);

            // Precompute the window's terrain once: height, swamp flag, and land masks at both floors.
            var height = new double[Ht, Wt];
            var swamp = new bool[Ht, Wt];
            var land22 = new bool[Ht, Wt];
            var landProp = new bool[Ht, Wt];
            for (int ty = 0; ty < Ht; ty++)
                for (int tx = 0; tx < Wt; tx++)
                {
                    double wx = wMinX + (tx + 0.5) * Texel, wz = wMinZ + (ty + 0.5) * Texel;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    bool sw = sampler.GetBiome((float)wx, (float)wz) == BiomeType.Swamp;
                    height[ty, tx] = h; swamp[ty, tx] = sw;
                    land22[ty, tx]   = h >= Sea || (sw && h >= Cur);
                    landProp[ty, tx] = h >= Sea || (sw && h >= Prop);
                }

            // HONEST coastal-vs-interior: flood from the window BORDER through {water@22 ∪ shed} cells.
            // A shed cell the flood REACHES is the shoreline retreating inward (coastal). A shed cell the
            // flood CANNOT reach is walled off by land that survives@prop → a hole in the body (interior).
            // "Open water" is seeded from the window edge, the same way ocean reaches a coast.
            bool Shed(int ty, int tx) => land22[ty, tx] && !landProp[ty, tx];
            var reached = new bool[Ht, Wt];
            var stack = new Stack<(int ty, int tx)>();
            void Seed(int ty, int tx)
            {
                if (ty < 0 || tx < 0 || ty >= Ht || tx >= Wt || reached[ty, tx]) return;
                // passable = was-water@22 OR is a shed cell (the retreat corridor)
                if (land22[ty, tx] && !Shed(ty, tx)) return; // surviving land blocks the flood
                reached[ty, tx] = true; stack.Push((ty, tx));
            }
            for (int tx = 0; tx < Wt; tx++) { Seed(0, tx); Seed(Ht - 1, tx); }
            for (int ty = 0; ty < Ht; ty++) { Seed(ty, 0); Seed(ty, Wt - 1); }
            while (stack.Count > 0)
            {
                var (cy, cx) = stack.Pop();
                Seed(cy - 1, cx); Seed(cy + 1, cx); Seed(cy, cx - 1); Seed(cy, cx + 1);
            }

            int scale = Math.Max(1, 760 / Math.Max(Wt, Ht));
            int panelW = Wt * scale, panelH = Ht * scale;
            const int gap = 12;
            int W = panelW * 2 + gap, H = panelH;
            byte[] img = new byte[W * H * 3];
            for (int i = 0; i < img.Length; i += 3) { img[i] = 18; img[i + 1] = 18; img[i + 2] = 22; }

            long shedTotal = 0, shedCoastal = 0, shedInterior = 0;
            long interiorRescuedBy275 = 0; // interior cells with height in [27.5, prop) — saved by floor 27.5

            for (int ty = 0; ty < Ht; ty++)
                for (int tx = 0; tx < Wt; tx++)
                {
                    bool shed = Shed(ty, tx);
                    bool interior = shed && !reached[ty, tx];
                    if (shed)
                    {
                        shedTotal++;
                        if (interior) { shedInterior++; if (height[ty, tx] >= 27.5) interiorRescuedBy275++; }
                        else shedCoastal++;
                    }

                    // LEFT panel = current 22; RIGHT panel = proposed, shed split coastal(amber)/interior(red)
                    PaintBlock(img, W, H, 0, tx, ty, scale, Ht, ColourFor(land22[ty, tx], swamp[ty, tx], height[ty, tx], 0));
                    int shedClass = !shed ? 0 : (interior ? 2 : 1);
                    PaintBlock(img, W, H, panelW + gap, tx, ty, scale, Ht,
                        ColourFor(landProp[ty, tx], swamp[ty, tx], height[ty, tx], shedClass));
                }

            double pctInt = shedTotal > 0 ? 100.0 * shedInterior / shedTotal : 0;
            Console.WriteLine($"      shed {shedTotal}tx:  coastal(amber)={shedCoastal} ({100 - pctInt:F0}%)  " +
                              $"INTERIOR(red)={shedInterior} ({pctInt:F0}%)  " +
                              $"{(shedTotal == 0 ? "(no change)" : pctInt < 10 ? "→ clean coastal trim" : pctInt < 30 ? "→ mostly coastal, some interior speckle" : "→ ⚠ substantial interior holes")}");
            if (shedInterior > 0)
                Console.WriteLine($"        of the {shedInterior} interior: {interiorRescuedBy275} sit in [27.5,{Prop}) " +
                                  $"→ floor 27.5 would rescue them ({(shedInterior > 0 ? 100.0 * interiorRescuedBy275 / shedInterior : 0):F0}% of interior holes)");

            string safe = r.RegionKey.Replace('.', '_');
            string path = System.IO.Path.Combine(outDir, $"swampAB_{safe}.png");
            PngWriter.Write(path, W, H, img);
            return path;
        }

        // Colour map. shedClass: 0=not shed, 1=coastal retreat (amber), 2=interior hole (red).
        static (byte r, byte g, byte b) ColourFor(bool land, bool isSwamp, double height, int shedClass)
        {
            if (shedClass == 1) return (235, 165, 40);     // amber = coastal shoreline retreat
            if (shedClass == 2) return (225, 45, 45);      // red   = interior hole (surrounded by surviving land)
            if (land) return isSwamp ? ((byte)150, (byte)168, (byte)78) : ((byte)70, (byte)150, (byte)70);
            double d = Math.Min(1.0, (Sea - height) / 40.0);   // water, depth-shaded
            return ((byte)(24 + 10 * (1 - d)), (byte)(44 + 26 * (1 - d)), (byte)(86 + 52 * (1 - d)));
        }

        static void PaintBlock(byte[] img, int W, int H, int xOffset, int tx, int ty, int scale, int Ht, (byte r, byte g, byte b) c)
        {
            int py0 = (Ht - 1 - ty) * scale, px0 = xOffset + tx * scale;
            for (int dy = 0; dy < scale; dy++)
                for (int dx = 0; dx < scale; dx++)
                {
                    int px = px0 + dx, py = py0 + dy;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    int o = (py * W + px) * 3;
                    img[o] = c.r; img[o + 1] = c.g; img[o + 2] = c.b;
                }
        }
    }
}

