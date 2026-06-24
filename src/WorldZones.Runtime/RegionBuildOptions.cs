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
        /// The namer that assigns region display names. Default = <see cref="MultiSchemaRegionNamer"/>
        /// (the rich faux-lore namer). Set to your own <see cref="IRegionNamer"/> to override, or to a
        /// namer constructed with a location sidecar to unlock boss-seat / trader / dungeon schemas.
        /// </summary>
        public IRegionNamer Namer { get; set; }

        /// <summary>A fresh options object with shipped defaults.</summary>
        public static RegionBuildOptions Default => new RegionBuildOptions();
    }
}
