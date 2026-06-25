using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The fully-built region world for one Valheim world — the object a consumer holds onto.
    ///
    /// <para>It serves both consumer shapes:</para>
    /// <list type="bullet">
    ///   <item><b>Browse / enumerate</b>: <see cref="Regions"/> — the rich, named region set
    ///   (the in-process gazetteer).</item>
    ///   <item><b>Point query</b>: <see cref="Lookup"/> — "what region is at (x,z)?" via the existing
    ///   <see cref="IRegionLookupService"/> contract, plus <see cref="RegionAt"/> for the rich record.</item>
    /// </list>
    /// </summary>
    public sealed class RegionWorld
    {
        private readonly Dictionary<string, RegionInfo> byKey;

        internal RegionWorld(
            string worldId,
            IReadOnlyList<RegionInfo> regions,
            IRegionLookupService lookup,
            ZoneGrid grid,
            int[,] regionIdGrid,
            ProtoRegionResult protoResult,
            IReadOnlyList<GazetteerLocation> allLocations = null,
            IReadOnlyList<CandidateGroup> candidateGroups = null)
        {
            this.WorldId = worldId;
            this.Regions = regions;
            this.Lookup = lookup;
            this.Grid = grid;
            this.RegionIdGrid = regionIdGrid;
            this.ProtoResult = protoResult;
            this.AllLocations = allLocations ?? System.Array.Empty<GazetteerLocation>();
            this.CandidateGroups = candidateGroups ?? System.Array.Empty<CandidateGroup>();

            this.byKey = new Dictionary<string, RegionInfo>(regions.Count, StringComparer.Ordinal);
            foreach (var r in regions) this.byKey[r.RegionKey] = r;
        }

        /// <summary>Stable world identity (the sampler's WorldId) names + persistence keyed off.</summary>
        public string WorldId { get; }

        /// <summary>All regions, richly described and named, ordered by durable RegionKey.</summary>
        public IReadOnlyList<RegionInfo> Regions { get; }

        /// <summary>Point-query service implementing the existing <see cref="IRegionLookupService"/>.</summary>
        public IRegionLookupService Lookup { get; }

        /// <summary>The classified zone grid (advanced consumers / renderers).</summary>
        public ZoneGrid Grid { get; }

        /// <summary>Per-zone region-id assignment grid (advanced consumers / ESP rendering).</summary>
        public int[,] RegionIdGrid { get; }

        /// <summary>Raw proto-region statistics from generation.</summary>
        public ProtoRegionResult ProtoResult { get; }

        /// <summary>
        /// Every gazetteer location in the world (the flat set), each joined to its region and carrying
        /// its <see cref="PlacementStatus"/>. Empty unless a <see cref="RegionBuildOptions.LocationSource"/>
        /// was supplied. Per-region slices are on <see cref="RegionInfo.Locations"/>; this is the
        /// world-wide view (includes locations outside any region — ocean/islet — with null RegionKey).
        /// </summary>
        public IReadOnlyList<GazetteerLocation> AllLocations { get; }

        /// <summary>
        /// Candidate groups for UNIQUE locations (Haldor, PlaceOfMystery, Hildir) — each a world-scoped
        /// set of N sites that resolve to exactly one. Empty unless a location source was supplied and
        /// the world has uniques. Offline these are unresolved (the seed doesn't pick the winner); a live
        /// source resolves them as the world is explored. See <see cref="CandidateGroup"/>.
        /// </summary>
        public IReadOnlyList<CandidateGroup> CandidateGroups { get; }

        /// <summary>Look up a rich region by its durable key, or null if unknown.</summary>
        public RegionInfo GetByKey(string regionKey)
        {
            if (regionKey == null) return null;
            return this.byKey.TryGetValue(regionKey, out var r) ? r : null;
        }

        /// <summary>
        /// The rich <see cref="RegionInfo"/> at a world coordinate, or null if the point is
        /// unassigned / out of bounds. Convenience over <see cref="Lookup"/> + <see cref="GetByKey"/>.
        /// </summary>
        public RegionInfo RegionAt(float worldX, float worldZ)
        {
            RegionLookupResult res = this.Lookup.ResolveCurrent(worldX, worldZ);
            if (res == null || !res.HasRegion || string.IsNullOrEmpty(res.RegionKey)) return null;
            return GetByKey(res.RegionKey);
        }

        /// <summary>
        /// Double-coordinate convenience overload. Region centroids (<see cref="RegionInfo.CentroidX"/>
        /// etc.) are doubles, so a consumer querying at a region's own centroid lands here without an
        /// explicit cast. The lookup itself is float-precision (zone-resolution), so the doubles are
        /// narrowed — harmless at 64 m zone granularity.
        /// </summary>
        public RegionInfo RegionAt(double worldX, double worldZ) => RegionAt((float)worldX, (float)worldZ);

        /// <summary>
        /// Build the renderable boundary geometry (deduplicated seams + closed fill rings) for this
        /// world — the Tier-1 export a render consumer (the standalone overlay, a Trailborne adapter)
        /// projects with <see cref="Geometry.MapProjector"/>. Computed on demand from
        /// <see cref="RegionIdGrid"/>; cache the result if you draw every frame. Keyed by durable
        /// <see cref="RegionInfo.RegionKey"/>. See docs/design/region-render-seam.md.
        /// </summary>
        public RegionBoundaryGraph BuildBoundaryGraph()
        {
            var idToKey = new Dictionary<int, string>();
            foreach (var r in this.Regions)
            {
                // RegionInfo.TransientId is the grid's int label; RegionKey is the durable identity.
                if (!idToKey.ContainsKey(r.TransientId)) idToKey[r.TransientId] = r.RegionKey;
            }
            return RegionBoundaryExtractor.Extract(this.RegionIdGrid, this.Grid.MinIndex, idToKey);
        }
    }
}
