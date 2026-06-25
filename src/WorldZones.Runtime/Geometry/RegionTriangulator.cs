using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// Triangulated fill for one region: a flat vertex list (world metres) + triangle indices (CCW),
    /// ready for a Unity <c>Mesh</c> or a UI fill. Produced by <see cref="RegionTriangulator"/>.
    /// </summary>
    public sealed class RegionMesh
    {
        public string RegionKey { get; }
        public IReadOnlyList<WzVec2> Vertices { get; }
        /// <summary>Triangle index triples (length is a multiple of 3), CCW winding.</summary>
        public IReadOnlyList<int> Triangles { get; }

        public RegionMesh(string regionKey, IReadOnlyList<WzVec2> vertices, IReadOnlyList<int> triangles)
        {
            this.RegionKey = regionKey;
            this.Vertices = vertices;
            this.Triangles = triangles;
        }
    }

    /// <summary>
    /// Ear-clipping triangulation of a region's outer ring with its hole rings bridged in. Pure
    /// (Tier-1, headless-tested). Robust enough for the rectilinear, axis-aligned loops the zone-edge
    /// lattice produces (no self-intersections; all interior angles are multiples of 90°). A consumer
    /// that wants a different fill strategy (e.g. a GPU stencil) can ignore this and use the rings
    /// directly. See docs/design/region-render-seam.md.
    /// </summary>
    public static class RegionTriangulator
    {
        /// <summary>Triangulate every region's fill from the boundary graph.</summary>
        public static IReadOnlyList<RegionMesh> Triangulate(RegionBoundaryGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            var meshes = new List<RegionMesh>();
            foreach (var key in graph.Rings.Select(r => r.RegionKey).Distinct(StringComparer.Ordinal))
            {
                var mesh = TriangulateRegion(graph, key);
                if (mesh != null) meshes.Add(mesh);
            }
            return meshes;
        }

        /// <summary>Triangulate a single region (outer ring + holes), or null if it has no outer ring.</summary>
        public static RegionMesh TriangulateRegion(RegionBoundaryGraph graph, string regionKey)
        {
            RegionRing outer = graph.OuterRing(regionKey);
            if (outer == null || outer.Vertices.Count < 3) return null;

            var holes = graph.RingsFor(regionKey).Where(r => r.IsHole).ToList();

            // Build a single simple polygon: outer CCW, with each hole (CW) bridged in.
            List<WzVec2> poly = outer.Vertices.ToList();
            EnsureWinding(poly, ccw: true);

            foreach (var hole in holes.OrderByDescending(h => Math.Abs(h.SignedArea)))
            {
                var holeVerts = hole.Vertices.ToList();
                EnsureWinding(holeVerts, ccw: false); // hole CW inside CCW outer
                poly = BridgeHole(poly, holeVerts);
            }

            var tris = EarClip(poly);
            return new RegionMesh(regionKey, poly, tris);
        }

        private static void EnsureWinding(List<WzVec2> v, bool ccw)
        {
            double area = Shoelace(v);
            bool isCcw = area > 0;
            if (isCcw != ccw) v.Reverse();
        }

        private static double Shoelace(IReadOnlyList<WzVec2> v)
        {
            double s = 0; int n = v.Count;
            for (int i = 0; i < n; i++) { var a = v[i]; var b = v[(i + 1) % n]; s += a.X * b.Z - b.X * a.Z; }
            return s / 2.0;
        }

        // Bridge a hole into the outer polygon by connecting the hole's rightmost vertex to a
        // mutually-visible outer vertex, splicing the hole loop (+ the two bridge vertices) inline.
        // Standard "two-way bridge" cut; adequate for non-overlapping rectilinear rings.
        private static List<WzVec2> BridgeHole(List<WzVec2> outer, List<WzVec2> hole)
        {
            int hIdx = 0;
            for (int i = 1; i < hole.Count; i++)
                if (hole[i].X > hole[hIdx].X) hIdx = i;
            WzVec2 hp = hole[hIdx];

            // Pick the outer vertex closest to the hole's bridge point (cheap, robust for these shapes).
            int oIdx = 0; double best = double.MaxValue;
            for (int i = 0; i < outer.Count; i++)
            {
                double dx = outer[i].X - hp.X, dz = outer[i].Z - hp.Z;
                double d = dx * dx + dz * dz;
                if (d < best) { best = d; oIdx = i; }
            }

            var result = new List<WzVec2>(outer.Count + hole.Count + 2);
            for (int i = 0; i <= oIdx; i++) result.Add(outer[i]);
            for (int i = 0; i < hole.Count; i++) result.Add(hole[(hIdx + i) % hole.Count]);
            result.Add(hole[hIdx]);          // close the hole loop
            result.Add(outer[oIdx]);         // bridge back to the outer
            for (int i = oIdx + 1; i < outer.Count; i++) result.Add(outer[i]);
            return result;
        }

        private static List<int> EarClip(List<WzVec2> poly)
        {
            var tris = new List<int>();
            int n = poly.Count;
            if (n < 3) return tris;

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            int guard = 0, maxGuard = n * n + 16;
            while (idx.Count > 3 && guard++ < maxGuard)
            {
                bool clipped = false;
                int m = idx.Count;
                for (int i = 0; i < m; i++)
                {
                    int i0 = idx[(i - 1 + m) % m], i1 = idx[i], i2 = idx[(i + 1) % m];
                    WzVec2 a = poly[i0], b = poly[i1], c = poly[i2];

                    if (Cross(a, b, c) <= 0) continue; // reflex or collinear under CCW

                    bool anyInside = false;
                    for (int j = 0; j < m; j++)
                    {
                        int vj = idx[j];
                        if (vj == i0 || vj == i1 || vj == i2) continue;
                        if (PointInTri(poly[vj], a, b, c)) { anyInside = true; break; }
                    }
                    if (anyInside) continue;

                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // no ear found — bail rather than spin (degenerate input)
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        private static double Cross(WzVec2 a, WzVec2 b, WzVec2 c)
            => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        private static bool PointInTri(WzVec2 p, WzVec2 a, WzVec2 b, WzVec2 c)
        {
            double d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos); // inside (or on edge) when all same sign
        }
    }
}
