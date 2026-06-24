using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.WorldGen;
using Vector2i = WorldZones.Regions.Vector2i;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The rich, consumer-facing description of one region — everything a downstream mod needs to
    /// reason about "what place is this" without re-deriving it from the grid.
    ///
    /// <para>
    /// This is the in-process equivalent of one entry in the gazetteer JSON. It used to exist only
    /// as a throwaway <c>Agg</c> struct private to the CLI exporter; it is promoted here to a real
    /// type so the overlay plugin, the CLI, and external consumers all share ONE region model.
    /// </para>
    ///
    /// <para>
    /// Identity is <see cref="RegionKey"/> (durable, coordinate-derived) — persist and key off THAT,
    /// never <see cref="TransientId"/>, which is just the seed's list index and renumbers whenever
    /// seeding changes (border rewrites, authored seeds, Valheim 1.0). See docs/design/region-identity.md.
    /// </para>
    /// </summary>
    public sealed class RegionInfo
    {
        /// <summary>Durable, coordinate-derived identity (e.g. "r.-3.7"). Persist + join on this.</summary>
        public string RegionKey { get; set; }

        /// <summary>Deterministic display name produced by the active <see cref="IRegionNamer"/>.</summary>
        public string Name { get; set; }

        /// <summary>Transient BFS/seed-list index. Internal scratch — do NOT persist or name off it.</summary>
        public int TransientId { get; set; }

        /// <summary>The region's durable identity coordinate (the MIN absorbed seed zone coord).</summary>
        public Vector2i IdentityCoord { get; set; }

        /// <summary>The zone coordinate this region was originally seeded at.</summary>
        public Vector2i SeedZone { get; set; }

        /// <summary>Area-weighted centroid in world metres. Double precision: this is an average
        /// over many zones, and consumers (and the gazetteer serializer) round at the edge.</summary>
        public double CentroidX { get; set; }

        /// <summary>Area-weighted centroid in world metres.</summary>
        public double CentroidZ { get; set; }

        /// <summary>Inclusive zone-coordinate bounding box.</summary>
        public int MinZoneX { get; set; }

        /// <summary>Inclusive zone-coordinate bounding box.</summary>
        public int MinZoneZ { get; set; }

        /// <summary>Inclusive zone-coordinate bounding box.</summary>
        public int MaxZoneX { get; set; }

        /// <summary>Inclusive zone-coordinate bounding box.</summary>
        public int MaxZoneZ { get; set; }

        /// <summary>Total territory in zones (land + attributed inland water).</summary>
        public int AreaZones { get; set; }

        /// <summary>Land-only zone count (the proto-region's official land area).</summary>
        public int LandZones { get; set; }

        /// <summary>
        /// Count of land zones the aggregation sampler actually visited for this region — the
        /// denominator behind <see cref="BiomeComposition"/> and the centroid/mean averages. Usually
        /// equals <see cref="LandZones"/>, but can differ slightly (the proto-region's official land
        /// area vs. the zones walked during aggregation), so biome fractions must divide by THIS, not
        /// by <see cref="LandZones"/>, to stay exact.
        /// </summary>
        public int SampledLandZones { get; set; }

        /// <summary>Attributed inland-water zone count.</summary>
        public int InlandWaterZones { get; set; }

        /// <summary>Territory area in square kilometres (land + inland water).</summary>
        public double AreaKm2 { get; set; }

        /// <summary>True if any zone borders a non-land (shallow/deep/ocean) zone.</summary>
        public bool IsCoastal { get; set; }

        /// <summary>The most common non-ocean biome by zone count.</summary>
        public BiomeType DominantBiome { get; set; }

        /// <summary>
        /// Biome → fraction of land zones (descending). Sums to ~1.0 over land. Ocean excluded.
        /// Convenience over <see cref="BiomeZoneCounts"/>; for exact reproduction prefer the counts.
        /// </summary>
        public IReadOnlyDictionary<BiomeType, float> BiomeComposition { get; set; }

        /// <summary>
        /// Biome → raw land-zone count (descending), ocean excluded. Lossless: fractions are derived
        /// as count / <see cref="LandZones"/>. Carried so a serializer can reproduce a fraction at any
        /// precision without float-rounding drift.
        /// </summary>
        public IReadOnlyDictionary<BiomeType, int> BiomeZoneCounts { get; set; }

        /// <summary>Minimum terrain height (world metres) over the region's land zones.</summary>
        public float MinElevation { get; set; }

        /// <summary>Mean terrain height (world metres) over the region's land zones. Double: averaged.</summary>
        public double MeanElevation { get; set; }

        /// <summary>Maximum terrain height (world metres) over the region's land zones.</summary>
        public float MaxElevation { get; set; }

        /// <summary>Vertical relief = <see cref="MaxElevation"/> − <see cref="MinElevation"/>.</summary>
        public float Relief => this.MaxElevation - this.MinElevation;

        /// <summary>World-metre X of the highest sampled point in the region.</summary>
        public float HighestPeakX { get; set; }

        /// <summary>World-metre Z of the highest sampled point in the region.</summary>
        public float HighestPeakZ { get; set; }

        /// <summary>Durable keys of the regions that share a border with this one (sorted, ordinal).</summary>
        public IReadOnlyList<string> NeighborKeys { get; set; } = new List<string>();
    }
}
