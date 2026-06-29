using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// The authoritative refined boundary for a whole region world: every <see cref="RegionRing"/> of
    /// <see cref="RegionBoundaryGraph.Rings"/> promoted to a watertight <see cref="RefinedRing"/> via
    /// <see cref="RegionRingRefiner"/>. This is the vector source of truth (DECISION 2026-06-29): region
    /// membership (point-in-polygon) and the gazetteer key off these rings; the raster fill mask is a
    /// 2D-map render consumer of them. Built once at world creation and persisted (see the world-build
    /// path), not recomputed per session. See docs/design/region-render-seam.md.
    /// </summary>
    public sealed class RefinedRegionBoundary
    {
        private readonly Dictionary<string, List<RefinedRing>> byRegion;

        /// <summary>Every refined ring (outer + holes) across all regions.</summary>
        public IReadOnlyList<RefinedRing> Rings { get; }

        /// <summary>Count rolled back to refined-only by the self-intersection guard (audit).</summary>
        public int RolledBackCount { get; }

        /// <summary>Count rolled all the way back to the raw source ring (both smoothed+refined crossed) (audit).</summary>
        public int RolledBackToRawCount { get; }

        /// <summary>Count whose smoothing was skipped by the size guard (tiny specks) (audit).</summary>
        public int SkippedSmallCount { get; }

        public RefinedRegionBoundary(IReadOnlyList<RefinedRing> rings)
        {
            this.Rings = rings;
            this.byRegion = new Dictionary<string, List<RefinedRing>>(System.StringComparer.Ordinal);
            int rolled = 0, rolledRaw = 0, skipped = 0;
            foreach (var r in rings)
            {
                if (!this.byRegion.TryGetValue(r.RegionKey, out var list))
                { list = new List<RefinedRing>(); this.byRegion[r.RegionKey] = list; }
                list.Add(r);
                if (r.Outcome == RingRefineOutcome.RolledBackSelfIntersect) rolled++;
                else if (r.Outcome == RingRefineOutcome.RolledBackToRaw) rolledRaw++;
                else if (r.Outcome == RingRefineOutcome.SkippedSmoothTooSmall) skipped++;
            }
            this.RolledBackCount = rolled;
            this.RolledBackToRawCount = rolledRaw;
            this.SkippedSmallCount = skipped;
        }

        /// <summary>All refined rings (outer + holes) for one region, or empty.</summary>
        public IReadOnlyList<RefinedRing> RingsFor(string regionKey)
        {
            if (regionKey != null && this.byRegion.TryGetValue(regionKey, out var list)) return list;
            return System.Array.Empty<RefinedRing>();
        }

        /// <summary>The largest outer (CCW) refined ring for a region — the membership/fill loop. Null if none.</summary>
        public RefinedRing OuterRing(string regionKey)
        {
            RefinedRing best = null;
            foreach (var r in this.RingsFor(regionKey))
            {
                if (r.IsHole) continue;
                if (best == null || r.SignedArea > best.SignedArea) best = r;
            }
            return best;
        }

        /// <summary>
        /// Refine every ring in <paramref name="graph"/> into the authoritative boundary. Region labels
        /// for edge classification come from <paramref name="idToLabel"/> (RegionKey → TransientId).
        /// </summary>
        public static RefinedRegionBoundary Build(RegionBoundaryGraph graph,
            IReadOnlyDictionary<string, int> idToLabel, RegionRingRefiner.RegionIdAt regionIdAt,
            IScalarField coastField, ICategoryField seamField, RingRefineOptions options = null)
        {
            var rings = new List<RefinedRing>(graph.Rings.Count);
            foreach (RegionRing ring in graph.Rings)
            {
                int label = idToLabel != null && idToLabel.TryGetValue(ring.RegionKey, out var l) ? l : -1;
                rings.Add(RegionRingRefiner.Refine(ring, label, regionIdAt, coastField, seamField, options));
            }
            return new RefinedRegionBoundary(rings);
        }
    }
}
