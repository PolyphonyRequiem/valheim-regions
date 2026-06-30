using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY (2026-06-29) — measure the terrain-HEIGHT distribution of SWAMP, to set the swamp
    /// land-floor from data instead of the inherited 22 m guess. Samples swamp biome terrain at the FINE
    /// (16 m) grid across the whole world and reports the height histogram relative to the 30 m waterline:
    /// how far below sea level swamp dips, where the mass sits, and what floor would capture X% of swamp
    /// land. The current classifier calls a zone Land at ≥30 and rescues swamp at ≥22 — this shows whether
    /// 22 is too high (drops walkable swamp) or too low (claims open water).
    /// </summary>
    public static class SwampHeightViz
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== Swamp height distribution — seed '{seed}' ===");
            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            const double sea = 30.0;

            // Sample the whole world at 16 m where biome == Swamp; bucket terrain height.
            // World radius ~10500 m → step 16 m. Only swamp-biome samples counted.
            const double step = 16.0, R = 10500.0;
            var hist = new SortedDictionary<int, long>();   // height bucket (1 m) → count
            long total = 0; double minH = double.MaxValue, maxH = double.MinValue, sum = 0;
            long belowSea = 0, below22 = 0, below20 = 0, below15 = 0, below10 = 0, aboveSea = 0;

            for (double wz = -R; wz <= R; wz += step)
                for (double wx = -R; wx <= R; wx += step)
                {
                    if (wx * wx + wz * wz > R * R) continue;
                    if (sampler.GetBiome((float)wx, (float)wz) != BiomeType.Swamp) continue;
                    double h = sampler.GetHeight((float)wx, (float)wz);
                    total++; sum += h; if (h < minH) minH = h; if (h > maxH) maxH = h;
                    int b = (int)Math.Floor(h);
                    hist[b] = hist.TryGetValue(b, out var c) ? c + 1 : 1;
                    if (h >= sea) aboveSea++; else belowSea++;
                    if (h < 22) below22++; if (h < 20) below20++; if (h < 15) below15++; if (h < 10) below10++;
                }

            if (total == 0) { Console.WriteLine("no swamp found"); return 0; }
            Console.WriteLine($"SWAMP terrain samples (16 m): {total}");
            Console.WriteLine($"  height range: min={minH:F1} m  max={maxH:F1} m  mean={sum/total:F1} m  (waterline = {sea})");
            Console.WriteLine($"  above waterline (≥30):  {aboveSea} ({100.0*aboveSea/total:F1}%)");
            Console.WriteLine($"  below waterline (<30):  {belowSea} ({100.0*belowSea/total:F1}%)  ← swamp that straddles/sits under water");
            Console.WriteLine();
            Console.WriteLine("  CUMULATIVE swamp land captured by floor F (samples with height ≥ F):");
            foreach (double f in new[] { 30.0, 28.0, 26.0, 25.0, 24.0, 23.0, 22.0, 21.0, 20.0, 18.0, 16.0, 14.0, 12.0, 10.0 })
            {
                long ge = 0; foreach (var kv in hist) if (kv.Key >= f) ge += kv.Value;
                Console.WriteLine($"    floor {f,5:F0} m → captures {ge,7} ({100.0*ge/total:F1}% of swamp){(Math.Abs(f-22)<0.5?"   ← current 22 m":"")}");
            }
            Console.WriteLine();
            Console.WriteLine("  HISTOGRAM (1 m buckets, height → count, * = relative):");
            long peak = 0; foreach (var kv in hist) if (kv.Value > peak) peak = kv.Value;
            // print compact: only buckets from min..max, scaled bar
            for (int b = (int)Math.Floor(minH); b <= (int)Math.Ceiling(maxH); b++)
            {
                long c = hist.TryGetValue(b, out var v) ? v : 0;
                int bar = peak > 0 ? (int)(40.0 * c / peak) : 0;
                string mark = b == 30 ? " <WATERLINE" : b == 22 ? " <floor22" : "";
                Console.WriteLine($"    {b,4} m | {new string('#', bar),-40} {c}{mark}");
            }
            return 0;
        }
    }
}
