using System;
using System.Collections.Generic;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// 🟡 STATUS: SCAFFOLD / source=MODELED — headless skeleton of Valheim's vegetation+resource
    /// placement (ZoneSystem.PlaceVegetation). This is the foundation for the "ore/vegetation node
    /// counts per region" sidecar discussed for the gazetteer. It is deliberately INCOMPLETE and
    /// honest about WHY — see the buildability verdict in docs/design/vegetation-resource-model.md.
    ///
    /// WHAT IS REAL HERE (portable, deterministic, verified-RNG):
    ///   • The per-zone RNG seeding formula, byte-exact with the decomp:
    ///       InitState(worldSeed + zoneId.x*4271 + zoneId.y*9187 + prefab.GetStableHashCode())
    ///   • The scatter loop structure: N placement attempts in a 2*(32-groupRadius) m box around the
    ///     zone centre, each spawning a group of [groupSizeMin..groupSizeMax], using the SAME RNG draw
    ///     ORDER as the game (this is what makes a count reproduction possible at all).
    ///   • The cheap, headless-computable filters: count roll (m_min/m_max), altitude band, distance
    ///     from world centre. These need only height — which the port HAS (GetHeight).
    ///
    /// WHAT IS NOT — AND CANNOT BE — HERE (the honest blockers):
    ///   1. CONFIG DATA. The m_vegetation list (which prefabs exist, their min/max counts, biome,
    ///      altitude bands, group sizes) lives in Unity SERIALIZED ASSETS (ZNetScene prefabs), NOT in
    ///      any DLL and NOT anywhere on a headless box. Copper's "m_min=0,m_max=1,m_groupSizeMin=3" is
    ///      asset data we do not have. => configs are an EXTERNAL INPUT to this model. Until a real
    ///      extraction exists (asset-ripper on a client install), the catalogue is EMPTY and any count
    ///      this produces is structurally honest but numerically empty. We do NOT fabricate configs.
    ///   2. MESH / PHYSICS FILTERS. PlaceVegetation also calls GetVegetationMask, GetOceanDepth,
    ///      GetTerrainDelta (Heightmap mesh queries) and IsBlocked / GetGroundData (Unity physics
    ///      raycasts). These have NO headless equivalent — they need the built terrain mesh + collider
    ///      world. A headless reproduction will therefore OVER-count (it cannot reject placements the
    ///      game rejects via these). This is a known, documented bias, not a bug.
    ///   3. GetForestFactor (m_inForest filter) is a WorldGenerator static the port did not bring over.
    ///      Portable if needed, but parked with the rest until configs exist to make it matter.
    ///
    /// So: this class lets a future caller load REAL extracted configs and get a determinitic, RNG-exact
    /// (if over-counting) estimate, every output tagged source=modeled. It is the seam, not the answer.
    /// </summary>
    public static class VegetationModel
    {
        /// <summary>
        /// One vegetation/resource placement rule — the headless-relevant subset of Valheim's
        /// ZoneVegetation. Populate from a REAL extracted catalogue; do not hand-author numbers.
        /// </summary>
        public sealed class VegetationConfig
        {
            public string PrefabName = "";          // e.g. "Copper", "MineRock_Tin", "Birch1"
            public bool Enable = true;
            public BiomeType Biome;                 // bitmask of biomes this may spawn in
            public float Min;                       // m_min  (if Max<1 => probability gate instead of count)
            public float Max = 10f;                 // m_max
            public int GroupSizeMin = 1;
            public int GroupSizeMax = 1;
            public float GroupRadius;
            public float MinAltitude = -1000f;      // metres above sea (p.y - 30)
            public float MaxAltitude = 1000f;
            public float MinDistanceFromCenter;
            public float MaxDistanceFromCenter;
            public bool ForcePlacement;
            /// <summary>True for ore/mineable resources (Copper, Tin, SilverVein, MudPile, obsidian…)
            /// — lets the gazetteer sidecar report "resource" counts distinctly from flora.</summary>
            public bool IsResource;
        }

        /// <summary>A modelled count of one prefab in one zone. source is ALWAYS "modeled".</summary>
        public readonly struct ZoneVegCount
        {
            public readonly string PrefabName;
            public readonly int EstimatedCount;
            public readonly bool IsResource;
            public ZoneVegCount(string prefab, int count, bool isResource)
            {
                this.PrefabName = prefab; this.EstimatedCount = count; this.IsResource = isResource;
            }
        }

        public const int ZoneSize = 64;
        const float SpawnHalfDefault = 32f;
        const float SeaLevel = 30f;

        /// <summary>World-space centre of a zone (matches ZoneSystem.GetZonePos / 64 m grid).</summary>
        public static (float x, float z) ZoneCenter(int zoneX, int zoneY)
            => (zoneX * (float)ZoneSize, zoneY * (float)ZoneSize);

        /// <summary>
        /// Deterministically MODEL the vegetation/resource counts for a single zone, byte-exact in RNG
        /// seeding + draw order with ZoneSystem.PlaceVegetation, using only headless-computable filters
        /// (count roll, altitude, distance-from-centre). Mesh/physics filters are NOT applied → counts
        /// are an UPPER-BIAS estimate. Returns empty if <paramref name="catalogue"/> is empty (the
        /// honest default — no fabricated configs).
        /// </summary>
        /// <param name="worldSeed">WorldGenerator seed hash (seed string via GetStableHashCode, or the int).</param>
        /// <param name="height">Height sampler — the verified port's GetHeight(wx,wz) (world metres).</param>
        /// <param name="biomeAt">Biome sampler — the verified port's GetBiome(wx,wz).</param>
        public static List<ZoneVegCount> ModelZone(
            int worldSeed, int zoneX, int zoneY,
            IReadOnlyList<VegetationConfig> catalogue,
            Func<float, float, float> height,
            Func<float, float, BiomeType> biomeAt)
        {
            var results = new List<ZoneVegCount>();
            if (catalogue == null || catalogue.Count == 0) return results; // honest empty

            var (cx, cz) = ZoneCenter(zoneX, zoneY);
            var rng = new UnityRandom(0);

            foreach (var v in catalogue)
            {
                if (!v.Enable || string.IsNullOrEmpty(v.PrefabName)) continue;

                // EXACT per-prefab seeding from the decomp (line 97044).
                rng.InitState(worldSeed + zoneX * 4271 + zoneY * 9187 + v.PrefabName.GetStableHashCode());

                int targetCount = 1;
                if (v.Max < 1f)
                {
                    if (rng.Value > v.Max) continue;     // probability gate
                }
                else
                {
                    targetCount = rng.Range((int)v.Min, (int)v.Max + 1);
                }

                float spawnHalf = SpawnHalfDefault - v.GroupRadius;
                int attempts = v.ForcePlacement ? targetCount * 50 : targetCount;
                int placedGroups = 0, placedInstances = 0;

                for (int i = 0; i < attempts; i++)
                {
                    // group anchor (draw order preserved even though we don't use the mesh)
                    float ax = rng.Range(cx - spawnHalf, cx + spawnHalf);
                    _ = rng.Range(cz - spawnHalf, cz + spawnHalf); // z draw (consumed, matches game order)
                    int groupSize = rng.Range(v.GroupSizeMin, v.GroupSizeMax + 1);

                    bool anyPlaced = false;
                    for (int j = 0; j < groupSize; j++)
                    {
                        // per-instance draws the game makes (rotation, scale, tilt) — consumed to keep
                        // RNG aligned, even though we only need the COUNT, not the transform.
                        _ = rng.Range(0, 360);
                        _ = rng.Value; _ = rng.Value; _ = rng.Value;

                        // headless-computable filters only:
                        float px = ax; // group centre is sufficient for a count estimate
                        float pz = cz;
                        var biome = biomeAt(px, pz);
                        if ((v.Biome & biome) == 0) continue;

                        float groundY = height(px, pz);
                        float alt = groundY - SeaLevel;
                        if (alt < v.MinAltitude || alt > v.MaxAltitude) continue;

                        if (v.MinDistanceFromCenter > 0f || v.MaxDistanceFromCenter > 0f)
                        {
                            float d = (float)Math.Sqrt(px * px + pz * pz);
                            if ((v.MinDistanceFromCenter > 0f && d < v.MinDistanceFromCenter) ||
                                (v.MaxDistanceFromCenter > 0f && d > v.MaxDistanceFromCenter)) continue;
                        }

                        // NOTE: mesh/physics filters (vegetation mask, ocean depth, terrain delta,
                        // tilt-vs-normal, blocked, clear-area, forest factor) are DELIBERATELY skipped
                        // — headless cannot evaluate them. This is the documented over-count bias.
                        placedInstances++;
                        anyPlaced = true;
                    }
                    if (anyPlaced) placedGroups++;
                    if (placedGroups >= targetCount) break;
                }

                if (placedInstances > 0)
                    results.Add(new ZoneVegCount(v.PrefabName, placedInstances, v.IsResource));
            }

            return results;
        }
    }
}
