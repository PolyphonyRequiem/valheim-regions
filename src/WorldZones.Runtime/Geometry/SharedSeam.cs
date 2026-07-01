using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// ONE shared border between two regions (or a region and the void), computed ONCE and consumed by
    /// BOTH bounding regions' fill rings AND the ink — so fill and ink agree <b>by construction</b> rather
    /// than as three independent refinements that weave (~16 m apart) as they do today. This is the
    /// "shared-seam primitive" (fork B, docs/design/spike-004-shared-seam-primitive.md), the interior-seam
    /// analogue of the coast render-domain-agreement fix.
    ///
    /// <para>A seam is a maximal run of the coarse <see cref="BorderSegment"/> lattice for ONE region-pair,
    /// broken at <b>junctions</b> — lattice nodes where 3+ regions meet or the region-pair changes. Its two
    /// endpoints are those junction nodes (or equal, for a seam that closes on itself with no junction).
    /// The junction endpoints are the pin points: every seam incident to a junction shares that exact node,
    /// so refined seams meet with zero gap (proven: spike-004 SPIKE 2, gap = 0.000000 m over 145+ junctions).</para>
    /// </summary>
    public sealed class SharedSeam
    {
        /// <summary>Durable key of one bounding region (ordinally-lesser). Never null.</summary>
        public string KeyA { get; }

        /// <summary>Durable key of the other bounding region, or null when the far side is void (coast).</summary>
        public string KeyB { get; }

        /// <summary>Packed lattice-node id of one endpoint (a junction, unless the seam is a bare closed loop).</summary>
        public long Node0 { get; }

        /// <summary>Packed lattice-node id of the other endpoint. Equals <see cref="Node0"/> for a closed-loop seam.</summary>
        public long Node1 { get; }

        /// <summary>The coarse 64 m polyline, ordered Node0 → Node1 (world metres, on the 64·n+32 lattice).</summary>
        public IReadOnlyList<WzVec2> Coarse { get; }

        /// <summary>
        /// The refined polyline, ordered Node0 → Node1, snapped to the feature + σ-smoothed with its junction
        /// endpoints PINNED, through the watertight ladder. First == coarse[0] and last == coarse[^1] exactly
        /// (endpoints never move), so seams sharing a junction stay coincident. Set by <see cref="SharedSeamSet.Build"/>.
        /// </summary>
        public IReadOnlyList<WzVec2> Refined { get; internal set; }

        /// <summary>True when the far side is void (region-vs-ocean/world-edge) — a coastline seam.</summary>
        public bool IsCoast => this.KeyB == null;

        /// <summary>True when the seam closes on itself with no junction (a whole region-pair border is one loop).</summary>
        public bool IsClosedLoop => this.Node0 == this.Node1;

        internal SharedSeam(string keyA, string keyB, long node0, long node1, IReadOnlyList<WzVec2> coarse)
        {
            this.KeyA = keyA;
            this.KeyB = keyB;
            this.Node0 = node0;
            this.Node1 = node1;
            this.Coarse = coarse;
            this.Refined = coarse; // until Build refines it
        }

        /// <summary>Does this seam bound region <paramref name="regionKey"/>?</summary>
        public bool Touches(string regionKey)
            => string.Equals(this.KeyA, regionKey, StringComparison.Ordinal)
            || string.Equals(this.KeyB, regionKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole world's border, decomposed into <see cref="SharedSeam"/>s — the single source of truth a
    /// region fill ring AND the ink both read, so they cannot disagree. Built ONCE from the coarse
    /// <see cref="RegionBoundaryGraph"/> (which already carries both region keys per 64 m edge); no worldgen.
    ///
    /// <para><b>Off by default.</b> Nothing consumes this until a caller opts in — the live overlay still
    /// runs its independent ink + per-region-ring refinements. This type is the substrate for that switch.
    /// Pure Tier-1 (no Unity), headless-testable. Feasibility + method proven in spike-004.</para>
    /// </summary>
    public sealed class SharedSeamSet
    {
        /// <summary>Every shared seam in the world (interior region-pairs + coastlines).</summary>
        public IReadOnlyList<SharedSeam> Seams { get; }

        /// <summary>Lattice-node ids classified as junctions (degree ≠ 2, or a region-pair change).</summary>
        public IReadOnlyCollection<long> JunctionNodes { get; }

        private readonly Dictionary<string, List<SharedSeam>> byRegion;

        private SharedSeamSet(IReadOnlyList<SharedSeam> seams, IReadOnlyCollection<long> junctions)
        {
            this.Seams = seams;
            this.JunctionNodes = junctions;
            this.byRegion = new Dictionary<string, List<SharedSeam>>(StringComparer.Ordinal);
            foreach (SharedSeam s in seams)
            {
                Index(s.KeyA, s);
                if (s.KeyB != null) Index(s.KeyB, s);
            }
        }

        private void Index(string key, SharedSeam s)
        {
            if (key == null) return;
            if (!this.byRegion.TryGetValue(key, out var l)) { l = new List<SharedSeam>(); this.byRegion[key] = l; }
            l.Add(s);
        }

        /// <summary>All seams bounding one region (its whole border, in arbitrary order), or empty.</summary>
        public IReadOnlyList<SharedSeam> SeamsFor(string regionKey)
            => regionKey != null && this.byRegion.TryGetValue(regionKey, out var l)
                ? (IReadOnlyList<SharedSeam>)l : Array.Empty<SharedSeam>();

        // ── Lattice-node packing (exact for the 64·n+32 corner lattice) ──────────────────────────────────
        // A seam endpoint sits at a zone CORNER: world = c·64 − 32 for integer corner index c. Pack (cx,cz)
        // into a long so endpoints compare/hash exactly (no float epsilon). Public so consumers + tests can
        // resolve a seam's Node0/Node1 to a world position (or hash a world corner to a node).
        public static long NodeId(WzVec2 p, double zone)
        {
            long cx = (long)Math.Round((p.X + zone / 2.0) / zone);
            long cz = (long)Math.Round((p.Z + zone / 2.0) / zone);
            return (cx << 32) ^ (cz & 0xffffffffL);
        }

        public static WzVec2 NodePos(long id, double zone)
        {
            int cx = (int)(id >> 32);
            int cz = (int)(id & 0xffffffffL);
            return new WzVec2(cx * zone - zone / 2.0, cz * zone - zone / 2.0);
        }

        private static string PairKey(string a, string b)
            => b == null ? a + "|~" : (string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a);

        /// <summary>
        /// Decompose <paramref name="graph"/>.Segments into shared seams, then refine each seam ONCE
        /// (feature snap + σ smoothing, junction endpoints pinned, through the watertight ladder). The
        /// coarse decomposition is deterministic; the refine matches <see cref="RegionRingRefiner"/>'s
        /// mechanism so a seam-built fill ring lands on the same curve the ink would.
        /// </summary>
        /// <param name="graph">The coarse boundary graph (from <see cref="RegionBoundaryExtractor"/>).</param>
        /// <param name="coastField">Height field whose iso the coast seams hug (null ⇒ coast seams stay coarse).</param>
        /// <param name="seamField">Biome-category field the interior seams hug (null ⇒ interior seams stay coarse).</param>
        /// <param name="options">Refine tunables (σ, snap reach…). Null ⇒ <see cref="SharedSeamRefineOptions.Default"/>.</param>
        /// <param name="zoneMeters">Zone size (default 64). The seam lattice is 64·n+32.</param>
        public static SharedSeamSet Build(RegionBoundaryGraph graph,
            IScalarField coastField = null, ICategoryField seamField = null,
            SharedSeamRefineOptions options = null, double zoneMeters = 64.0)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            options ??= SharedSeamRefineOptions.Default;

            // ── Build the seam-node graph: node → incident (otherNode, pairKey, segment) ──
            var incident = new Dictionary<long, List<(long other, string pair, BorderSegment seg)>>();
            void AddInc(long n, long other, string pair, BorderSegment s)
            {
                if (!incident.TryGetValue(n, out var l)) { l = new(); incident[n] = l; }
                l.Add((other, pair, s));
            }
            foreach (BorderSegment s in graph.Segments)
            {
                long a = NodeId(s.A, zoneMeters), b = NodeId(s.B, zoneMeters);
                string pair = PairKey(s.KeyA, s.KeyB);
                AddInc(a, b, pair, s);
                AddInc(b, a, pair, s);
            }

            // ── Junction = degree ≠ 2, OR the two incident segments carry different region-pairs ──
            var junctions = new HashSet<long>();
            foreach (var kv in incident)
            {
                var l = kv.Value;
                if (l.Count != 2 || l[0].pair != l[1].pair) junctions.Add(kv.Key);
            }

            // ── Walk maximal per-pair arcs, split at junctions ──
            var used = new HashSet<(long, long, string)>();
            (long, long, string) SegKey(long a, long b, string pair) => (Math.Min(a, b), Math.Max(a, b), pair);

            List<WzVec2> Walk(long start, long firstOther, string pair, out long end)
            {
                var pts = new List<WzVec2> { NodePos(start, zoneMeters) };
                long prev = start, cur = firstOther;
                used.Add(SegKey(start, firstOther, pair));
                int guard = graph.Segments.Count + 8;
                while (guard-- > 0)
                {
                    pts.Add(NodePos(cur, zoneMeters));
                    if (junctions.Contains(cur)) break;      // reached the next junction
                    if (cur == start) break;                 // closed loop back to origin
                    var next = incident[cur].Where(e => e.other != prev && e.pair == pair
                                                        && !used.Contains(SegKey(cur, e.other, pair))).ToList();
                    if (next.Count == 0) break;
                    long nextOther = next[0].other;
                    used.Add(SegKey(cur, nextOther, pair));
                    prev = cur; cur = nextOther;
                }
                end = cur;
                return pts;
            }

            var seams = new List<SharedSeam>();
            // Junction-bounded seams first (deterministic order: sort junctions, then incident by other-node+pair).
            foreach (long j in junctions.OrderBy(x => x))
            {
                foreach (var (other, pair, seg) in incident[j].OrderBy(e => e.other).ThenBy(e => e.pair, StringComparer.Ordinal))
                {
                    if (used.Contains(SegKey(j, other, pair))) continue;
                    var pts = Walk(j, other, pair, out long end);
                    var (ka, kb) = SplitPair(pair, seg);
                    seams.Add(new SharedSeam(ka, kb, j, end, pts));
                }
            }
            // Pure closed-loop seams (no junction anywhere): any segment still unused, in deterministic order.
            foreach (BorderSegment s in graph.Segments)
            {
                long a = NodeId(s.A, zoneMeters), b = NodeId(s.B, zoneMeters);
                string pair = PairKey(s.KeyA, s.KeyB);
                if (used.Contains(SegKey(a, b, pair))) continue;
                var pts = Walk(a, b, pair, out long end);
                var (ka, kb) = SplitPair(pair, s);
                seams.Add(new SharedSeam(ka, kb, a, end, pts));
            }

            // ── Refine each seam ONCE (endpoints pinned, watertight ladder) ──
            foreach (SharedSeam seam in seams)
                seam.Refined = SharedSeamRefiner.RefineOnce(seam, coastField, seamField, options);

            return new SharedSeamSet(seams, junctions);
        }

        private static (string, string) SplitPair(string pair, BorderSegment seg)
        {
            if (pair.EndsWith("|~", StringComparison.Ordinal)) return (seg.KeyA, null);
            int i = pair.IndexOf('|');
            return (pair.Substring(0, i), pair.Substring(i + 1));
        }
    }
}
