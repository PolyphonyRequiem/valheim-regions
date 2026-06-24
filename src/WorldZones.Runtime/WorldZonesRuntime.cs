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

            // 1. Classify the world into a depth grid via the sampler's height field.
            var grid = new ZoneGrid(options.WorldRadiusMeters);
            ZoneClassifier.Classify(grid, new SamplerWorldDataProvider(sampler));

            // 2. Label connected land components.
            List<LandComponent> landComponents = ComponentLabeler.LabelLand(grid, out _);

            // 3. Generate proto-regions (the topology layer; still biome-blind by design).
            ProtoRegionResult protoResult = ProtoRegionGenerator.GenerateLand(
                grid,
                landComponents,
                options.TargetZonesPerRegion,
                seedRng,
                out int[,] regionIdGrid,
                out _,
                inlandWaterOptions: options.IncludeInlandWater
                    ? new InlandWaterAttributionOptions { Enabled = true }
                    : null);

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

            // 6. The point-query service (existing contract).
            var lookup = new RegionLookupService(grid, regionIdGrid, worldId, knownIds, identityById);

            // 7. The rich, aggregated region model + naming — skipped for point-query-only consumers
            //    (ComputeRegionInfo=false), which keeps that path free of the biome sampler + namer.
            List<RegionInfo> regions;
            if (options.ComputeRegionInfo)
            {
                regions = GazetteerBuilder.Build(sampler, grid, protoResult, regionIdGrid);
                IRegionNamer namer = options.Namer ?? new MultiSchemaRegionNamer();
                namer.NameAll(worldId, regions);
            }
            else
            {
                regions = new List<RegionInfo>();
            }

            return new RegionWorld(worldId, regions, lookup, grid, regionIdGrid, protoResult);
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
