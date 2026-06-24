using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// Deep single-type RNG-stream probe for the location port. Reproduces the EXACT per-candidate
    /// draw sequence of ZoneSystem.GenerateLocationsTimeSliced for ONE location type and prints the
    /// first N accepted placements, so we can diff against the real .db candidate-by-candidate and
    /// localize where (and whether) the RNG stream diverges — the way to resolve insideUnitCircle.
    /// </summary>
    public static class LocationProbe
    {
        public static int Run(string seed, string prefabName, string cataloguePath, string oraclePath,
                              string strategyName, int showN)
        {
            var catalogue = LocationValidation.LoadCatalogue(cataloguePath);
            var cfg = catalogue.First(c => c.PrefabName == prefabName);
            int worldSeed = seed.GetStableHashCode();
            var gen = new WorldGenerator(seed);
            var strat = (InsideUnitCircleStrategy)Enum.Parse(typeof(InsideUnitCircleStrategy), strategyName, true);

            // real placements for this prefab
            var oracle = System.Text.Json.JsonSerializer.Deserialize<OracleFile>(
                System.IO.File.ReadAllText(oraclePath),
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var reals = (oracle?.locations ?? Array.Empty<OracleLoc>())
                .Where(l => l.prefab == prefabName).Select(l => (l.x, l.z)).ToList();

            Console.WriteLine($"=== PROBE {prefabName} seed={seed} strategy={strat} ===");
            Console.WriteLine($"cfg: q={cfg.Quantity} prio={cfg.Prioritized} biome={cfg.Biome} extR={cfg.ExteriorRadius} " +
                              $"alt={cfg.MinAltitude}..{cfg.MaxAltitude} terrDelta={cfg.MinTerrainDelta}..{cfg.MaxTerrainDelta} " +
                              $"centerFirst={cfg.CenterFirst} minDistSim={cfg.MinDistanceFromSimilar}");
            Console.WriteLine($"real placements: {reals.Count}");

            var rng = new UnityRandom(worldSeed + prefabName.GetStableHashCode());
            float maxRadius = Math.Max(cfg.ExteriorRadius, cfg.InteriorRadius);
            int attempts = cfg.Prioritized ? 200000 : 100000;
            float maxRange = cfg.CenterFirst ? cfg.MinDistance : 10000f;
            var placed = new List<(float x, float z)>();
            var occupied = new HashSet<(int, int)>();

            int i = 0, shown = 0;
            while (i < attempts && placed.Count < cfg.Quantity)
            {
                var (zx, zy) = GetRandomZone(rng, maxRange);
                if (cfg.CenterFirst) maxRange += 1f;
                if (!occupied.Contains((zx, zy)))
                {
                    var (zcx, zcz) = (zx * 64f, zy * 64f);
                    int ba = gen.GetBiomeArea(zcx, zcz);
                    if ((cfg.BiomeArea & ba) != 0)
                    {
                        for (int j = 0; j < 20; j++)
                        {
                            var (px, pz) = GetRandomPointInZone(rng, zx, zy, maxRadius);
                            float mag = (float)Math.Sqrt((double)px * px + (double)pz * pz);
                            if (cfg.MinDistance != 0f && mag < cfg.MinDistance) continue;
                            if (cfg.MaxDistance != 0f && mag > cfg.MaxDistance) continue;
                            var biome = gen.GetBiome(px, pz);
                            if (((int)cfg.Biome & (int)biome) == 0) continue;
                            float gy = gen.GetHeight(px, pz);
                            float alt = (float)((double)gy - 30.0);
                            if (alt < cfg.MinAltitude || alt > cfg.MaxAltitude) continue;
                            if (cfg.InForest)
                            {
                                float ff = gen.GetForestFactor(px, pz);
                                if (ff < cfg.ForestTresholdMin || ff > cfg.ForestTresholdMax) continue;
                            }
                            // terrain delta (consumes 10× insideUnitCircle)
                            float maxH = -999999f, minH = 999999f;
                            for (int k = 0; k < 10; k++)
                            {
                                var (ux, uy) = rng.InsideUnitCircle(strat);
                                float h = gen.GetHeight(px + ux * cfg.ExteriorRadius, pz + uy * cfg.ExteriorRadius);
                                if (h < minH) minH = h; if (h > maxH) maxH = h;
                            }
                            float delta = (float)((double)maxH - (double)minH);
                            if (delta > cfg.MaxTerrainDelta || delta < cfg.MinTerrainDelta) continue;

                            // accept
                            var zone = GetZone(px, pz);
                            if (!occupied.Contains(zone)) occupied.Add(zone);
                            placed.Add((px, pz));
                            if (shown < showN)
                            {
                                double best = double.MaxValue; (float x, float z) bestp = (0, 0);
                                foreach (var r in reals)
                                {
                                    double d = (px - r.x) * (px - r.x) + (pz - r.z) * (pz - r.z);
                                    if (d < best) { best = d; bestp = r; }
                                }
                                Console.WriteLine($"  #{placed.Count,3} attempt={i,5} j={j} computed=({px,9:F2},{pz,9:F2}) " +
                                                  $"nearestReal=({bestp.x,9:F2},{bestp.z,9:F2}) dist={Math.Sqrt(best),8:F2}");
                                shown++;
                            }
                            break;
                        }
                    }
                }
                i++;
            }
            Console.WriteLine($"placed {placed.Count}/{cfg.Quantity}");
            return 0;
        }

        sealed class OracleFile { public OracleLoc[]? locations { get; set; } }
        sealed class OracleLoc { public string? prefab { get; set; } public float x { get; set; } public float z { get; set; } public bool placed { get; set; } }

        static (int, int) GetZone(float wx, float wz)
            => ((int)Math.Floor((((double)wx + 32.0) / 64.0)), (int)Math.Floor((((double)wz + 32.0) / 64.0)));
        static float ZoneMag(int zx, int zy) => (float)Math.Sqrt((double)(zx * 64f) * (zx * 64f) + (double)(zy * 64f) * (zy * 64f));
        static (int, int) GetRandomZone(UnityRandom rng, float range)
        {
            int num = (int)range / 64; int zx, zy;
            do { zx = rng.Range(-num, num); zy = rng.Range(-num, num); } while (!(ZoneMag(zx, zy) < 10000f));
            return (zx, zy);
        }
        static (float, float) GetRandomPointInZone(UnityRandom rng, int zx, int zy, float r)
        {
            float cx = zx * 64f, cz = zy * 64f;
            float x = rng.Range(-32f + r, 32f - r); float z = rng.Range(-32f + r, 32f - r);
            return (cx + x, cz + z);
        }
    }
}
