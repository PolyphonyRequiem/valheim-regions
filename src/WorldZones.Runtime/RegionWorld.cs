using System;
using System.Collections.Generic;
using WorldZones.Regions;

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
            ProtoRegionResult protoResult)
        {
            this.WorldId = worldId;
            this.Regions = regions;
            this.Lookup = lookup;
            this.Grid = grid;
            this.RegionIdGrid = regionIdGrid;
            this.ProtoResult = protoResult;

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
    }
}
