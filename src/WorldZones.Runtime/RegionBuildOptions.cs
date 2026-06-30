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

        /// <summary>
        /// Enable the biome-aware SEEDING lever — the only lever proven able to move region COMPOSITION
        /// (the multi-biome-blob oddity). When true, the build computes a per-zone biome-diversity field
        /// (<see cref="RegionSeedingFieldBuilder"/>, needs the biome sampler) and scales each land
        /// component's seed budget UP where it spans many biomes, so a diverse landmass splits into
        /// smaller, more-mono-biome regions. Default <c>false</c> (preserves shipped geometry bit-for-bit:
        /// a null field leaves the legacy area-only seed budget untouched). This is ORTHOGONAL to
        /// <see cref="UseFeatureAwareBorders"/> (routing): seeding sets composition, routing sets where
        /// the border falls between seeds. ⚠️ Changing seed count renumbers regions (RegionKey is
        /// seed-coordinate-derived) — fine pre-ship, but it shifts names + any persisted discovery state.
        /// See docs/design/region-borders.md ("the SEEDING lever").
        /// </summary>
        public bool UseBiomeAwareSeeding { get; set; }

        /// <summary>
        /// Tunables for <see cref="UseBiomeAwareSeeding"/>. Null = the default (aggressiveness 1.0,
        /// 5×5 neighbourhood, 4-biome normaliser). Ignored when biome-aware seeding is off. These are a
        /// starting dial, not a locked value — split aggressiveness is partly a walk judgment.
        /// </summary>
        public RegionSeedingFieldOptions SeedingFieldOptions { get; set; }

        /// <summary>A fresh options object with shipped defaults.</summary>
        public static RegionBuildOptions Default => new RegionBuildOptions();

        /// <summary>
        /// Minimum region size in 64 m zones. After growth, any region smaller than this is merged into
        /// its largest-shared-border neighbour (<c>MergeTinyRegions</c>), so the world has no runt regions
        /// that are too small to read as a real place on the map. Now PLUMBED to this option (was a
        /// hardcoded <c>6</c> in <c>ProtoRegionGenerator</c>) so callers can tune it. Default kept at
        /// <c>6</c> for now: raising it to 25 was investigated 2026-06-29 (Daniel) after a 17-zone runt
        /// rendered as a non-region, BUT the bump had ZERO effect because <c>MergeTinyRegions</c> only
        /// merges runts that have a land neighbour — 15 of 27 sub-25 regions are isolated islands
        /// (unmergeable) and a further 12 have neighbours yet survive anyway (a real merge bug). Until
        /// that merge bug is fixed, raising this default is cosmetic. See the handoff:
        /// docs/design/region-min-size-merge-handoff.md. Higher = fewer, larger regions; merge tie-break
        /// is lower region id.
        /// </summary>
        public int MinRegionZones { get; set; } = 6;

        /// <summary>
        /// Minimum LAND-COMPONENT size in 64 m zones for a component to earn a region seed. A connected
        /// land mass smaller than this never gets a proto-seed — it is recorded as a
        /// <see cref="ProtoRegionResult.MinorIslets">MinorIslet</see> and renders as UNINCORPORATED land
        /// (no region id, no name, no fill), exactly the "deep separated islands stay unincorporated"
        /// stance. Now PLUMBED to this option (was a hardcoded <c>12</c> via
        /// <c>ProtoRegionGenerator.DefaultMinComponentZonesForProto</c>) so callers can tune it.
        ///
        /// <para>
        /// This is the lever — NOT <see cref="MinRegionZones"/> — for the "runt region" problem. The merge
        /// floor only folds a runt that shares a LAND border with a bigger region; a tiny whole-island
        /// runt has no land neighbour (verified 2026-06-30 on Astley: all 27 sub-25-zone regions are their
        /// own entire land component, 0 land-adjacent → the merge provably can't touch them). Raising THIS
        /// floor is what demotes those islands. Daniel LOCKED this design (option A) 2026-06-30. The two
        /// floors are orthogonal and cover the two distinct runt cases: a sub-split of a big component that
        /// shares a land border → <see cref="MinRegionZones"/> merges it; a tiny standalone island →
        /// THIS floor demotes it to unincorporated.
        /// </para>
        ///
        /// <para>
        /// LOCKED at <c>25</c> (design A, Daniel, 2026-06-30): the only floor that takes runt regions to
        /// exactly 0 on Astley — every surviving region is ≥25 land-component zones. Measured cost
        /// (`compfloor` sweep, seed Astley): 27 tiny whole-island components demote to unincorporated,
        /// world-wide unincorporated land 4.52% → 6.24% (~+1.7 pts). The 12-24 zone band had no natural
        /// gap, so 25 is an intent line ("too small to be a place"), not a distribution cliff. Raising
        /// this guarantees no region's LAND COMPONENT is below the floor — it does NOT by itself guarantee
        /// no region is below the floor in zones (a large component can still birth a small sub-split region
        /// down to <see cref="MinRegionZones"/>); on Astley the two coincide (min region land == 25). For a
        /// full min-size guarantee in worlds where they diverge, move both floors together.
        /// </para>
        /// </summary>
        public int MinComponentZonesForProto { get; set; } = 25;

        /// <summary>
        /// Swamp land-rescue floor (world metres). Swamp terrain straddles the 30 m waterline
        /// (measured range ~24.8–33.8 m on real worlds), so the height-only land test
        /// (<c>height ≥ 30</c>) drops ~64% of swamp zones to Shallow/Deep — they then fall out of every
        /// region (no fill, no border), the "swamp not reliably included" bug. When set, a zone is also
        /// classified Land if its biome is Swamp AND its height ≥ this floor. Gated to the Swamp biome,
        /// so it provably changes NO other terrain (zero blast radius outside swamp — verified across the
        /// whole world). Default <c>27.5f</c>: ~2.5 m below the 30 m waterline. Measured 2026-06-29 (seed
        /// Astley): swamp terrain sits in a tight 24–33 m band (mean 29.6, peak 29), so a floor in this
        /// range rescues the near-surface walkable swamp while letting the deeper bog read as water.
        ///
        /// <para>
        /// FINALISED at <c>27.5</c> (Daniel, 2026-06-30) after the same-window 22-vs-floor A/B
        /// (<c>swampab</c>, seed Astley) on 4 swamp-heavy non-runt regions, with an HONEST coastal-vs-
        /// interior split (flood from open water through the shed band: a shed cell the flood reaches =
        /// shoreline retreat = coastal/good; a shed cell walled off by surviving land = interior hole/bad).
        /// At 28.5 the shed punched INTERIOR HOLES into swamp bodies (Kjellvik 89% interior, Kjellhavn 36%,
        /// Nordreach 29%, Galdhavn 11%) — water pockets inside the bog, not clean coast. Those holes were
        /// almost entirely terrain in the razor-thin <c>[27.5, 28.5)</c> band: dropping the floor 1 m to
        /// 27.5 rescues 94–100% of them, and every region then reads as CLEAN COASTAL TRIM (interior 0–5%).
        /// 27.5 is the line that trims wet coast without holing the body. (An earlier "100% coastal @28.5"
        /// claim came from a BROKEN proximity metric that counted bog-near-bog as coast — superseded by the
        /// flood-based split. Do not resurrect proximity-to-any-water as a coastal test.)
        /// </para>
        ///
        /// Set <c>null</c> to disable (legacy height-only behaviour). See
        /// docs/design/region-borders.md ("the swamp land-floor").
        /// </summary>
        public float? SwampLandFloorMeters { get; set; } = 27.5f;
    }
}
