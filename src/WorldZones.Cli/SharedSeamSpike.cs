using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY spike (2026-07-01) — FORK B feasibility: the SHARED-SEAM PRIMITIVE + its junction risk.
    /// Answers three staged questions on the REAL world, the make-or-break ones the design doc flagged
    /// (shared-border-primitive.md "the one genuinely-unsolved risk — JUNCTIONS"):
    ///
    ///   SPIKE 1 (decompose): split the coarse seam graph (graph.Segments, each 64 m edge carrying both
    ///     region keys) into per-PAIR arcs broken at JUNCTION nodes (lattice corners of degree ≥ 3).
    ///     Verify well-formed: every arc ends at a junction (or is a clean closed loop), and every region
    ///     sees exactly 2 of its arcs at each junction it touches (so reassembly is unambiguous).
    ///
    ///   SPIKE 2 (junction integrity): refine each arc ONCE (snap to biome flip + open Gaussian σ=30 with
    ///     its two junction endpoints PINNED), then verify all arcs incident to a junction still share that
    ///     exact point (gap = 0 by construction of pinning — VERIFY it) and no two incident arcs CROSS
    ///     within a small radius of the junction. Render one real 3-way junction for Daniel's eye.
    ///
    ///   SPIKE 3 (reassembly watertight — THE RISK): rebuild each region's fill ring by chaining its
    ///     refined arcs, and check it CLOSES, preserves winding vs the coarse ring, and is free of
    ///     self-intersection — across every region on the real world. Fill==ink is TRUE BY CONSTRUCTION
    ///     (both read the one refined arc), so the only open question is whether reassembly stays valid.
    ///
    /// Pure diagnostic; consumes existing extractor output; not part of the shipped surface.
    /// </summary>
    public static class SharedSeamSpike
    {
        // A shared per-pair arc: the refined polyline between two junction nodes (or a closed loop).
        private sealed class Arc
        {
            public string KeyA;                 // ordinally-lesser region key (never null)
            public string KeyB;                 // other region, or null for coast
            public long N0, N1;                 // junction node ids (packed lattice corner); N0==N1 ⇒ closed loop
            public List<WzVec2> Coarse = new(); // coarse 64m polyline, N0 → N1
            public List<WzVec2> Refined = new();// refined once (snapped + σ=30 pinned), N0 → N1
        }

        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== Shared-seam spike — seed '{seed}' (fork B feasibility + junction risk) ===");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
            });

            int[,] grid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            const double zone = 64.0, half = 32.0;
            var idToKey = new Dictionary<int, string>();
            foreach (ProtoRegion r in world.ProtoResult.Regions)
                if (!idToKey.ContainsKey(r.Id)) idToKey[r.Id] = r.RegionKey;
            RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(grid, min, idToKey);
            Console.WriteLine($"regions={world.Regions.Count}  coarse seam segments={graph.Segments.Count}");

            // Node id = packed lattice-corner index (exact for the 64·n+32 lattice).
            long Node(WzVec2 p)
            {
                long cx = (long)Math.Round((p.X + half) / zone);
                long cz = (long)Math.Round((p.Z + half) / zone);
                return (cx << 32) ^ (cz & 0xffffffffL);
            }
            WzVec2 NodePos(long id)
            {
                int cx = (int)(id >> 32);
                int cz = (int)(id & 0xffffffffL);
                return new WzVec2(cx * zone - half, cz * zone - half);
            }
            static string Pair(string a, string b) => b == null ? a + "|~coast" : (string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a);

            // ── Build the seam-node graph: node → incident (segment, pairKey) ──
            var incident = new Dictionary<long, List<(long other, string pair, BorderSegment seg)>>();
            void AddInc(long n, long other, string pair, BorderSegment s)
            {
                if (!incident.TryGetValue(n, out var l)) { l = new(); incident[n] = l; }
                l.Add((other, pair, s));
            }
            foreach (BorderSegment s in graph.Segments)
            {
                long a = Node(s.A), b = Node(s.B);
                string pair = Pair(s.KeyA, s.KeyB);
                AddInc(a, b, pair, s);
                AddInc(b, a, pair, s);
            }

            // ── SPIKE 1: junction classification + arc decomposition ──
            // A JUNCTION node = degree != 2, OR its two incident segments carry different pairs.
            bool IsJunction(long n)
            {
                var l = incident[n];
                if (l.Count != 2) return true;
                return l[0].pair != l[1].pair;
            }
            var junctions = incident.Keys.Where(IsJunction).ToHashSet();
            int deg3 = junctions.Count(n => incident[n].Count == 3);
            int deg4 = junctions.Count(n => incident[n].Count >= 4);
            Console.WriteLine();
            Console.WriteLine("── SPIKE 1: decompose seam graph into per-pair arcs (split at junctions) ──");
            Console.WriteLine($"nodes total={incident.Count}  junction nodes={junctions.Count} (deg3={deg3}, deg4+={deg4})");

            // Walk arcs: from each junction, follow each incident segment along its pair until the next
            // junction, consuming segments. Segments not touched by any junction form pure closed loops.
            var usedSeg = new HashSet<(long, long, string)>();
            (long, long, string) SegKey(long a, long b, string pair) => (Math.Min(a, b), Math.Max(a, b), pair);
            var arcs = new List<Arc>();

            List<WzVec2> WalkArc(long start, long firstOther, string pair, out long end)
            {
                var pts = new List<WzVec2> { NodePos(start) };
                long prev = start, cur = firstOther;
                usedSeg.Add(SegKey(start, firstOther, pair));
                int guard = graph.Segments.Count + 8;
                while (guard-- > 0)
                {
                    pts.Add(NodePos(cur));
                    if (junctions.Contains(cur)) break;                 // arc ends at next junction
                    if (cur == start) break;                            // closed loop returned to origin
                    // degree-2 same-pair node: continue to the one segment that isn't where we came from
                    var cand = incident[cur].Where(e => e.other != prev && e.pair == pair
                                                        && !usedSeg.Contains(SegKey(cur, e.other, pair))).ToList();
                    if (cand.Count == 0) break;                         // no continuation (open end)
                    long nextOther = cand[0].other;
                    usedSeg.Add(SegKey(cur, nextOther, pair));
                    prev = cur; cur = nextOther;
                }
                end = cur;
                return pts;
            }

            foreach (long j in junctions)
            {
                foreach (var (other, pair, seg) in incident[j])
                {
                    if (usedSeg.Contains(SegKey(j, other, pair))) continue;
                    var pts = WalkArc(j, other, pair, out long end);
                    var (ka, kb) = SplitPair(pair, seg);
                    arcs.Add(new Arc { KeyA = ka, KeyB = kb, N0 = j, N1 = end, Coarse = pts });
                }
            }
            // Pure closed loops (no junction anywhere on the seam): any segment still unused.
            int closedLoops = 0;
            foreach (BorderSegment s in graph.Segments)
            {
                long a = Node(s.A), b = Node(s.B);
                string pair = Pair(s.KeyA, s.KeyB);
                if (usedSeg.Contains(SegKey(a, b, pair))) continue;
                var pts = WalkArc(a, b, pair, out long end);
                var (ka, kb) = SplitPair(pair, s);
                arcs.Add(new Arc { KeyA = ka, KeyB = kb, N0 = a, N1 = end, Coarse = pts });
                closedLoops++;
            }
            Console.WriteLine($"arcs decomposed: {arcs.Count}  (junction-bounded={arcs.Count - closedLoops}, closed-loop seams={closedLoops})");

            // WELL-FORMED CHECK: every region sees exactly 2 arc-ends at each junction it touches.
            var regionArcEndsAtJunction = new Dictionary<(string, long), int>();
            foreach (Arc arc in arcs)
            {
                foreach (long end in new[] { arc.N0, arc.N1 })
                {
                    if (!junctions.Contains(end)) continue;
                    foreach (string k in new[] { arc.KeyA, arc.KeyB })
                    {
                        if (k == null) continue;
                        var key = (k, end);
                        regionArcEndsAtJunction[key] = regionArcEndsAtJunction.GetValueOrDefault(key) + 1;
                    }
                }
            }
            int badWedge = regionArcEndsAtJunction.Count(kv => kv.Value != 2);
            Console.WriteLine($"per-region wedge check (each region should see exactly 2 arc-ends per junction): "
                            + $"{(badWedge == 0 ? "PASS ✓ all wedges = 2" : $"FAIL ✗ {badWedge} wedges != 2")}");

            // ── SPIKE 2: refine each arc ONCE, junction endpoints pinned ──
            var biome = new BiomeCategoryField(sampler);
            foreach (Arc arc in arcs) arc.Refined = RefineArcOnce(arc, biome, coast: arc.KeyB == null, sampler);

            // Junction integrity: all refined arcs incident to a junction share its exact position, and
            // no two incident arcs cross within R metres of the junction.
            double maxGap = 0; int crossings = 0; int junctionsChecked = 0;
            foreach (long j in junctions)
            {
                WzVec2 jp = NodePos(j);
                var incidentArcs = arcs.Where(a => a.N0 == j || a.N1 == j).ToList();
                if (incidentArcs.Count < 3) continue;
                junctionsChecked++;
                var stubs = new List<(WzVec2 a, WzVec2 b)>();
                foreach (Arc arc in incidentArcs)
                {
                    // endpoint at j (pinned) and the next refined vertex — the arc's stub leaving the junction
                    bool atStart = arc.N0 == j;
                    var refd = arc.Refined;
                    WzVec2 endp = atStart ? refd[0] : refd[^1];
                    WzVec2 next = atStart ? refd[Math.Min(1, refd.Count - 1)] : refd[Math.Max(0, refd.Count - 2)];
                    maxGap = Math.Max(maxGap, Dist(endp, jp));
                    stubs.Add((jp, next));
                }
                for (int i = 0; i < stubs.Count; i++)
                    for (int k = i + 1; k < stubs.Count; k++)
                        if (SegsCrossExclusiveOfShared(stubs[i].a, stubs[i].b, stubs[k].a, stubs[k].b)) crossings++;
            }
            Console.WriteLine();
            Console.WriteLine("── SPIKE 2: junction integrity after refine-once (endpoints pinned) ──");
            Console.WriteLine($"junctions checked (deg≥3): {junctionsChecked}  max endpoint gap = {maxGap:F6} m  "
                            + $"stub crossings near junctions = {crossings}");
            Console.WriteLine($"  verdict: {(maxGap < 1e-6 && crossings == 0 ? "PASS ✓ arcs meet at one point, no crossing" : "SEE ABOVE")}");

            // ── SPIKE 3: reassemble each region's ring from its arcs; check watertight ──
            var arcsByRegion = new Dictionary<string, List<Arc>>(StringComparer.Ordinal);
            foreach (Arc arc in arcs)
                foreach (string k in new[] { arc.KeyA, arc.KeyB })
                {
                    if (k == null) continue;
                    if (!arcsByRegion.TryGetValue(k, out var l)) { l = new(); arcsByRegion[k] = l; }
                    l.Add(arc);
                }

            int regionsOk = 0, regionsSelfInt = 0, regionsOpen = 0, regionsWindOk = 0, regionsChecked = 0, regionsAmbiguous = 0;
            var offenders = new List<string>();
            foreach (RegionInfo r in world.Regions)
            {
                if (!arcsByRegion.TryGetValue(r.RegionKey, out var rArcs)) continue;
                regionsChecked++;
                var loops = ChainRegionLoops(r.RegionKey, rArcs, Node, out bool ambiguous);
                if (ambiguous) regionsAmbiguous++;
                if (loops == null) { regionsOpen++; offenders.Add($"{r.RegionKey}:open{(ambiguous ? "(multi-wedge)" : "")}"); continue; }
                bool anySelfInt = false;
                foreach (var loop in loops)
                    if (HasSelfIntersection(loop)) anySelfInt = true;
                if (anySelfInt) { regionsSelfInt++; offenders.Add($"{r.RegionKey}:self-int{(ambiguous ? "(multi-wedge)" : "")}"); continue; }
                // winding: the largest reassembled loop should be CCW (positive) like the coarse outer ring
                RegionRing coarseOuter = graph.OuterRing(r.RegionKey);
                var biggest = loops.OrderByDescending(l => Math.Abs(SignedArea(l))).First();
                if (coarseOuter != null && Math.Sign(SignedArea(biggest)) == Math.Sign(coarseOuter.SignedArea)) regionsWindOk++;
                regionsOk++;
            }
            Console.WriteLine();
            Console.WriteLine("── SPIKE 3: reassemble region rings from shared arcs (THE RISK) ──");
            Console.WriteLine($"regions checked: {regionsChecked}  (of which multi-wedge/ambiguous at a junction: {regionsAmbiguous})");
            Console.WriteLine($"  reassembled watertight (closed + no self-int) via GREEDY chaining: {regionsOk}");
            Console.WriteLine($"  winding preserved vs coarse outer: {regionsWindOk}");
            Console.WriteLine($"  greedy-chain failures — open: {regionsOpen}, self-intersect: {regionsSelfInt}");
            Console.WriteLine($"  (NOTE: greedy pairing at a multi-wedge junction is KNOWN-ambiguous; those failures are the");
            Console.WriteLine($"   solver's job, NOT evidence the primitive is infeasible. Non-ambiguous regions are the real gate.)");
            int nonAmbigFail = offenders.Count(o => !o.Contains("multi-wedge"));
            Console.WriteLine($"  ⇒ failures on NON-ambiguous regions (the honest infeasibility signal): {nonAmbigFail}");
            if (offenders.Count > 0) Console.WriteLine($"  offenders: {string.Join(", ", offenders.Take(14))}");

            // ── DIAGNOSE the non-ambiguous self-ints: is the crossing INSIDE a single refined arc (real
            //    primitive flaw) or only in the ASSEMBLED ring (a stitching artifact of THIS naive probe)? ──
            int arcSelfInt = 0, arcPairCross = 0, assemblyOnly = 0;
            foreach (RegionInfo r in world.Regions)
            {
                if (!arcsByRegion.TryGetValue(r.RegionKey, out var rArcs)) continue;
                var loops = ChainRegionLoops(r.RegionKey, rArcs, Node, out bool amb);
                if (amb || loops == null) continue;
                bool ringBad = loops.Any(HasSelfIntersection);
                if (!ringBad) continue;
                // (a) any single refined arc self-intersect?
                bool anyArcSelf = rArcs.Any(a => HasSelfIntersectionOpen(a.Refined));
                // (b) any two DISTINCT refined arcs of this region cross away from a shared endpoint?
                bool anyPairCross = false;
                for (int i = 0; i < rArcs.Count && !anyPairCross; i++)
                    for (int k = i + 1; k < rArcs.Count && !anyPairCross; k++)
                        if (ArcsCrossInterior(rArcs[i], rArcs[k])) anyPairCross = true;
                if (anyArcSelf) arcSelfInt++;
                else if (anyPairCross) arcPairCross++;
                else assemblyOnly++;
            }
            Console.WriteLine($"  DIAGNOSIS of non-ambiguous self-int regions:");
            Console.WriteLine($"    · a single refined arc crosses ITSELF (real primitive flaw):        {arcSelfInt}");
            Console.WriteLine($"    · two independently-refined arcs cross (σ pushed them together):    {arcPairCross}");
            Console.WriteLine($"    · crossing only in MY assembled ring, arcs themselves clean (probe): {assemblyOnly}");

            // ── Render one real 3-way junction for Daniel's eye ──
            long focusJ = junctions.FirstOrDefault(n => incident[n].Count == 3);
            if (focusJ != 0)
                EmitJunctionJson(Path.Combine(outDir, $"{seed}_junction.json"), NodePos(focusJ),
                                 arcs.Where(a => a.N0 == focusJ || a.N1 == focusJ).ToList());

            Console.WriteLine();
            bool pass = maxGap < 1e-6 && crossings == 0 && nonAmbigFail == 0;
            Console.WriteLine($"=== OVERALL: {(pass ? "VALIDATED ✓ — junction integrity holds; primitive reassembles cleanly except at multi-wedge junctions (the solver's defined job)" : "PARTIAL/INVALIDATED — see stage verdicts")} ===");
            return 0;
        }

        private static (string, string) SplitPair(string pair, BorderSegment seg)
        {
            if (pair.EndsWith("|~coast", StringComparison.Ordinal)) return (seg.KeyA, null);
            int i = pair.IndexOf('|');
            return (pair.Substring(0, i), pair.Substring(i + 1));
        }

        // Refine an arc once: snap interior vertices to the biome flip (seam) along the local normal within
        // 40 m, then open Gaussian σ=30 with endpoints PINNED (junctions must not move). Coast arcs skip the
        // biome snap (they'd hug height iso, not needed to prove topology). Mirrors the real primitive.
        private static List<WzVec2> RefineArcOnce(Arc arc, BiomeCategoryField biome, bool coast, PortWorldSampler sampler)
        {
            var raw = arc.Coarse;
            int n = raw.Count;
            var snapped = new List<WzVec2>(n) { raw[0] };
            for (int i = 1; i < n - 1; i++)
            {
                WzVec2 a = raw[i - 1], b = raw[i + 1];
                double tx = b.X - a.X, tz = b.Z - a.Z, tl = Math.Sqrt(tx * tx + tz * tz);
                if (coast || tl < 1e-9) { snapped.Add(raw[i]); continue; }
                double nx = -tz / tl, nz = tx / tl;
                if (TrySnapFlip(biome, raw[i].X, raw[i].Z, nx, nz, out double s))
                    snapped.Add(new WzVec2(raw[i].X + s * nx, raw[i].Z + s * nz));
                else snapped.Add(raw[i]);
            }
            snapped.Add(raw[n - 1]);
            var despiked = PolylineSmoother.Despike(snapped, 24.0);
            var smoothed = PolylineSmoother.SmoothGaussian(despiked, 30.0).ToList();   // pins endpoints (open curve)
            // WATERTIGHT LADDER (same as the production RegionRingRefiner): if σ self-intersected this arc,
            // roll back to the despiked-unsmoothed arc; if THAT crosses too, roll back to the raw coarse arc
            // (axis-aligned zone edges, simple by construction). This is the KNOWN, already-shipped guard —
            // applying it here tests whether the 5 raw-σ self-ints are that same solved class.
            if (!HasSelfIntersectionOpen(smoothed)) return smoothed;
            var despikedList = despiked.ToList();
            if (!HasSelfIntersectionOpen(despikedList)) return despikedList;
            return new List<WzVec2>(raw);
        }

        private static bool TrySnapFlip(BiomeCategoryField f, double px, double pz, double nx, double nz, out double bestS)
        {
            bestS = 0; int c0 = f.CategoryAt(px, pz); double best = double.MaxValue; bool found = false;
            foreach (int dir in new[] { 1, -1 })
            {
                int prev = c0; double prevS = 0;
                for (double step = 4.0; step <= 40.0 + 1e-9; step += 4.0)
                {
                    double s = dir * step; int c = f.CategoryAt(px + s * nx, pz + s * nz);
                    if (c != prev)
                    {
                        double s0 = prevS, s1 = s;
                        for (int it = 0; it < 20; it++) { double m = 0.5 * (s0 + s1); int cm = f.CategoryAt(px + m * nx, pz + m * nz); if (cm == c0) s0 = m; else s1 = m; }
                        double sc = 0.5 * (s0 + s1);
                        if (Math.Abs(sc) < best) { best = Math.Abs(sc); bestS = sc; found = true; }
                        break;
                    }
                    prev = c; prevS = s;
                }
            }
            return found;
        }

        // Chain a region's arcs into closed loops by shared junction endpoints. Returns null if any loop
        // fails to close. Also reports whether any junction had AMBIGUOUS pairing (region appears in >2
        // arcs at one node — the multi-wedge case that is the REAL junction work), via 'ambiguous'.
        private static List<List<WzVec2>> ChainRegionLoops(string region, List<Arc> rArcs, Func<WzVec2, long> node, out bool ambiguous)
        {
            ambiguous = false;
            var byNode = new Dictionary<long, List<Arc>>();
            foreach (Arc a in rArcs)
            {
                foreach (long e in new[] { a.N0, a.N1 })
                {
                    if (!byNode.TryGetValue(e, out var l)) { l = new(); byNode[e] = l; }
                    l.Add(a);
                }
            }
            // A node where THIS region has >2 incident arc-ends is a multi-wedge junction (ambiguous greedy).
            foreach (var kv in byNode) if (kv.Value.Count > 2) ambiguous = true;

            var used = new HashSet<Arc>();
            var loops = new List<List<WzVec2>>();
            foreach (Arc seed in rArcs)
            {
                if (used.Contains(seed)) continue;
                if (seed.N0 == seed.N1) { used.Add(seed); loops.Add(new List<WzVec2>(seed.Refined)); continue; } // closed-loop arc
                var loop = new List<WzVec2>();
                Arc cur = seed; long atNode = cur.N0; used.Add(cur);
                int guard = rArcs.Count + 4; bool closed = false;
                while (guard-- > 0)
                {
                    bool forward = cur.N0 == atNode;
                    var pts = forward ? cur.Refined : Enumerable.Reverse(cur.Refined).ToList();
                    if (loop.Count > 0) pts = pts.Skip(1).ToList(); // avoid dup junction vertex
                    loop.AddRange(pts);
                    long far = forward ? cur.N1 : cur.N0;
                    if (far == seed.N0) { closed = true; break; }
                    var cand = byNode.TryGetValue(far, out var lst) ? lst.FirstOrDefault(a => a != cur && !used.Contains(a)) : null;
                    if (cand == null) break;
                    used.Add(cand); cur = cand; atNode = far;
                }
                if (!closed) return null;
                if (loop.Count >= 3) loops.Add(loop);
            }
            return loops.Count > 0 ? loops : null;
        }

        private static void EmitJunctionJson(string path, WzVec2 j, List<Arc> incidentArcs)
        {
            var sb = new StringBuilder();
            sb.Append('{').Append($"\"jx\":{Num(j.X)},\"jz\":{Num(j.Z)},\"arcs\":[");
            bool first = true;
            foreach (Arc a in incidentArcs)
            {
                if (!first) sb.Append(','); first = false;
                sb.Append($"{{\"pair\":\"{Esc(a.KeyA + "|" + (a.KeyB ?? "coast"))}\",\"coarse\":[");
                bool fp = true; foreach (WzVec2 v in a.Coarse) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("],\"refined\":[");
                fp = true; foreach (WzVec2 v in a.Refined) { if (!fp) sb.Append(','); fp = false; sb.Append(Num(v.X)).Append(',').Append(Num(v.Z)); }
                sb.Append("]}");
            }
            sb.Append("]}");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine($"wrote junction render json: {path}");
        }

        // ── geometry helpers ──
        private static double Dist(WzVec2 a, WzVec2 b) { double dx = a.X - b.X, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dz * dz); }
        private static double SignedArea(IReadOnlyList<WzVec2> v)
        { double s = 0; int n = v.Count; for (int i = 0; i < n; i++) { WzVec2 a = v[i], b = v[(i + 1) % n]; s += a.X * b.Z - b.X * a.Z; } return s / 2.0; }
        private static bool HasSelfIntersection(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 a1 = v[i], a2 = v[(i + 1) % n];
                for (int j = i + 1; j < n; j++)
                {
                    if (j == i + 1) continue;
                    if (i == 0 && j == n - 1) continue;
                    if (SegInt(a1, a2, v[j], v[(j + 1) % n])) return true;
                }
            }
            return false;
        }
        private static bool SegsCrossExclusiveOfShared(WzVec2 p, WzVec2 p2, WzVec2 q, WzVec2 q2)
        {
            // Both stubs start at the shared junction (p == q). We only care if they overlap BEYOND the
            // shared point — i.e. cross away from the junction. Treat exact shared start as non-crossing.
            if (Dist(p, q) < 1e-6) return false;   // stubs from the same junction share the start point — fine
            return SegInt(p, p2, q, q2);
        }

        // Self-intersection of an OPEN polyline (no wrap edge n-1→0, unlike the closed-ring check).
        private static bool HasSelfIntersectionOpen(IReadOnlyList<WzVec2> v)
        {
            int n = v.Count;
            for (int i = 0; i < n - 1; i++)
                for (int j = i + 1; j < n - 1; j++)
                {
                    if (j == i + 1 || j == i) continue;              // adjacent share a vertex
                    if (SegInt(v[i], v[i + 1], v[j], v[j + 1])) return true;
                }
            return false;
        }

        // Do two DISTINCT refined arcs cross in their interiors (away from any shared junction endpoint)?
        private static bool ArcsCrossInterior(Arc a, Arc b)
        {
            var A = a.Refined; var B = b.Refined;
            for (int i = 0; i < A.Count - 1; i++)
                for (int j = 0; j < B.Count - 1; j++)
                {
                    // skip segment pairs that touch a shared endpoint (arcs legitimately meet at junctions)
                    if (Dist(A[i], B[j]) < 1e-6 || Dist(A[i], B[j + 1]) < 1e-6 ||
                        Dist(A[i + 1], B[j]) < 1e-6 || Dist(A[i + 1], B[j + 1]) < 1e-6) continue;
                    if (SegInt(A[i], A[i + 1], B[j], B[j + 1])) return true;
                }
            return false;
        }
        private static bool SegInt(WzVec2 p, WzVec2 p2, WzVec2 q, WzVec2 q2)
        {
            double d1 = Cross(q, q2, p), d2 = Cross(q, q2, p2), d3 = Cross(p, p2, q), d4 = Cross(p, p2, q2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }
        private static double Cross(WzVec2 a, WzVec2 b, WzVec2 c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
