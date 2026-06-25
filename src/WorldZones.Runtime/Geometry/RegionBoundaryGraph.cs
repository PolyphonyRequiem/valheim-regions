using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// The renderable boundary geometry for a whole region world: the deduplicated seam set
    /// (<see cref="Segments"/> — the stroke-once primitive) and the closed fill loops
    /// (<see cref="Rings"/>). Produced by <see cref="RegionBoundaryExtractor"/> (or
    /// <c>RegionWorld.BuildBoundaryGraph()</c>). Pure world-space metres; a consumer projects it with
    /// <see cref="MapProjector"/> + a <see cref="MapFrame"/>. See docs/design/region-render-seam.md.
    /// </summary>
    public sealed class RegionBoundaryGraph
    {
        private readonly Dictionary<string, List<RegionRing>> ringsByRegion;

        /// <summary>
        /// Every region-to-region (and region-to-void) seam, each emitted exactly ONCE with both
        /// bounding keys. Draw these for "borders only" styles — no double-stroke, no z-fight.
        /// </summary>
        public IReadOnlyList<BorderSegment> Segments { get; }

        /// <summary>
        /// Every closed boundary loop, keyed by region. One CCW outer ring + zero or more CW hole
        /// rings per region. Triangulate / fill these for "tint" and "parchment" styles.
        /// </summary>
        public IReadOnlyList<RegionRing> Rings { get; }

        public RegionBoundaryGraph(IReadOnlyList<BorderSegment> segments, IReadOnlyList<RegionRing> rings)
        {
            this.Segments = segments;
            this.Rings = rings;

            this.ringsByRegion = new Dictionary<string, List<RegionRing>>(System.StringComparer.Ordinal);
            foreach (var ring in rings)
            {
                if (!this.ringsByRegion.TryGetValue(ring.RegionKey, out var list))
                {
                    list = new List<RegionRing>();
                    this.ringsByRegion[ring.RegionKey] = list;
                }
                list.Add(ring);
            }
        }

        /// <summary>All rings (outer + holes) for one region, or an empty list if unknown.</summary>
        public IReadOnlyList<RegionRing> RingsFor(string regionKey)
        {
            if (regionKey != null && this.ringsByRegion.TryGetValue(regionKey, out var list)) return list;
            return System.Array.Empty<RegionRing>();
        }

        /// <summary>
        /// The largest outer (CCW) ring for a region — the one a single-label / single-fill consumer
        /// wants. Null if the region has no outer ring (only possible for degenerate input).
        /// </summary>
        public RegionRing OuterRing(string regionKey)
        {
            RegionRing best = null;
            foreach (var ring in this.RingsFor(regionKey))
            {
                if (ring.IsHole) continue;
                if (best == null || ring.SignedArea > best.SignedArea) best = ring;
            }
            return best;
        }
    }
}
