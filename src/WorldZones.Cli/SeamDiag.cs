using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY diagnostic (2026-06-30) for the two reported interior-seam bugs:
    ///   #1 "wiggly ink — is it finding terrain features to hug?"
    ///   #2 "region colours jump back and forth across the line — wrong raster behaviour"
    /// Runs the EXACT live overlay refine path (RegionOverlayPlugin.CacheOverlayGeometry) headlessly:
    ///   INK  = RefineCoastlinesSmoothed ∪ RefineBiomeSeams (per region-PAIR arcs)
    ///   FILL = RefinedRegionBoundary.Build → per-region RINGS (what the ring-fill baker rasterises)
    /// Then measures, for the INTERIOR (region-vs-region) seams ONLY:
    ///   (1a) how many interior seam sample points have a BIOME FLIP within MaxDisplacement (=huggable)
    ///        vs none (same biome both sides → nothing to hug → Chaikin invents a wiggle).
    ///   (1b) the per-arc hug flag spread from the actual RefineBiomeSeams output.
    ///   (2)  whether the FILL ring boundary and the INK arc are the SAME curve along a shared seam
    ///        (max/mean separation in metres). Big separation ⇒ colour crosses the ink line.
    /// Emits geometry JSON for an offline render overlay (fill rings + interior ink arcs).
    /// </summary>
    public static class SeamDiag
    {
        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== Seam diagnostic — seed '{seed}' (live overlay refine path, headless) ===");

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
            const double zone = 64.0, half = 32.0;
            Console.WriteLine($"regions={world.Regions.Count} grid={gw}x{gh} minIndex={min}");

            // ── Build the graph exactly as the live plugin does (idToKey from ProtoResult) ──
            var idToKey = new Dictionary<int, string>();
            foreach (ProtoRegion r in world.ProtoResult.Regions)
                if (!idToKey.ContainsKey(r.Id)) idToKey[r.Id] = r.RegionKey;
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);

            var heightField = new HeightScalarField(sampler);            // CoastIso = 25 m (the ink coast)
            var biomeField = new BiomeCategoryField(sampler);

            // ── INK path: refined arcs (coast ∪ biome-seam) ──
            var inkArcs = new List<RefinedBorder>();
            inkArcs.AddRange(RegionBoundaryRefiner.RefineCoastlinesSmoothed(graph, heightField));
            var biomeSeamArcs = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField);
            inkArcs.AddRange(biomeSeamArcs);

            // ── (1a) Is there a BIOME FEATURE to hug along interior seams? ──
            // Walk every interior (KeyB != null) raw seam segment, sample its midpoint, and march ±40 m
            // perpendicular looking for a biome-category flip. No flip in reach ⇒ nothing to hug.
            int interiorSegs = 0, huggableSegs = 0, sameBiomeBothSides = 0;
            const double maxDisp = 40.0, marchStep = 4.0;
            foreach (BorderSegment seg in graph.Segments)
            {
                if (seg.IsCoastline) continue;       // interior only
                interiorSegs++;
                double mx = (seg.A.X + seg.B.X) * 0.5, mz = (seg.A.Z + seg.B.Z) * 0.5;
                double dx = seg.B.X - seg.A.X, dz = seg.B.Z - seg.A.Z;
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l < 1e-9) continue;
                double nx = -dz / l, nz = dx / l;
                int c0 = biomeField.CategoryAt(mx, mz);
                // biome immediately on each side (32 m in, the zone centres this seam divides)
                int cP = biomeField.CategoryAt(mx + nx * half, mz + nz * half);
                int cN = biomeField.CategoryAt(mx - nx * half, mz - nz * half);
                if (cP == cN) sameBiomeBothSides++;
                bool flip = false;
                foreach (int dir in new[] { 1, -1 })
                {
                    int prev = c0;
                    for (double s = marchStep; s <= maxDisp + 1e-9; s += marchStep)
                    {
                        int cat = biomeField.CategoryAt(mx + dir * s * nx, mz + dir * s * nz);
                        if (cat != prev) { flip = true; break; }
                        prev = cat;
                    }
                    if (flip) break;
                }
                if (flip) huggableSegs++;
            }
            Console.WriteLine();
            Console.WriteLine("── #1  INTERIOR-SEAM HUGGABILITY (does the ink have a feature to follow?) ──");
            Console.WriteLine($"interior seam segments: {interiorSegs}");
            Console.WriteLine($"  with a biome FLIP within {maxDisp:F0} m (huggable): {huggableSegs} "
                            + $"({Pct(huggableSegs, interiorSegs)})");
            Console.WriteLine($"  SAME biome on both sides at the seam (no local flip): {sameBiomeBothSides} "
                            + $"({Pct(sameBiomeBothSides, interiorSegs)})");

            // ── (1b) Per-arc hug flag from the ACTUAL RefineBiomeSeams output ──
            int arcs = 0, arcsHugged = 0, arcVerts = 0;
            foreach (RefinedBorder a in biomeSeamArcs)
            {
                arcs++;
                if (a.Hugged) arcsHugged++;
                arcVerts += a.Polyline.Count;
            }
            Console.WriteLine();
            Console.WriteLine("── #1b  RefineBiomeSeams OUTPUT (the ink arcs actually drawn) ──");
            Console.WriteLine($"biome-seam arcs: {arcs}, of which anyHug=true: {arcsHugged} ({Pct(arcsHugged, arcs)}); "
                            + $"total vertices: {arcVerts}");
            Console.WriteLine("  (anyHug=false arc ⇒ EVERY vertex stayed on the 64 m lattice; the wiggle you see");
            Console.WriteLine("   on those is pure Chaikin rounding of the zone staircase — not terrain-hugging.)");

            // ── FILL path: the refined rings the ring-fill baker rasterises ──
            var keyToLabel = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (RegionInfo r in world.Regions) keyToLabel[r.RegionKey] = r.TransientId;
            RegionRingRefiner.RegionIdAt ridAt = (wx, wz) =>
            {
                int zx = (int)Math.Round(wx / zone) - min;
                int zy = (int)Math.Round(wz / zone) - min;
                return (zx < 0 || zy < 0 || zx >= gw || zy >= gh) ? -1 : grid[zy, zx];
            };
            var ringCoast = new HeightScalarField(sampler, HeightScalarField.SeaLevel);  // 30 m waterline
            var ringSeam = new BiomeCategoryField(sampler);
            RefinedRegionBoundary fill = RefinedRegionBoundary.Build(graph, keyToLabel, ridAt, ringCoast, ringSeam);
            Console.WriteLine();
            Console.WriteLine("── FILL ring boundary (what colours the map) ──");
            Console.WriteLine($"refined rings: {fill.Rings.Count}, rolledBackSelfIntersect={fill.RolledBackCount}, "
                            + $"rolledToRaw={fill.RolledBackToRawCount}, skippedSmall={fill.SkippedSmallCount}");

            // ── (2) FILL-vs-INK agreement along a shared interior seam ──
            // For each interior ink arc (region pair A|B), sample its vertices and measure the distance to
            // the NEAREST point of region A's fill ring boundary. If fill==ink, this is ~0. A large value
            // means the colour boundary (fill ring) and the drawn line (ink arc) are different curves, so
            // the colour weaves across the line. Sample a bounded subset for speed.
            double sumSep = 0, maxSep = 0; long sepN = 0;
            int arcsChecked = 0;
            foreach (RefinedBorder a in biomeSeamArcs)
            {
                if (a.KeyA == null || a.KeyB == null) continue;
                // Collect the fill-ring vertices for BOTH regions (the colour edge is whichever ring is nearer).
                var ringPts = new List<WzVec2>();
                foreach (RefinedRing rr in fill.RingsFor(a.KeyA)) ringPts.AddRange(rr.Vertices);
                foreach (RefinedRing rr in fill.RingsFor(a.KeyB)) ringPts.AddRange(rr.Vertices);
                if (ringPts.Count == 0) continue;
                arcsChecked++;
                foreach (WzVec2 p in a.Polyline)
                {
                    double best = double.MaxValue;
                    // nearest ring VERTEX (cheap upper bound on curve distance; rings are dense ~16-64 m)
                    foreach (WzVec2 q in ringPts)
                    {
                        double d2 = (p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z);
                        if (d2 < best) best = d2;
                    }
                    double d = Math.Sqrt(best);
                    sumSep += d; sepN++;
                    if (d > maxSep) maxSep = d;
                }
                if (arcsChecked >= 200) break;   // bound the O(n²)
            }
            Console.WriteLine();
            Console.WriteLine("── #2  FILL-vs-INK SEPARATION along interior seams (colour edge vs drawn line) ──");
            Console.WriteLine($"sampled {sepN} ink vertices across {arcsChecked} interior arcs: "
                            + $"mean nearest-fill-ring-vertex distance = {(sepN > 0 ? sumSep / sepN : 0):F1} m, "
                            + $"max = {maxSep:F1} m");
            Console.WriteLine("  (NOTE: this is distance to the nearest ring VERTEX, an upper bound — but the FILL");
            Console.WriteLine("   colour is decided by point-in-polygon of that ring, so any gap = colour spill past the ink.)");

            // ── Emit geometry JSON for the offline overlay render ──
            string outPath = Path.Combine(outDir, $"{seed}_seamgeom.json");
            var sb = new StringBuilder(8 * 1024 * 1024);
            sb.Append('{');
            sb.Append($"\"seed\":\"{Esc(seed)}\",\"size\":{gw},\"minIndex\":{min},\"zoneMeters\":64,");
            // interior ink arcs only (the ones in question)
            sb.Append("\"inkArcs\":[");
            bool firstA = true;
            foreach (RefinedBorder a in biomeSeamArcs)
            {
                if (a.KeyA == null || a.KeyB == null) continue;
                if (!firstA) sb.Append(','); firstA = false;
                sb.Append("{\"hug\":").Append(a.Hugged ? "true" : "false").Append(",\"p\":[");
                bool fp = true;
                foreach (WzVec2 v in a.Polyline) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append("],");
            // fill rings (outer + holes), each with region label
            sb.Append("\"fillRings\":[");
            bool firstR = true;
            foreach (RefinedRing rr in fill.Rings)
            {
                int label = keyToLabel.TryGetValue(rr.RegionKey, out var lb) ? lb : -1;
                if (!firstR) sb.Append(','); firstR = false;
                sb.Append("{\"label\":").Append(label).Append(",\"hole\":").Append(rr.IsHole ? "true" : "false").Append(",\"p\":[");
                bool fp = true;
                foreach (WzVec2 v in rr.Vertices) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append("],");
            // region biome lookup for colouring
            sb.Append("\"regions\":[");
            bool firstReg = true;
            foreach (RegionInfo r in world.Regions)
            {
                if (!firstReg) sb.Append(','); firstReg = false;
                sb.Append('{').Append($"\"id\":{r.TransientId},\"domBiome\":\"{r.DominantBiome}\"").Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine();
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length / 1024} KB)");
            return 0;
        }

        private static string Pct(long a, long b) => b > 0 ? $"{100.0 * a / b:F1}%" : "n/a";
        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
