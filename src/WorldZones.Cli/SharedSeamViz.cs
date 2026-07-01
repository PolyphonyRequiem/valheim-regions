using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY render (2026-07-01) — fork B proof. Emits, for one seed: (a) the reassembled fill rings
    /// built from the SHARED seams (SharedSeamBoundary), and (b) the shared seams themselves. Overlaid on an
    /// interior seam, the fill-ring edge and the seam (= the ink) are the SAME curve — the whole point of B,
    /// vs today's ~16 m weave. Also prints the measured separation. Not part of the shipped surface.
    /// </summary>
    public static class SharedSeamViz
    {
        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== Shared-seam viz — seed '{seed}' (fork B: fill == ink) ===");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
            });

            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var coast = new HeightScalarField(sampler);
            var flip = new BiomeCategoryField(sampler);
            SharedSeamSet seams = SharedSeamSet.Build(graph, coast, flip);
            IReadOnlyList<SharedSeamRing> rings = SharedSeamBoundary.Build(seams, graph, out var failed);

            Console.WriteLine($"regions={world.Regions.Count}  seams={seams.Seams.Count} "
                            + $"(interior={seams.Seams.Count(s => !s.IsCoast)}, coast={seams.Seams.Count(s => s.IsCoast)})  "
                            + $"reassembled rings={rings.Count}  failed regions={failed.Count}");

            // Measure fill-vs-ink separation the OLD way (independent ring refine) vs the NEW way (shared).
            // NEW: a shared seam vertex should be ON both regions' reassembled rings → 0. Report worst.
            var ringVerts = rings.GroupBy(r => r.RegionKey)
                .ToDictionary(g => g.Key, g => new HashSet<(long, long)>(
                    g.SelectMany(r => r.Vertices).Select(v => (Q(v.X), Q(v.Z)))));
            int checkedV = 0, off = 0;
            foreach (SharedSeam s in seams.Seams.Where(s => !s.IsCoast))
                foreach (string k in new[] { s.KeyA, s.KeyB })
                    if (k != null && ringVerts.TryGetValue(k, out var vs))
                        foreach (WzVec2 p in s.Refined) { checkedV++; if (!vs.Contains((Q(p.X), Q(p.Z)))) off++; }
            Console.WriteLine($"fill==ink check: {checkedV} interior seam verts, {off} NOT on the reassembled ring "
                            + $"({(off == 0 ? "PASS ✓ same curve, 0 separation" : "FAIL")})");

            // Emit JSON for the render: reassembled rings + interior seams, cropped by the longest interior seam.
            string outPath = Path.Combine(outDir, $"{seed}_sharedseam.json");
            var sb = new StringBuilder(8 * 1024 * 1024);
            sb.Append('{').Append($"\"seed\":\"{Esc(seed)}\",\"zone\":64,");
            sb.Append("\"rings\":[");
            bool fr = true;
            foreach (SharedSeamRing r in rings)
            {
                int lbl = world.Regions.FirstOrDefault(x => x.RegionKey == r.RegionKey)?.TransientId ?? -1;
                if (!fr) sb.Append(','); fr = false;
                sb.Append("{\"label\":").Append(lbl).Append(",\"hole\":").Append(r.IsHole ? "true" : "false").Append(",\"p\":[");
                bool fp = true; foreach (WzVec2 v in r.Vertices) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append("],\"seams\":[");
            bool fs = true;
            foreach (SharedSeam s in seams.Seams.Where(s => !s.IsCoast))
            {
                if (!fs) sb.Append(','); fs = false;
                sb.Append("{\"p\":[");
                bool fp = true; foreach (WzVec2 v in s.Refined) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append("]}");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length / 1024} KB)");
            return 0;
        }

        private static long Q(double v) => (long)Math.Round(v * 1000);   // mm quantize (rings built from same pts)
        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
