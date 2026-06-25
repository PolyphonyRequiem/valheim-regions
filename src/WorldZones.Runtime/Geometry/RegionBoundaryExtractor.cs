using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// Extracts renderable boundary geometry from a region-id grid: the deduplicated seam set (the
    /// stroke-once <see cref="BorderSegment"/> primitive) and the closed fill loops
    /// (<see cref="RegionRing"/>). Pure — no Unity, no live game read — so it runs under the headless
    /// net8 test net. See docs/design/region-render-seam.md.
    ///
    /// <para><b>Lattice.</b> Zone <c>(zx,zy)</c> spans world X∈[zx·64−32, zx·64+32], Z likewise — so
    /// a zone CORNER sits at world <c>((c)·64 − 32)</c> for integer corner-index <c>c</c>. Region
    /// assignment changes across a shared zone EDGE; that edge IS the border, and it lands on the
    /// <c>64·n+32</c> lattice. We never invent geometry between zone centres — the seam is exactly the
    /// cell edge, matching how the engine classifies at 64 m zone resolution.</para>
    ///
    /// <para><b>Winding.</b> Boundary edges are walked DIRECTED with the region's interior on the
    /// LEFT. Under a standard +X-right / +Z-up axis that yields counter-clockwise OUTER rings
    /// (positive signed area) and clockwise HOLE rings (negative) for free — the polygon-with-holes
    /// convention a triangulator/UI-fill expects, no post-hoc hole detection needed.</para>
    /// </summary>
    public static class RegionBoundaryExtractor
    {
        private const double Half = ZoneGrid.ZoneSize / 2.0; // 32 m — corner offset off the zone centre

        /// <summary>
        /// Build the boundary graph from a classified region-id grid.
        /// </summary>
        /// <param name="regionIdGrid">Per-zone region int id, indexed <c>[gy, gx]</c> (grid-local;
        ///   <c>gx = zx − minIndex</c>). <c>&lt; 0</c> = unassigned (ocean / unassigned land).</param>
        /// <param name="minIndex">The grid's minimum zone coordinate on each axis (<c>ZoneGrid.MinIndex</c>).</param>
        /// <param name="idToKey">Region int id → durable <see cref="RegionKey"/> string. Ids absent from
        ///   this map are treated as unassigned (defensive: a stray label with no region record).</param>
        public static RegionBoundaryGraph Extract(int[,] regionIdGrid, int minIndex, IReadOnlyDictionary<int, string> idToKey)
        {
            if (regionIdGrid == null) throw new ArgumentNullException(nameof(regionIdGrid));
            if (idToKey == null) throw new ArgumentNullException(nameof(idToKey));

            int height = regionIdGrid.GetLength(0); // gy extent
            int width = regionIdGrid.GetLength(1);  // gx extent

            string KeyAt(int gx, int gy)
            {
                if (gx < 0 || gx >= width || gy < 0 || gy >= height) return null;
                int id = regionIdGrid[gy, gx];
                if (id < 0) return null;
                return idToKey.TryGetValue(id, out var k) ? k : null;
            }

            // World coordinate of the lattice CORNER at corner-index (cx, cy). Corner cx sits between
            // zone column (cx-1) and zone column (cx): world = (cx + minIndex) * 64 - 32.
            WzVec2 Corner(int cx, int cy) => new WzVec2(
                (cx + minIndex) * (double)ZoneGrid.ZoneSize - Half,
                (cy + minIndex) * (double)ZoneGrid.ZoneSize - Half);

            var segments = ExtractSegments(width, height, KeyAt, Corner);
            var rings = ExtractRings(width, height, KeyAt, Corner);
            return new RegionBoundaryGraph(segments, rings);
        }

        /// <summary>
        /// Pass 1 — the deduplicated seam set. Each interior vertical edge (between zone gx-1 and gx)
        /// and horizontal edge (between zone gy-1 and gy) where the two sides differ becomes ONE
        /// segment carrying both keys, canonicalised (KeyA ≤ KeyB ordinally; a region-vs-void seam
        /// puts the region in KeyA, null in KeyB). Emitted once — never per-region.
        /// </summary>
        private static List<BorderSegment> ExtractSegments(
            int width, int height, Func<int, int, string> keyAt, Func<int, int, WzVec2> corner)
        {
            var segs = new List<BorderSegment>();

            // Vertical lattice edges: between zone column (gx-1) [left] and zone column gx [right],
            // spanning corner row gy..gy+1. Iterate gx in [0..width] so the world's outer rim is included.
            for (int gx = 0; gx <= width; gx++)
            for (int gy = 0; gy < height; gy++)
            {
                string left = keyAt(gx - 1, gy);
                string right = keyAt(gx, gy);
                if (string.Equals(left, right, StringComparison.Ordinal)) continue;

                WzVec2 a = corner(gx, gy);
                WzVec2 b = corner(gx, gy + 1);
                segs.Add(Canonical(a, b, left, right));
            }

            // Horizontal lattice edges: between zone row (gy-1) [below] and zone row gy [above],
            // spanning corner column gx..gx+1.
            for (int gy = 0; gy <= height; gy++)
            for (int gx = 0; gx < width; gx++)
            {
                string below = keyAt(gx, gy - 1);
                string above = keyAt(gx, gy);
                if (string.Equals(below, above, StringComparison.Ordinal)) continue;

                WzVec2 a = corner(gx, gy);
                WzVec2 b = corner(gx + 1, gy);
                segs.Add(Canonical(a, b, below, above));
            }

            return segs;
        }

        private static BorderSegment Canonical(WzVec2 a, WzVec2 b, string s1, string s2)
        {
            // Exactly one side may be null (we never emit a null/null edge — equal sides are skipped).
            if (s1 == null) return new BorderSegment(a, b, s2, null);
            if (s2 == null) return new BorderSegment(a, b, s1, null);
            // Both real: order by key so the pair is canonical and dedup-friendly.
            return string.CompareOrdinal(s1, s2) <= 0
                ? new BorderSegment(a, b, s1, s2)
                : new BorderSegment(a, b, s2, s1);
        }

        // ── Pass 2 — closed rings per region, via directed-edge boundary tracing ────────────────────

        // A directed boundary edge between two lattice corners. Interior of `key` is on the LEFT.
        private readonly struct DEdge
        {
            public readonly int FromX, FromY, ToX, ToY;
            public DEdge(int fx, int fy, int tx, int ty) { FromX = fx; FromY = fy; ToX = tx; ToY = ty; }
        }

        private static List<RegionRing> ExtractRings(
            int width, int height, Func<int, int, string> keyAt, Func<int, int, WzVec2> corner)
        {
            // Collect, per region key, its directed boundary edges (interior-on-left), then stitch
            // them into closed loops. For a cell of region K, each of its 4 sides that faces a
            // DIFFERENT region contributes one directed edge wound CCW around the cell:
            //   right side  → corner(gx+1,gy)   → corner(gx+1,gy+1)
            //   top side    → corner(gx+1,gy+1) → corner(gx,  gy+1)
            //   left side   → corner(gx,  gy+1) → corner(gx,  gy)
            //   bottom side → corner(gx,  gy)   → corner(gx+1,gy)
            // Shared interior edges between two cells of the SAME region cancel (never emitted);
            // edges on a true region boundary survive, each appearing once for the region on its left.
            var edgesByKey = new Dictionary<string, List<DEdge>>(StringComparer.Ordinal);

            void Add(string key, int fx, int fy, int tx, int ty)
            {
                if (key == null) return;
                if (!edgesByKey.TryGetValue(key, out var list))
                {
                    list = new List<DEdge>();
                    edgesByKey[key] = list;
                }
                list.Add(new DEdge(fx, fy, tx, ty));
            }

            for (int gy = 0; gy < height; gy++)
            for (int gx = 0; gx < width; gx++)
            {
                string k = keyAt(gx, gy);
                if (k == null) continue;

                if (!string.Equals(keyAt(gx + 1, gy), k, StringComparison.Ordinal)) Add(k, gx + 1, gy, gx + 1, gy + 1); // right
                if (!string.Equals(keyAt(gx, gy + 1), k, StringComparison.Ordinal)) Add(k, gx + 1, gy + 1, gx, gy + 1); // top
                if (!string.Equals(keyAt(gx - 1, gy), k, StringComparison.Ordinal)) Add(k, gx, gy + 1, gx, gy);         // left
                if (!string.Equals(keyAt(gx, gy - 1), k, StringComparison.Ordinal)) Add(k, gx, gy, gx + 1, gy);         // bottom
            }

            var rings = new List<RegionRing>();
            foreach (var kv in edgesByKey)
            {
                foreach (var loop in StitchLoops(kv.Value))
                {
                    var verts = loop.Select(c => corner(c.x, c.y)).ToList();
                    var simplified = CollapseCollinear(verts);
                    if (simplified.Count < 3) continue; // degenerate
                    double area = SignedArea(simplified);
                    rings.Add(new RegionRing(kv.Key, simplified, area));
                }
            }
            return rings;
        }

        // Stitch a bag of directed edges into closed loops by chaining ToCorner → FromCorner.
        // Each loop is returned as an ordered list of corner indices (closed implicitly).
        private static IEnumerable<List<(int x, int y)>> StitchLoops(List<DEdge> edges)
        {
            // Index edges by their start corner. A region boundary is a set of simple closed loops;
            // a corner can be a junction (two loops meeting at a point) so we key a LIST per start.
            var byStart = new Dictionary<(int, int), LinkedList<DEdge>>();
            foreach (var e in edges)
            {
                var key = (e.FromX, e.FromY);
                if (!byStart.TryGetValue(key, out var list))
                {
                    list = new LinkedList<DEdge>();
                    byStart[key] = list;
                }
                list.AddLast(e);
            }

            foreach (var e in edges)
            {
                var startKey = (e.FromX, e.FromY);
                if (!byStart.TryGetValue(startKey, out var bucket) || bucket.Count == 0) continue;

                // Begin a loop from this still-unconsumed edge.
                var first = bucket.First.Value;
                bucket.RemoveFirst();

                var loop = new List<(int x, int y)> { (first.FromX, first.FromY) };
                int curX = first.ToX, curY = first.ToY;
                int guard = edges.Count + 4;

                while (guard-- > 0)
                {
                    loop.Add((curX, curY));
                    if (curX == first.FromX && curY == first.FromY) break; // closed

                    if (!byStart.TryGetValue((curX, curY), out var nextBucket) || nextBucket.Count == 0)
                        break; // open chain (shouldn't happen on a well-formed grid) — drop tail

                    var next = nextBucket.First.Value;
                    nextBucket.RemoveFirst();
                    curX = next.ToX; curY = next.ToY;
                }

                // loop's last element duplicates the first corner (the closing vertex) — drop it.
                if (loop.Count >= 2 && loop[loop.Count - 1] == loop[0]) loop.RemoveAt(loop.Count - 1);
                if (loop.Count >= 3) yield return loop;
            }
        }

        // Remove redundant collinear vertices (long straight runs along a zone edge → two endpoints).
        private static List<WzVec2> CollapseCollinear(List<WzVec2> verts)
        {
            int n = verts.Count;
            if (n < 3) return verts;
            var outv = new List<WzVec2>(n);
            for (int i = 0; i < n; i++)
            {
                WzVec2 prev = verts[(i - 1 + n) % n];
                WzVec2 cur = verts[i];
                WzVec2 next = verts[(i + 1) % n];
                double cross = (cur.X - prev.X) * (next.Z - prev.Z) - (cur.Z - prev.Z) * (next.X - prev.X);
                if (Math.Abs(cross) > 1e-6) outv.Add(cur); // keep only true corners
            }
            return outv.Count >= 3 ? outv : verts;
        }

        // Shoelace signed area (+ = CCW under +X/+Z axes).
        private static double SignedArea(List<WzVec2> v)
        {
            double sum = 0.0;
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 a = v[i], b = v[(i + 1) % n];
                sum += a.X * b.Z - b.X * a.Z;
            }
            return sum / 2.0;
        }
    }
}
