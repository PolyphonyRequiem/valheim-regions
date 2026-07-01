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
    /// THROWAWAY diagnostic (2026-07-01) — the FORK-A render gate. Builds the region FILL boundary TWICE on
    /// the IDENTICAL seed/graph/fields — once with the legacy Chaikin fill (SmoothingSigmaMeters = 0) and once
    /// with the new closed-loop Gaussian fill (σ = 30 m) — and emits BOTH ring sets plus the interior ink arcs
    /// as one JSON, so an offline render can put the current stepped fill next to the σ=30 fill on the SAME
    /// interior region-vs-region seam. This is the "show not tell" gate for whether the fill stops stepping.
    ///
    /// It also prints the headless jaggedness delta (mean per-vertex midpoint deviation over the large smoothed
    /// rings) so the numbers back the picture. Pure diagnostic; not part of the shipped surface.
    /// </summary>
    public static class RingSigmaViz
    {
        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== Ring σ fill viz — seed '{seed}' (fork A: closed-loop Gaussian fill) ===");

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
            const double zone = 64.0;
            Console.WriteLine($"regions={world.Regions.Count} grid={gw}x{gh} minIndex={min}");

            var idToKey = new Dictionary<int, string>();
            foreach (ProtoRegion r in world.ProtoResult.Regions)
                if (!idToKey.ContainsKey(r.Id)) idToKey[r.Id] = r.RegionKey;
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);

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

            // Fill built BOTH ways on the identical graph/fields — only SmoothingSigmaMeters differs.
            RefinedRegionBoundary fillChaikin = RefinedRegionBoundary.Build(graph, keyToLabel, ridAt, ringCoast, ringSeam,
                new RingRefineOptions { SmoothingSigmaMeters = 0.0 });
            RefinedRegionBoundary fillSigma = RefinedRegionBoundary.Build(graph, keyToLabel, ridAt, ringCoast, ringSeam,
                new RingRefineOptions { SmoothingSigmaMeters = 30.0 });

            Console.WriteLine($"chaikin fill: {fillChaikin.Rings.Count} rings (rolledSelfInt={fillChaikin.RolledBackCount}, "
                            + $"rolledToRaw={fillChaikin.RolledBackToRawCount}, skippedSmall={fillChaikin.SkippedSmallCount})");
            Console.WriteLine($"σ=30   fill: {fillSigma.Rings.Count} rings (rolledSelfInt={fillSigma.RolledBackCount}, "
                            + $"rolledToRaw={fillSigma.RolledBackToRawCount}, skippedSmall={fillSigma.SkippedSmallCount})");

            // Headless jaggedness delta over the large smoothed OUTER rings present in both.
            var chOuter = new Dictionary<string, RefinedRing>(StringComparer.Ordinal);
            foreach (RefinedRing r in fillChaikin.Rings)
                if (r.Outcome == RingRefineOutcome.Smoothed && !r.IsHole && !chOuter.ContainsKey(r.RegionKey))
                    chOuter[r.RegionKey] = r;
            double sumCh = 0, sumSig = 0; int compared = 0;
            foreach (RefinedRing s in fillSigma.Rings)
            {
                if (s.Outcome != RingRefineOutcome.Smoothed || s.IsHole) continue;
                if (!chOuter.TryGetValue(s.RegionKey, out RefinedRing c)) continue;
                if (s.Vertices.Count < 8 || c.Vertices.Count < 8) continue;
                sumCh += MeanMidDev(c.Vertices); sumSig += MeanMidDev(s.Vertices); compared++;
            }
            if (compared > 0)
                Console.WriteLine($"mean per-vertex jaggedness over {compared} large rings: "
                                + $"Chaikin={sumCh / compared:F3} m → σ=30={sumSig / compared:F3} m "
                                + $"({100.0 * (1 - (sumSig / compared) / (sumCh / compared)):F0}% smoother)");

            // Interior ink arcs (region pairs) so the render can pick the same seam SeamDiag showed.
            var biomeField = new BiomeCategoryField(sampler);  // ink coast iso lives in HeightScalarField default
            var inkSeamArcs = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField);

            string outPath = Path.Combine(outDir, $"{seed}_ringsigma.json");
            var sb = new StringBuilder(16 * 1024 * 1024);
            sb.Append('{');
            sb.Append($"\"seed\":\"{Esc(seed)}\",\"size\":{gw},\"minIndex\":{min},\"zoneMeters\":64,");
            AppendRings(sb, "fillChaikin", fillChaikin, keyToLabel); sb.Append(',');
            AppendRings(sb, "fillSigma", fillSigma, keyToLabel); sb.Append(',');
            sb.Append("\"inkArcs\":[");
            bool firstA = true;
            foreach (RefinedBorder a in inkSeamArcs)
            {
                if (a.KeyA == null || a.KeyB == null) continue;
                if (!firstA) sb.Append(','); firstA = false;
                sb.Append("{\"p\":[");
                bool fp = true;
                foreach (WzVec2 v in a.Polyline) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append("],");
            sb.Append("\"regions\":[");
            bool firstReg = true;
            foreach (RegionInfo r in world.Regions)
            {
                if (!firstReg) sb.Append(','); firstReg = false;
                sb.Append('{').Append($"\"id\":{r.TransientId},\"domBiome\":\"{r.DominantBiome}\"").Append('}');
            }
            sb.Append("]}");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length / 1024} KB)");
            return 0;
        }

        private static void AppendRings(StringBuilder sb, string field, RefinedRegionBoundary fill,
                                        Dictionary<string, int> keyToLabel)
        {
            sb.Append('"').Append(field).Append("\":[");
            bool first = true;
            foreach (RefinedRing rr in fill.Rings)
            {
                int label = keyToLabel.TryGetValue(rr.RegionKey, out var lb) ? lb : -1;
                if (!first) sb.Append(','); first = false;
                sb.Append("{\"label\":").Append(label).Append(",\"hole\":").Append(rr.IsHole ? "true" : "false").Append(",\"p\":[");
                bool fp = true;
                foreach (WzVec2 v in rr.Vertices) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append(']');
        }

        private static double MeanMidDev(IReadOnlyList<WzVec2> v)
        {
            double s = 0; int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 prev = v[(i - 1 + n) % n], next = v[(i + 1) % n];
                double mx = (prev.X + next.X) * 0.5, mz = (prev.Z + next.Z) * 0.5;
                double dx = v[i].X - mx, dz = v[i].Z - mz;
                s += Math.Sqrt(dx * dx + dz * dz);
            }
            return n > 0 ? s / n : 0;
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
