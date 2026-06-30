using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Rasterizes the AUTHORITATIVE refined region rings (<see cref="RefinedRegionBoundary"/>) into a fine
    /// region-id raster — the VECTOR fill path (DECISION 2026-06-29). A texel takes a region's label iff it
    /// is inside that region's refined OUTER ring and not inside any of its hole rings (point-in-polygon,
    /// holes subtracted). This replaces the 64 m zone-membership edge of <see cref="RegionFillMaskBaker"/>
    /// (whose edge is a coarse zone staircase — Daniel's "blocky swamp fill" was 72% zone-limited) with the
    /// smooth, contour-hugging ring boundary, so the fill edge follows the real coastline.
    ///
    /// <para>Output is the SAME <c>int[,]</c> shape + origin contract as <see cref="RegionFillMaskBaker.Bake"/>
    /// (a <c>[gh·sub, gw·sub]</c> raster on the <c>minIndex·64 − 32</c> lattice at <c>64/sub</c> m texels),
    /// so it is a drop-in replacement for the consumer (<c>RegionTextureBaker.BakeFine</c>) — the controller,
    /// fog gate, and the fill↔fade partition all keep working unchanged. Pure Runtime (no Unity); the rings
    /// already carry the height-clipped coast by construction (the coast edges were refined to the 30 m
    /// waterline iso), so no per-texel height test is needed here.</para>
    /// </summary>
    public sealed class RegionRingFillBaker
    {
        private readonly RefinedRegionBoundary boundary;
        private readonly IReadOnlyDictionary<string, int> keyToLabel;

        /// <param name="boundary">The refined ring boundary for the world (built at world-load).</param>
        /// <param name="keyToLabel">RegionKey → grid label (TransientId / ProtoRegion.Id — same space).</param>
        public RegionRingFillBaker(RefinedRegionBoundary boundary, IReadOnlyDictionary<string, int> keyToLabel)
        {
            this.boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
            this.keyToLabel = keyToLabel ?? throw new ArgumentNullException(nameof(keyToLabel));
        }

        /// <summary>
        /// Bake the fine ring-fill raster. <paramref name="minIndex"/> is the ZONE grid min index and
        /// <paramref name="subdivisions"/> the fine texels per zone edge (4 ⇒ 16 m), matching
        /// <see cref="RegionFillMaskBaker.Bake"/> exactly so the texture registers identically under the map.
        /// </summary>
        /// <returns>A <c>[gh·sub, gw·sub]</c> int raster of region labels (−1 = outside every ring).</returns>
        public int[,] Bake(int gridH, int gridW, int minIndex, int subdivisions = RegionFillMaskBaker.DefaultSubdivisions)
        {
            if (subdivisions < 1) throw new ArgumentOutOfRangeException(nameof(subdivisions));

            const double zone = ZoneGrid.ZoneSize;        // 64
            const double half = ZoneGrid.ZoneSize / 2.0;  // 32
            double texel = zone / subdivisions;
            double originX = minIndex * zone - half;
            double originZ = minIndex * zone - half;

            int fh = gridH * subdivisions, fw = gridW * subdivisions;
            var outRaster = new int[fh, fw];
            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                    outRaster[y, x] = -1;

            // Group rings by region so holes are subtracted only within their own region.
            var byKey = new Dictionary<string, (List<RefinedRing> outers, List<RefinedRing> holes)>(StringComparer.Ordinal);
            foreach (RefinedRing rr in this.boundary.Rings)
            {
                if (!byKey.TryGetValue(rr.RegionKey, out var lists))
                {
                    lists = (new List<RefinedRing>(), new List<RefinedRing>());
                    byKey[rr.RegionKey] = lists;
                }
                if (rr.IsHole) lists.holes.Add(rr); else lists.outers.Add(rr);
            }

            foreach (var kv in byKey)
            {
                if (!this.keyToLabel.TryGetValue(kv.Key, out int label) || label < 0) continue;
                foreach (RefinedRing outer in kv.Value.outers)
                {
                    // bbox of this outer ring in texel coords (clamped), so we only scan its footprint.
                    double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
                    var ov = outer.Vertices;
                    for (int i = 0; i < ov.Count; i++)
                    {
                        WzVec2 p = ov[i];
                        if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
                        if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
                    }
                    int fx0 = Math.Max(0, (int)((minX - originX) / texel) - 1);
                    int fx1 = Math.Min(fw - 1, (int)((maxX - originX) / texel) + 1);
                    int fy0 = Math.Max(0, (int)((minZ - originZ) / texel) - 1);
                    int fy1 = Math.Min(fh - 1, (int)((maxZ - originZ) / texel) + 1);

                    for (int fy = fy0; fy <= fy1; fy++)
                    {
                        double wz = originZ + (fy + 0.5) * texel;
                        for (int fx = fx0; fx <= fx1; fx++)
                        {
                            if (outRaster[fy, fx] >= 0) continue;   // already claimed (first ring wins; regions don't overlap)
                            double wx = originX + (fx + 0.5) * texel;
                            if (!PointInRing(ov, wx, wz)) continue;
                            bool inHole = false;
                            foreach (RefinedRing hole in kv.Value.holes)
                                if (PointInRing(hole.Vertices, wx, wz)) { inHole = true; break; }
                            if (!inHole) outRaster[fy, fx] = label;
                        }
                    }
                }
            }
            return outRaster;
        }

        // Standard even-odd ray-cast point-in-polygon (world X/Z).
        private static bool PointInRing(IReadOnlyList<WzVec2> v, double px, double pz)
        {
            bool inside = false;
            int n = v.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double xi = v[i].X, zi = v[i].Z, xj = v[j].X, zj = v[j].Z;
                if (((zi > pz) != (zj > pz)) &&
                    (px < (xj - xi) * (pz - zi) / (zj - zi) + xi))
                    inside = !inside;
            }
            return inside;
        }
    }
}
