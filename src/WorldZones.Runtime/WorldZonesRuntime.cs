using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.WorldGen;
using Vector2i = WorldZones.Regions.Vector2i;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The one entry point a consumer calls to get regions for a world. This is the substrate's
    /// front door.
    ///
    /// <para>
    /// Before this existed, every consumer (the overlay plugin, the CLI regions export, the CLI
    /// gazetteer) hand-rolled the same ~70-line bootstrap: classify a grid → label land → generate
    /// proto-regions → build the identity map → scan the grid into a known-id set → construct the
    /// lookup service. Three copies had already drifted. <see cref="Build"/> is that bootstrap,
    /// extracted once, so a new consumer mod writes:
    /// </para>
    /// <code>
    /// var world = WorldZonesRuntime.Build(PortWorldSampler.FromSeed("ForTheWort"));
    /// foreach (var r in world.Regions) Log($"{r.Name}  [{r.DominantBiome}]  {r.AreaKm2:F1} km²");
    /// var here = world.RegionAt(player.x, player.z);
    /// </code>
    /// </summary>
    public static class WorldZonesRuntime
    {
        /// <summary>
        /// Builds the full region world from a sampler. The sampler is the only thing a consumer must
        /// provide — <see cref="PortWorldSampler"/> for headless/offline, or a Valheim-backed sampler
        /// (in the mod project) for the live game.
        /// </summary>
        /// <param name="sampler">World data source (WorldId + height + biome).</param>
        /// <param name="options">Tunables; <see cref="RegionBuildOptions.Default"/> when null.</param>
        public static RegionWorld Build(IWorldSampler sampler, RegionBuildOptions options = null)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            options ??= RegionBuildOptions.Default;

            string worldId = sampler.WorldId;
            if (string.IsNullOrWhiteSpace(worldId))
                throw new InvalidOperationException("IWorldSampler.WorldId must not be null or empty.");

            // SeedRng: explicit override, else derive from WorldId via the Valheim stable hash —
            // identical to the CLI gazetteer's seed.GetStableHashCode(), so geometry matches.
            int seedRng = options.SeedRng ?? worldId.GetStableHashCode();

            // 1. Classify the world into a depth grid via the sampler's height field. When a swamp
            //    land-floor is set (default), use the biome-aware classify so swamp zones that dip below
            //    the waterline are still Land (rescuing them into regions); gated to Swamp, it changes no
            //    other biome. A null floor falls back to the depth-only classify (legacy geometry).
            var grid = new ZoneGrid(options.WorldRadiusMeters);
            if (options.SwampLandFloorMeters.HasValue)
            {
                ZoneClassifier.ClassifyWithSwampFloor(
                    grid,
                    (wx, wz) => sampler.GetHeight(wx, wz),
                    (wx, wz) => sampler.GetBiome(wx, wz) == global::WorldZones.WorldGen.BiomeType.Swamp,
                    options.SwampLandFloorMeters);
            }
            else
            {
                ZoneClassifier.Classify(grid, new SamplerWorldDataProvider(sampler));
            }

            // 2. Label connected land components.
            List<LandComponent> landComponents = ComponentLabeler.LabelLand(grid, out _);

            // 2b. Optional v3 cost field — biome-aware, so it lives here (Runtime), not in the
            //     biome-blind topology lib. When enabled, region growth becomes weighted Dijkstra
            //     (watershed): borders fall on biome edges / shores instead of geometric midlines.
            RegionCostField costField = null;
            if (options.UseFeatureAwareBorders)
                costField = RegionCostFieldBuilder.Build(sampler, grid, options.CostFieldOptions);

            // 2c. Optional biome-aware SEEDING field — the orthogonal lever that moves COMPOSITION
            //     (not routing). Also biome-reading, so it lives here (Runtime). When enabled, diverse
            //     land components get more seeds → split into smaller, more-mono-biome regions. A null
            //     field leaves the legacy area-only seed budget untouched (byte-identical fallback).
            SeedingField seedingField = null;
            if (options.UseBiomeAwareSeeding)
                seedingField = RegionSeedingFieldBuilder.Build(sampler, grid, options.SeedingFieldOptions);

            // 3. Generate proto-regions (the topology layer; cost + seeding fields are opaque to it).
            ProtoRegionResult protoResult = ProtoRegionGenerator.GenerateLand(
                grid,
                landComponents,
                options.TargetZonesPerRegion,
                seedRng,
                out int[,] regionIdGrid,
                out _,
                minRegionZones: options.MinRegionZones,
                minComponentZonesForProto: options.MinComponentZonesForProto,
                inlandWaterOptions: options.IncludeInlandWater
                    ? new InlandWaterAttributionOptions { Enabled = true }
                    : null,
                costField: costField,
                seedingField: seedingField);

            // 4. Build the transient-id → durable-identity-coordinate map (for the lookup service).
            var identityById = new Dictionary<int, Vector2i>(protoResult.Regions.Count);
            foreach (var region in protoResult.Regions)
                identityById[region.Id] = region.IdentityCoord;

            // 5. Collect the set of assigned region ids (the "known" set the lookup gates on).
            var knownIds = new HashSet<int>();
            int rows = regionIdGrid.GetLength(0), cols = regionIdGrid.GetLength(1);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int id = regionIdGrid[y, x];
                    if (id >= 0) knownIds.Add(id);
                }

            // 6. The rich, aggregated region model + naming — built BEFORE the lookup service so its
            //    output (the multi-schema names) can be threaded into the lookup. Skipped for
            //    point-query-only consumers (ComputeRegionInfo=false), which keeps that path free of
            //    the biome sampler + namer.
            List<RegionInfo> regions;
            Dictionary<string, string> namesByKey = null;
            if (options.ComputeRegionInfo)
            {
                regions = GazetteerBuilder.Build(sampler, grid, protoResult, regionIdGrid);
                IRegionNamer namer = options.Namer ?? new MultiSchemaRegionNamer();
                namer.NameAll(worldId, regions);

                // Thread the rich names into the lookup: RegionKey → Name. ResolveCurrent derives the
                // IDENTICAL RegionKey (RegionKey.From(identityCoord)) it resolves a point to, so this
                // map lines up 1:1 and the live minimap labels (which read lookupResult.RegionName)
                // render the multi-schema names instead of the legacy flat catalogue. Stays null when
                // naming is skipped → the lookup falls back to the legacy deterministic hash unchanged.
                namesByKey = new Dictionary<string, string>(regions.Count, StringComparer.Ordinal);
                foreach (var region in regions)
                    if (!string.IsNullOrEmpty(region.RegionKey) && !string.IsNullOrWhiteSpace(region.Name))
                        namesByKey[region.RegionKey] = region.Name;
            }
            else
            {
                regions = new List<RegionInfo>();
            }

            // 7. The point-query service (existing contract), now carrying the rich names when present
            //    (a null map makes ResolveCurrent use the legacy deterministic hash, byte-identical to before).
            var lookup = new RegionLookupService(grid, regionIdGrid, worldId, knownIds, identityById, namesByKey);

            // 8. Optional location join — bind POIs/dungeons/bosses/traders to their containing region,
            //    group unique-location candidates. Only when a source is supplied AND the rich model
            //    exists (the per-region attach needs RegionInfo; the join needs the lookup service).
            IReadOnlyList<GazetteerLocation> allLocations = System.Array.Empty<GazetteerLocation>();
            IReadOnlyList<CandidateGroup> candidateGroups = System.Array.Empty<CandidateGroup>();
            if (options.LocationSource != null && options.ComputeRegionInfo)
            {
                (allLocations, candidateGroups) = JoinLocations(options.LocationSource, lookup, regions);
            }

            return new RegionWorld(worldId, regions, lookup, grid, regionIdGrid, protoResult,
                allLocations, candidateGroups);
        }

        /// <summary>
        /// Bind each source location to its containing region, resolve <see cref="PlacementStatus"/>,
        /// build candidate groups for uniques, and attach per-region location slices. Pure aggregation —
        /// the source already computed positions; this only joins them to the region topology.
        /// </summary>
        private static (IReadOnlyList<GazetteerLocation>, IReadOnlyList<CandidateGroup>) JoinLocations(
            ILocationSource source, IRegionLookupService lookup, List<RegionInfo> regions)
        {
            var all = new List<GazetteerLocation>();
            // group sites per unique prefab as we go
            var groupSites = new Dictionary<string, List<GazetteerLocation>>(StringComparer.Ordinal);

            foreach (LocationRecord rec in source.EnumerateLocations())
            {
                string regionKey = null;
                RegionLookupResult res = lookup.ResolveCurrent(rec.X, rec.Z);
                if (res != null && res.HasRegion && !string.IsNullOrEmpty(res.RegionKey))
                    regionKey = res.RegionKey;

                // Status: a realized site (live source) -> Realized; a unique candidate -> Candidate;
                // a normal planned location -> Registered.
                PlacementStatus status =
                    rec.IsRealized ? PlacementStatus.Realized
                    : rec.IsUnique ? PlacementStatus.Candidate
                    : PlacementStatus.Registered;

                var loc = new GazetteerLocation
                {
                    PrefabName = rec.PrefabName,
                    X = rec.X,
                    Z = rec.Z,
                    RegionKey = regionKey,
                    Status = status,
                    CandidateGroupKey = rec.IsUnique ? rec.PrefabName : null,
                };
                all.Add(loc);

                if (rec.IsUnique)
                {
                    if (!groupSites.TryGetValue(rec.PrefabName, out var list))
                        groupSites[rec.PrefabName] = list = new List<GazetteerLocation>();
                    list.Add(loc);
                }
            }

            // Stable order: prefab, then position.
            all.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.PrefabName, b.PrefabName);
                if (c != 0) return c;
                c = a.X.CompareTo(b.X);
                return c != 0 ? c : a.Z.CompareTo(b.Z);
            });

            // Candidate groups (sorted by prefab for determinism).
            var groups = new List<CandidateGroup>(groupSites.Count);
            foreach (var kv in groupSites)
                groups.Add(new CandidateGroup(kv.Key, kv.Value));
            groups.Sort((a, b) => string.CompareOrdinal(a.PrefabName, b.PrefabName));

            // Per-region attach.
            var byRegion = new Dictionary<string, List<GazetteerLocation>>(StringComparer.Ordinal);
            foreach (var loc in all)
            {
                if (loc.RegionKey == null) continue;
                if (!byRegion.TryGetValue(loc.RegionKey, out var list))
                    byRegion[loc.RegionKey] = list = new List<GazetteerLocation>();
                list.Add(loc);
            }
            foreach (var region in regions)
                region.Locations = byRegion.TryGetValue(region.RegionKey, out var list)
                    ? list
                    : (IReadOnlyList<GazetteerLocation>)System.Array.Empty<GazetteerLocation>();

            return (all, groups);
        }

        /// <summary>
        /// Adapts an <see cref="IWorldSampler"/> to the topology layer's <see cref="IWorldDataProvider"/>.
        /// The topology layer only needs height (it is biome-blind); biome is consumed later by the
        /// aggregation. Kept private so the consumer-facing seam stays the single <see cref="IWorldSampler"/>.
        /// </summary>
        private sealed class SamplerWorldDataProvider : IWorldDataProvider
        {
            private readonly IWorldSampler sampler;
            public SamplerWorldDataProvider(IWorldSampler sampler) { this.sampler = sampler; }

            public string WorldId => this.sampler.WorldId;
            public float WaterLevel => ZoneClassifier.DefaultWaterLevel;
            public float GetTerrainHeight(float worldX, float worldZ) => this.sampler.GetHeight(worldX, worldZ);
        }
    }
}
