using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// 🟢 STATUS: source=REAL-COMPUTED — a faithful, headless, offline port of Valheim's
    /// <c>ZoneSystem.GenerateLocations</c> (assembly_valheim decomp ~97319/97404). Unlike the
    /// vegetation model (which hits genuine headless walls — mesh/physics filters), the location
    /// placement loop is ALMOST ENTIRELY portable math the verified port already provides
    /// (<c>GetBiome</c>/<c>GetBiomeArea</c>/<c>GetHeight</c>/<c>GetForestFactor</c>/<c>GetTerrainDelta</c>),
    /// gated by a bit-exact <c>UnityRandom</c>. So locations can be computed FROM SEED with no client,
    /// no Steam, no walk — the same superpower the terrain port has.
    ///
    /// <para>WHAT IS FAITHFUL (transcribed 1:1 from the decomp):</para>
    /// <list type="bullet">
    ///   <item>Type processing ORDER: <c>OrderByDescending(prioritized)</c>, then iterated forward.</item>
    ///   <item>Per-type reseed: <c>InitState(worldSeed + prefabName.GetStableHashCode())</c>.</item>
    ///   <item>Global zone occupancy (<c>m_locationInstances</c>): ONE location per 64m zone, shared
    ///         across ALL types — an earlier type claiming a zone blocks later types.</item>
    ///   <item>Per-candidate RNG draw order: <c>GetRandomZone</c> (2 int draws + reject) →
    ///         <c>GetRandomPointInZone</c> (2 float draws) → filter chain, with <c>GetTerrainDelta</c>'s
    ///         10× <c>insideUnitCircle</c> consumed in-order so the stream stays aligned.</item>
    ///   <item><c>centerFirst</c> spiral (maxRange grows +1 per outer attempt), <c>unique</c> short-circuit,
    ///         <c>HaveLocationInRange</c> min/max-similar proximity gates.</item>
    /// </list>
    ///
    /// <para>WHAT IS DROPPED (provably irrelevant to placement RESULT):</para>
    /// <list type="bullet">
    ///   <item>Coroutine/time-slicing, progress estimation, ZLog — control flow only, no RNG effect.</item>
    ///   <item>The vegetation-mask filters (<c>m_minimumVegetation</c>/<c>m_maximumVegetation</c>/
    ///         <c>m_surroundCheckVegetation</c>) draw NOTHING and are unused by every enabled base-game
    ///         location (verified against the extracted catalogue: 0/86). Kept as gated no-ops so a
    ///         future catalogue that uses them stays correct, but they never fire today.</item>
    /// </list>
    ///
    /// <para>THE ONE EMPIRICAL UNKNOWN: <c>insideUnitCircle</c> is native (not in Unity's public C#),
    /// so its exact draw pattern is resolved by the validation harness sweeping
    /// <see cref="InsideUnitCircleStrategy"/> against a real <c>.db</c>. See the harness in
    /// <c>WorldZones.Cli</c> (<c>locations --validate</c>).</para>
    /// </summary>
    public static class LocationModel
    {
        const float SeaLevel = 30f;
        const float ZoneSize = 64f;

        /// <summary>One placement rule — the headless-relevant subset of Valheim's ZoneLocation.
        /// Populate from the extracted catalogue (tools/locations/parse_locations.py); do not hand-author.</summary>
        public sealed class LocationConfig
        {
            public string PrefabName = "";
            public bool Enable = true;
            public int Quantity;
            public bool Prioritized;
            public bool CenterFirst;
            public bool Unique;
            public string Group = "";
            public string GroupMax = "";
            public BiomeType Biome;                 // m_biome bitmask
            public int BiomeArea = 7;               // m_biomeArea bitmask (Edge=1|Median=2 => Everything=3; some assets use 7)
            public float MinDistanceFromSimilar;
            public float MaxDistanceFromSimilar;
            public float ExteriorRadius;
            public float InteriorRadius;
            public float MinTerrainDelta;
            public float MaxTerrainDelta = 10f;
            public float MinAltitude = -1000f;
            public float MaxAltitude = 1000f;
            public bool InForest;
            public float ForestTresholdMin;
            public float ForestTresholdMax;
            public float MinDistanceFromCenter;
            public float MaxDistanceFromCenter;
            public float MinDistance;
            public float MaxDistance;
            // vegetation-mask filters — unused by base-game enabled locations (kept for completeness)
            public float MinimumVegetation;
            public float MaximumVegetation = 1f;
            public bool SurroundCheckVegetation;
            public float SurroundCheckDistance;
            public int SurroundCheckLayers;
            public float SurroundBetterThanAverage;
        }

        /// <summary>A computed location instance: prefab + world position. source = real-computed.</summary>
        public readonly struct PlacedLocation
        {
            public readonly string PrefabName;
            public readonly float X;
            public readonly float Z;
            public PlacedLocation(string prefab, float x, float z)
            {
                this.PrefabName = prefab; this.X = x; this.Z = z;
            }
        }

        // ---- pure helpers transcribed from the decomp ----

        /// <summary>ZoneSystem.GetZone (decomp 98346): point → zone id, 64m grid, +32 offset, floor.</summary>
        static (int x, int y) GetZone(float wx, float wz)
            => (FloorToInt((float)(((double)wx + 32.0) / 64.0)),
                FloorToInt((float)(((double)wz + 32.0) / 64.0)));

        /// <summary>ZoneSystem.GetZonePos (decomp 98353): zone id → world centre.</summary>
        static (float x, float z) GetZonePos(int zx, int zy)
            => ((float)((double)zx * 64.0), (float)((double)zy * 64.0));

        static int FloorToInt(float f) => (int)Math.Floor(f);

        static float ZonePosMagnitude(int zx, int zy)
        {
            var (x, z) = GetZonePos(zx, zy);
            return (float)Math.Sqrt((double)x * x + (double)z * z);
        }

        /// <summary>ZoneSystem.GetRandomZone (decomp 97648): reject until zone centre within 10km.</summary>
        static (int x, int y) GetRandomZone(UnityRandom rng, float range)
        {
            int num = (int)range / 64;
            int zx, zy;
            do
            {
                zx = rng.Range(-num, num);
                zy = rng.Range(-num, num);
            } while (!(ZonePosMagnitude(zx, zy) < 10000f));
            return (zx, zy);
        }

        /// <summary>ZoneSystem.GetRandomPointInZone (decomp 97660): zone centre + uniform offset.</summary>
        static (float x, float z) GetRandomPointInZone(UnityRandom rng, int zx, int zy, float locationRadius)
        {
            var (cx, cz) = GetZonePos(zx, zy);
            float x = rng.Range(-32f + locationRadius, 32f - locationRadius);
            float z = rng.Range(-32f + locationRadius, 32f - locationRadius);
            return (cx + x, cz + z);
        }

        /// <summary>WorldGenerator.GetTerrainDelta (decomp 130753): 10 insideUnitCircle samples,
        /// delta = maxHeight - minHeight. Consumes RNG; the slopeDirection out is unused by the loop.</summary>
        static float GetTerrainDelta(UnityRandom rng, WorldGenerator gen, float cx, float cz,
                                     float radius, InsideUnitCircleStrategy strat)
        {
            float maxH = -999999f, minH = 999999f;
            for (int i = 0; i < 10; i++)
            {
                var (ux, uy) = rng.InsideUnitCircle(strat);
                float px = cx + ux * radius;
                float pz = cz + uy * radius;
                float h = gen.GetHeight(px, pz);
                if (h < minH) minH = h;
                if (h > maxH) maxH = h;
            }
            return (float)((double)maxH - (double)minH);
        }

        // ---- the port ----

        /// <summary>
        /// Compute all location placements for a world, faithfully reproducing
        /// <c>ZoneSystem.GenerateLocations</c>. Deterministic given (seed, catalogue, strategy).
        /// </summary>
        /// <param name="worldSeed">World seed hash = <c>seedName.GetStableHashCode()</c> (== World.m_seed).</param>
        /// <param name="gen">The verified port (provides GetBiome/GetBiomeArea/GetHeight/GetForestFactor).</param>
        /// <param name="catalogue">Extracted ZoneLocation configs.</param>
        /// <param name="strategy">insideUnitCircle draw pattern (resolved by the validation harness).</param>
        public static List<PlacedLocation> Generate(
            int worldSeed,
            WorldGenerator gen,
            IReadOnlyList<LocationConfig> catalogue,
            InsideUnitCircleStrategy strategy = InsideUnitCircleStrategy.PolarRadiusFirst)
        {
            // Global zone occupancy — ONE location per zone, shared across all types (decomp m_locationInstances).
            var locationInstances = new Dictionary<(int, int), (LocationConfig cfg, float x, float z)>();

            // Type order: prioritized first (decomp 97340 OrderByDescending, then forward iterate).
            var ordered = catalogue
                .Where(c => c.Enable && c.Quantity != 0)
                .OrderByDescending(c => c.Prioritized)
                .ToList();

            foreach (var location in ordered)
            {
                int seed = worldSeed + location.PrefabName.GetStableHashCode();
                var rng = new UnityRandom(seed);

                float maxRadius = Math.Max(location.ExteriorRadius, location.InteriorRadius);
                int attempts = location.Prioritized ? 200000 : 100000;
                int placed = CountPlaced(locationInstances, location.PrefabName);
                float maxRange = location.CenterFirst ? location.MinDistance : 10000f;

                if (location.Unique && placed > 0) continue;

                int i = 0;
                while (i < attempts && placed < location.Quantity)
                {
                    var (zx, zy) = GetRandomZone(rng, maxRange);
                    if (location.CenterFirst) maxRange += 1f;

                    if (!locationInstances.ContainsKey((zx, zy)))
                    {
                        var (zcx, zcz) = GetZonePos(zx, zy);
                        var biomeArea = gen.GetBiomeArea(zcx, zcz);
                        if (((int)location.BiomeArea & (int)biomeArea) != 0)
                        {
                            for (int j = 0; j < 20; j++)
                            {
                                var (px, pz) = GetRandomPointInZone(rng, zx, zy, maxRadius);
                                float magnitude = (float)Math.Sqrt((double)px * px + (double)pz * pz);
                                if (location.MinDistance != 0f && magnitude < location.MinDistance) continue;
                                if (location.MaxDistance != 0f && magnitude > location.MaxDistance) continue;

                                var biome = gen.GetBiome(px, pz);
                                if (((int)location.Biome & (int)biome) == 0) continue;

                                float groundY = gen.GetHeight(px, pz);
                                float alt = (float)((double)groundY - 30.0);
                                if (alt < location.MinAltitude || alt > location.MaxAltitude) continue;

                                if (location.InForest)
                                {
                                    float ff = gen.GetForestFactor(px, pz);
                                    if (ff < location.ForestTresholdMin || ff > location.ForestTresholdMax) continue;
                                }

                                if (location.MinDistanceFromCenter > 0f || location.MaxDistanceFromCenter > 0f)
                                {
                                    float dc = (float)Math.Sqrt((double)px * px + (double)pz * pz);
                                    if ((location.MinDistanceFromCenter > 0f && dc < location.MinDistanceFromCenter) ||
                                        (location.MaxDistanceFromCenter > 0f && dc > location.MaxDistanceFromCenter))
                                        continue;
                                }

                                float delta = GetTerrainDelta(rng, gen, px, pz, location.ExteriorRadius, strategy);
                                if (delta > location.MaxTerrainDelta || delta < location.MinTerrainDelta) continue;

                                if (location.MinDistanceFromSimilar > 0f &&
                                    HaveLocationInRange(locationInstances, location.PrefabName, location.Group,
                                                        px, pz, location.MinDistanceFromSimilar, maxGroup: false))
                                    continue;
                                if (location.MaxDistanceFromSimilar > 0f &&
                                    !HaveLocationInRange(locationInstances, location.PrefabName, location.GroupMax,
                                                         px, pz, location.MaxDistanceFromSimilar, maxGroup: true))
                                    continue;

                                // vegetation-mask filters omitted (draw nothing; unused by enabled base locations).

                                RegisterLocation(locationInstances, location, px, pz);
                                placed++;
                                break;
                            }
                        }
                    }
                    i++;
                }
            }

            // Emit in deterministic order (zone key) for stable output / diffing.
            return locationInstances
                .OrderBy(kv => kv.Key.Item1).ThenBy(kv => kv.Key.Item2)
                .Select(kv => new PlacedLocation(kv.Value.cfg.PrefabName, kv.Value.x, kv.Value.z))
                .ToList();
        }

        static int CountPlaced(Dictionary<(int, int), (LocationConfig cfg, float x, float z)> inst, string prefab)
        {
            int n = 0;
            foreach (var v in inst.Values) if (v.cfg.PrefabName == prefab) n++;
            return n;
        }

        /// <summary>ZoneSystem.RegisterLocation (decomp 98008): claim the candidate's zone (first wins).</summary>
        static void RegisterLocation(Dictionary<(int, int), (LocationConfig, float, float)> inst,
                                     LocationConfig cfg, float x, float z)
        {
            var zone = GetZone(x, z);
            if (!inst.ContainsKey(zone)) inst.Add(zone, (cfg, x, z));
        }

        /// <summary>ZoneSystem.HaveLocationInRange (decomp 98026): proximity gate by prefab/group.</summary>
        static bool HaveLocationInRange(Dictionary<(int, int), (LocationConfig cfg, float x, float z)> inst,
                                        string prefabName, string group, float px, float pz, float radius, bool maxGroup)
        {
            foreach (var v in inst.Values)
            {
                bool nameOrGroup =
                    v.cfg.PrefabName == prefabName
                    || (!maxGroup && group.Length > 0 && group == v.cfg.Group)
                    || (maxGroup && group.Length > 0 && group == v.cfg.GroupMax);
                if (nameOrGroup)
                {
                    float dx = v.x - px, dz = v.z - pz;
                    if ((float)Math.Sqrt((double)dx * dx + (double)dz * dz) < radius) return true;
                }
            }
            return false;
        }
    }
}
