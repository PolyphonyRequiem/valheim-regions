using System;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Tunables for <see cref="WorldZonesRuntime.Build"/>. <see cref="Default"/> reproduces the
    /// settings the shipped overlay plugin and the CLI gazetteer already use, so routing an existing
    /// caller through the runtime does not move region geometry.
    /// </summary>
    public sealed class RegionBuildOptions
    {
        /// <summary>Desired average region size in zones. Per-component seed count =
        /// max(1, componentZones / this). Default 200 (the shipped value).</summary>
        public int TargetZonesPerRegion { get; set; } = 200;

        /// <summary>
        /// RNG seed for deterministic seed placement. When null, it is derived from the sampler's
        /// <c>WorldId</c> via the Valheim stable hash — the same derivation the CLI gazetteer uses
        /// (<c>seed.GetStableHashCode()</c>), so a null here reproduces gazetteer geometry exactly.
        /// Set explicitly only to force a specific placement RNG.
        /// </summary>
        public int? SeedRng { get; set; }

        /// <summary>World radius in metres for the zone grid. Default = Valheim's ±10,000m.</summary>
        public float WorldRadiusMeters { get; set; } = global::WorldZones.Regions.ZoneGrid.WorldRadius;

        /// <summary>Attribute enclosed inland water (lakes) to surrounding regions. Default off,
        /// matching the shipped plugin; the CLI enables it via <c>--inland-water</c>.</summary>
        public bool IncludeInlandWater { get; set; }

        /// <summary>
        /// Whether to compute the rich <see cref="RegionInfo"/> model (biome composition, terrain
        /// character, neighbour graph) and name every region. Default true.
        ///
        /// <para>
        /// Set false for a POINT-QUERY-ONLY consumer (e.g. the minimap name-label plugin): the build
        /// produces the topology + <see cref="RegionWorld.Lookup"/> service and nothing else —
        /// <see cref="RegionWorld.Regions"/> comes back empty, the namer never runs, and the sampler's
        /// <c>GetBiome</c> is never called. This is the exact work the shipped overlay plugin needs, so
        /// routing it through <see cref="WorldZonesRuntime.Build"/> stays behaviour-preserving and free
        /// of the biome bridge. Flip to true (and supply a real biome resolver) when a consumer wants
        /// the rich model / multi-schema names.
        /// </para>
        /// </summary>
        public bool ComputeRegionInfo { get; set; } = true;

        /// <summary>
        /// The namer that assigns region display names. Default = <see cref="MultiSchemaRegionNamer"/>
        /// (the rich faux-lore namer). Set to your own <see cref="IRegionNamer"/> to override, or to a
        /// namer constructed with a location sidecar to unlock boss-seat / trader / dungeon schemas.
        /// </summary>
        public IRegionNamer Namer { get; set; }

        /// <summary>
        /// Optional source of LOCATIONS (POIs, dungeons, bosses, traders) to join into the gazetteer in
        /// the same build pass. When set, every location is binned to its containing region
        /// (<see cref="RegionInfo.Locations"/>) and unique-location candidate sites are grouped into
        /// <see cref="RegionWorld.CandidateGroups"/>. When null (default), the build produces no location
        /// data and those collections come back empty — preserving the existing regions-only behaviour.
        ///
        /// <para>Use <see cref="PortLocationSource"/> for an offline/from-seed build (tagged computed),
        /// or the mod project's live source for runtime-exact data with realization. Requires
        /// <see cref="ComputeRegionInfo"/> = true (the join needs the rich region model + lookup).</para>
        /// </summary>
        public ILocationSource LocationSource { get; set; }

        /// <summary>
        /// Enable the v3 biome-edge cost field — weighted-Dijkstra (watershed) region growth instead of
        /// the legacy terrain-blind BFS, so borders fall on biome edges / shores rather than geometric
        /// midlines. Default <c>false</c> (preserves shipped geometry bit-for-bit). When true, the build
        /// computes the field via <see cref="RegionCostFieldBuilder"/> (needs the biome sampler, so it
        /// also forces the biome bridge on) and hands it to the generator. Tune the weights via
        /// <see cref="CostFieldOptions"/>. See docs/design/region-borders.md.
        /// </summary>
        public bool UseFeatureAwareBorders { get; set; }

        /// <summary>
        /// Per-feature weights for <see cref="UseFeatureAwareBorders"/>. Null = the measured v3 default
        /// (biome-edge 12 / shore 8 / interior 1). Ignored when feature-aware borders are off.
        /// </summary>
        public RegionCostFieldOptions CostFieldOptions { get; set; }

        /// <summary>A fresh options object with shipped defaults.</summary>
        public static RegionBuildOptions Default => new RegionBuildOptions();
    }
}
