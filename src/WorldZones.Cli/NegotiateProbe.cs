using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY feasibility probe (2026-06-30) for NEGOTIATED region boundaries. Daniel's model: a
    /// protoregion is the OPENING BID; a settlement pass relocates each A|B border onto the best nearby
    /// geographic feature (river / biome-edge / shore — the SAME walls the router uses), trading whole
    /// zones (up to ~20% of a region) to do it, bounded by a size metric. Straight line only where no
    /// feature is in reach.
    ///
    /// This probe DOES NOT build the re-router. It measures whether the idea is WORTH building, by
    /// answering one question per adjacent region pair on the REAL seed:
    ///   "If we let the seam relocate to the best feature-aligned cut within a K-zone band, how much
    ///    border-on-feature do we GAIN, and what % of each region's territory MOVES to get it?"
    ///
    /// Method (deterministic, measure-don't-guess — RULE 0):
    ///   1. Build world (live opts). Per ordered pair (A,B) adjacent on the 64 m grid:
    ///   2. CONTESTABLE BAND = zones owned by A or B within ≤K zones of the shared seam (BFS distance on
    ///      the union of A∪B from the seam). A's and B's cores (>K from the seam) are FROZEN — never move.
    ///   3. FEATURE score per band zone = on a river (weight≥0.25) OR a land/land biome-edge OR a shore,
    ///      exactly RegionCostFieldBuilder's definitions. featureZones / bandZones = how much feature is
    ///      even available to snap to.
    ///   4. BEST RELOCATED CUT: re-partition the band between A-side and B-side by a min-cost flood that
    ///      makes feature zones CHEAP to sit on the boundary of and open ground EXPENSIVE — the dual of
    ///      the router's "features are walls". Concretely: assign each band zone to A or B by a competition
    ///      seeded from the frozen cores, where moving the A|B contact onto a feature zone is rewarded.
    ///      Implemented as: for each band zone, cost-to-A = graph distance from A-core where stepping
    ///      ONTO a non-feature cell costs 1 and onto a feature cell costs ε (cheap) — so the meeting line
    ///      (equidistant set) prefers to fall along feature chains. Same for B. Zone goes to the cheaper.
    ///   5. MEASURE: new boundary on-feature% vs current seam on-feature%; zones flipped vs each region's
    ///      total land = % CHURN. Aggregate the distribution across all pairs; this is the verdict.
    ///   6. Render 2 example pairs (biggest on-feature GAIN) so Daniel sees current-vs-relocated on terrain.
    ///
    /// HONEST SCOPE: ridgelines are NOT a feature here (no clean extractor — measured too noisy). The
    /// snappable set is river/biome-edge/shore, the proven-crisp ones. This is a FEASIBILITY number, not
    /// the settlement algorithm; the real one needs a global multi-pair deterministic solve (this scores
    /// pairs independently, so junction interactions are NOT modelled — flagged, not solved).
    /// </summary>
    public static class NegotiateProbe
    {
        const double Zone = 64.0, Half = 32.0;
        const int BandK = 4;                  // contestable band half-width in zones (~256 m each side)
        // MIN-CUT capacities (cost to CUT an edge between two adjacent band cells): cheap to run the seam
        // BETWEEN two feature cells, expensive across open ground. This is the dual of the router's wall
        // model — separating ALONG features is cheap, so the min-cut prefers feature chains.
        const long CutOnFeature = 1;          // both endpoints are feature cells → cheap to cut here
        const long CutOffFeature = 12;        // at least one endpoint open ground → expensive
        // STAY BIAS = the size-metric stand-in: a cell flips off its current owner only when the feature-
        // aligned cut saves MORE than this. ~half of CutOffFeature: meaningful resistance, but a genuine
        // feature (cut drops 12→1, saving 11 per edge) easily overcomes it. Tunable knob in the real thing.
        const long StayBias = 6;
        const float RiverWeightThreshold = 0.25f;

        public static int Run(string seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            Console.WriteLine($"=== NEGOTIATED-BOUNDARY feasibility probe — seed '{seed}' ===");
            Console.WriteLine($"(band K={BandK} zones/side, features = river≥{RiverWeightThreshold}/biome-edge/shore, ridge EXCLUDED — no extractor)\n");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            var river = (IRiverSampler)sampler;
            RegionWorld world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
            });
            int[,] rid = world.RegionIdGrid;
            int min = world.Grid.MinIndex;
            int gh = rid.GetLength(0), gw = rid.GetLength(1);
            Console.WriteLine($"regions={world.Regions.Count} grid={gw}x{gh} minIndex={min}");

            // ── Pre-sample land + feature per zone (router's exact definitions) ──
            var isLand = new bool[gh, gw];
            var biome = new BiomeType[gh, gw];
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    double wx = (gx + min) * Zone, wz = (gy + min) * Zone;
                    bool land = sampler.GetHeight((float)wx, (float)wz) >= HeightScalarField.SeaLevel;
                    isLand[gy, gx] = land;
                    if (land) biome[gy, gx] = sampler.GetBiome((float)wx, (float)wz);
                }
            // feature[gy,gx] = this LAND zone sits on a river / biome-edge / shore
            var feature = new bool[gh, gw];
            int[] dx4 = { 1, -1, 0, 0 }, dy4 = { 0, 0, 1, -1 };
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    if (!isLand[gy, gx]) continue;
                    double wx = (gx + min) * Zone, wz = (gy + min) * Zone;
                    river.GetRiverWeight((float)wx, (float)wz, out float w, out _);
                    bool onRiver = w >= RiverWeightThreshold;
                    bool biomeEdge = false, shore = false;
                    for (int d = 0; d < 4; d++)
                    {
                        int ax = gx + dx4[d], ay = gy + dy4[d];
                        if (ax < 0 || ax >= gw || ay < 0 || ay >= gh) { shore = true; continue; }
                        if (!isLand[ay, ax]) { shore = true; continue; }
                        if (biome[ay, ax] != biome[gy, gx]) biomeEdge = true;
                    }
                    feature[gy, gx] = onRiver || biomeEdge || shore;
                }

            // ── region land-zone totals + label→info ──
            var landByLabel = new Dictionary<int, int>();
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                    if (rid[gy, gx] >= 0 && isLand[gy, gx])
                        landByLabel[rid[gy, gx]] = landByLabel.GetValueOrDefault(rid[gy, gx]) + 1;
            var infoByLabel = world.Regions.ToDictionary(r => r.TransientId, r => r);

            // ── adjacency: unordered land-land region pairs sharing a 4-neighbour seam ──
            var pairSeam = new Dictionary<(int, int), int>();   // pair → shared seam-edge count (zone borders)
            for (int gy = 0; gy < gh; gy++)
                for (int gx = 0; gx < gw; gx++)
                {
                    int a = rid[gy, gx];
                    if (a < 0 || !isLand[gy, gx]) continue;
                    for (int d = 0; d < 2; d++)  // only +x,+y to count each seam edge once
                    {
                        int ax = gx + dx4[d], ay = gy + dy4[d];
                        if (ax < 0 || ax >= gw || ay < 0 || ay >= gh) continue;
                        int b = rid[ay, ax];
                        if (b < 0 || !isLand[ay, ax] || b == a) continue;
                        var key = a < b ? (a, b) : (b, a);
                        pairSeam[key] = pairSeam.GetValueOrDefault(key) + 1;
                    }
                }
            Console.WriteLine($"adjacent land-land region pairs: {pairSeam.Count}\n");

            // ── per-pair negotiation measurement ──
            var rows = new List<Row>();
            foreach (var kv in pairSeam)
            {
                if (kv.Value < 3) continue;     // ignore trivial 1–2 zone touchings (noise)
                Row r = MeasurePair(kv.Key.Item1, kv.Key.Item2, rid, isLand, feature, gh, gw, landByLabel);
                if (r != null) rows.Add(r);
            }

            // ── aggregate distribution ──
            Console.WriteLine("── AGGREGATE (the verdict) ──");
            Console.WriteLine($"pairs measured (≥3 seam edges, both have a band): {rows.Count}");
            if (rows.Count == 0) { Console.WriteLine("no measurable pairs"); return 0; }

            double meanCur = rows.Average(r => r.CurOnFeaturePct);
            double meanNew = rows.Average(r => r.NewOnFeaturePct);
            Console.WriteLine($"border on-feature%  current: mean {meanCur:F1}%   relocated: mean {meanNew:F1}%   GAIN +{meanNew-meanCur:F1} pts");
            Console.WriteLine();
            Console.WriteLine("on-feature GAIN distribution (pts):");
            Histogram(rows.Select(r => r.NewOnFeaturePct - r.CurOnFeaturePct).ToList(),
                      new[] { -1, 0, 5, 10, 20, 30, 50, 100 });
            Console.WriteLine();
            Console.WriteLine("CHURN distribution (max % of either region's land that moves):");
            Histogram(rows.Select(r => r.MaxChurnPct).ToList(),
                      new[] { 0, 1, 5, 10, 20, 30, 50, 100 });
            Console.WriteLine();

            // the money quadrant: high gain achievable at modest churn
            int worthwhile = rows.Count(r => (r.NewOnFeaturePct - r.CurOnFeaturePct) >= 10 && r.MaxChurnPct <= 20);
            int bigGain = rows.Count(r => (r.NewOnFeaturePct - r.CurOnFeaturePct) >= 10);
            int cheapNoGain = rows.Count(r => (r.NewOnFeaturePct - r.CurOnFeaturePct) < 5);
            Console.WriteLine($"VERDICT BUCKETS:");
            Console.WriteLine($"  worthwhile (gain≥10pts AND churn≤20%): {worthwhile} ({Pct(worthwhile, rows.Count)})");
            Console.WriteLine($"  big gain (≥10pts, any churn):          {bigGain} ({Pct(bigGain, rows.Count)})");
            Console.WriteLine($"  ~no gain (<5pts — straight line wins):  {cheapNoGain} ({Pct(cheapNoGain, rows.Count)})");
            Console.WriteLine();

            // ── render SUBSTANTIVE example pairs: long enough seam to read (≥12 edges), real gain, sorted
            //    by gain. (Picking by raw gain alone surfaces trivial 4-edge 0→100 seams — visually mute.) ──
            var examples = rows.Where(r => r.SeamEdges >= 12)
                               .OrderByDescending(r => (r.NewOnFeaturePct - r.CurOnFeaturePct))
                               .Take(3).ToList();
            int ei = 0;
            foreach (var ex in examples)
            {
                ei++;
                var ia = infoByLabel.GetValueOrDefault(ex.A);
                var ib = infoByLabel.GetValueOrDefault(ex.B);
                Console.WriteLine($"EXAMPLE {ei}: {ia?.Name ?? ex.A.ToString()} | {ib?.Name ?? ex.B.ToString()}  "
                    + $"on-feat {ex.CurOnFeaturePct:F0}%→{ex.NewOnFeaturePct:F0}%  churn {ex.MaxChurnPct:F0}%");
                RenderPair(outDir, seed, ei, ex, sampler, rid, isLand, biome, feature, min, gh, gw);
            }

            // write a compact CSV for any follow-up analysis
            string csv = Path.Combine(outDir, $"{seed}_negotiate.csv");
            using (var sw = new StreamWriter(csv))
            {
                sw.WriteLine("A,B,seamEdges,bandZones,featurePctInBand,curOnFeatPct,newOnFeatPct,gainPts,zonesFlipped,churnA,churnB,maxChurn");
                foreach (var r in rows.OrderByDescending(r => r.NewOnFeaturePct - r.CurOnFeaturePct))
                    sw.WriteLine($"{r.A},{r.B},{r.SeamEdges},{r.BandZones},{r.FeaturePctInBand:F1},{r.CurOnFeaturePct:F1},"
                        + $"{r.NewOnFeaturePct:F1},{r.NewOnFeaturePct-r.CurOnFeaturePct:F1},{r.ZonesFlipped},{r.ChurnAPct:F1},{r.ChurnBPct:F1},{r.MaxChurnPct:F1}");
            }
            Console.WriteLine($"\nWrote {csv} ({rows.Count} pairs)");
            return 0;
        }

        sealed class Row
        {
            public int A, B, SeamEdges, BandZones, ZonesFlipped;
            public double FeaturePctInBand, CurOnFeaturePct, NewOnFeaturePct, ChurnAPct, ChurnBPct, MaxChurnPct;
            // band geometry kept for the render
            public List<(int gy, int gx)> Band;
            public Dictionary<(int,int), int> NewOwner;  // band zone → A or B after relocation
        }

        static Row MeasurePair(int A, int B, int[,] rid, bool[,] isLand, bool[,] feature,
            int gh, int gw, Dictionary<int,int> landByLabel)
        {
            // ── 1. distance-from-seam within A∪B (BFS on the union, seeded from seam-adjacent zones) ──
            var inUnion = new Func<int,int,bool>((gy,gx) =>
                gy>=0 && gx>=0 && gy<gh && gx<gw && isLand[gy,gx] && (rid[gy,gx]==A || rid[gy,gx]==B));
            int[] dx4 = { 1, -1, 0, 0 }, dy4 = { 0, 0, 1, -1 };

            var dist = new int[gh, gw];
            for (int y=0;y<gh;y++) for (int x=0;x<gw;x++) dist[y,x] = int.MaxValue;
            var q = new Queue<(int,int)>();
            // seam-adjacent: a union zone whose 4-neighbour is the OTHER region
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
            {
                if (!inUnion(gy,gx)) continue;
                int self = rid[gy,gx];
                bool adj=false;
                for (int d=0; d<4; d++){ int ax=gx+dx4[d], ay=gy+dy4[d]; if (ax<0||ax>=gw||ay<0||ay>=gh) continue;
                    if (isLand[ay,ax] && (rid[ay,ax]==A||rid[ay,ax]==B) && rid[ay,ax]!=self){ adj=true; break; } }
                if (adj){ dist[gy,gx]=0; q.Enqueue((gy,gx)); }
            }
            if (q.Count==0) return null;
            while (q.Count>0)
            {
                var (cy,cx)=q.Dequeue();
                for (int d=0; d<4; d++){ int ax=cx+dx4[d], ay=cy+dy4[d];
                    if (!inUnion(ay,ax)) continue;
                    if (dist[ay,ax] > dist[cy,cx]+1){ dist[ay,ax]=dist[cy,cx]+1; q.Enqueue((ay,ax)); } }
            }

            // ── 2. band = union zones within K of the seam; cores = the rest (frozen) ──
            var band = new List<(int gy,int gx)>();
            for (int gy=0;gy<gh;gy++) for (int gx=0;gx<gw;gx++)
                if (inUnion(gy,gx) && dist[gy,gx] <= BandK) band.Add((gy,gx));
            if (band.Count < 4) return null;

            // ── 3. feature availability in band ──
            int featCells = band.Count(c => feature[c.gy, c.gx]);
            double featurePct = 100.0 * featCells / band.Count;

            // ── current seam on-feature%: of the seam EDGES, how many run between two feature zones? ──
            int seamEdges=0, seamOnFeat=0;
            foreach (var (gy,gx) in band)
            {
                int self=rid[gy,gx];
                for (int d=0; d<2; d++){ int ax=gx+dx4[d], ay=gy+dy4[d]; if (ax<0||ax>=gw||ay<0||ay>=gh) continue;
                    if (!isLand[ay,ax]) continue; int o=rid[ay,ax]; if (o!=A && o!=B) continue;
                    if (o==self) continue;
                    seamEdges++;
                    if (feature[gy,gx] && feature[ay,ax]) seamOnFeat++;
                }
            }
            if (seamEdges==0) return null;
            double curOnFeat = 100.0 * seamOnFeat / seamEdges;

            // ── 4. relocate: s-t MIN-CUT that prefers to separate A from B ALONG features ──
            // This is the DUAL of the wall-flood and the right model for Daniel's question: a min-cut may
            // travel far off the cost-balanced midline to land the border on a feature, paying territory
            // (churn) to do it — exactly what a balanced watershed CANNOT do. Edge between adjacent band
            // cells = CHEAP to cut (1) when BOTH are feature cells (the seam runs on terrain there),
            // EXPENSIVE (12) otherwise — so the min-cut separates along feature chains where they exist and
            // only crosses open ground where forced. A-core anchors SOURCE (∞), B-core anchors SINK (∞),
            // so cores never move; the band in between is re-owned by which side of the cut it lands on.
            var idx = new Dictionary<(int,int),int>();
            for (int i=0;i<band.Count;i++) idx[band[i]]=i;
            var newOwner = MinCutAssign(A, B, band, idx, dist, rid, isLand, feature, gh, gw, BandK);
            int flipped=0;
            foreach (var c in band){ if (newOwner[c] != rid[c.gy,c.gx]) flipped++; }

            // ── 5. new boundary on-feature%: edges between differing NEW owners ──
            int newEdges=0, newOnFeat=0;
            int Owner((int gy,int gx) c) => newOwner.TryGetValue(c, out var o) ? o : rid[c.gy, c.gx];
            foreach (var (gy,gx) in band)
            {
                int self=Owner((gy,gx));
                for (int d=0; d<2; d++){ int ax=gx+dx4[d], ay=gy+dy4[d]; if (ax<0||ax>=gw||ay<0||ay>=gh) continue;
                    if (!isLand[ay,ax]) continue; int o=rid[ay,ax]; if (o!=A && o!=B) continue;
                    int on=Owner((ay,ax)); if (on==self) continue;
                    newEdges++;
                    if (feature[gy,gx] && feature[ay,ax]) newOnFeat++;
                }
            }
            double newOnFeatPct = newEdges>0 ? 100.0*newOnFeat/newEdges : curOnFeat;

            // ── churn: zones flipping away from each region / that region's land total ──
            int landA = landByLabel.GetValueOrDefault(A,1), landB = landByLabel.GetValueOrDefault(B,1);
            // A loses the zones that were A and became B; gains the B→A ones. Churn = moved off original.
            int aLost=0, bLost=0;
            foreach (var c in band){ int cur=rid[c.gy,c.gx]; int no=newOwner[c]; if (cur==A&&no==B) aLost++; if (cur==B&&no==A) bLost++; }
            double churnA = 100.0*aLost/landA, churnB = 100.0*bLost/landB;

            return new Row {
                A=A, B=B, SeamEdges=seamEdges, BandZones=band.Count, ZonesFlipped=flipped,
                FeaturePctInBand=featurePct, CurOnFeaturePct=curOnFeat, NewOnFeaturePct=newOnFeatPct,
                ChurnAPct=churnA, ChurnBPct=churnB, MaxChurnPct=Math.Max(churnA,churnB),
                Band=band, NewOwner=newOwner
            };
        }

        // s-t MIN-CUT relocation (Dinic max-flow). Nodes = band cells + SOURCE(A) + SINK(B). An A-core-
        // adjacent band cell is wired SOURCE→cell=∞ (locks it to A); a B-core-adjacent cell cell→SINK=∞.
        // Between adjacent band cells: undirected capacity = CUT COST = 1 if BOTH cells are features (cheap
        // to run the seam there) else FeatureWall (12, expensive to cut across open ground). The min-cut is
        // the cheapest A|B separation; cells on the SOURCE side → A, SINK side → B. Determinism: integer caps,
        // fixed node order (band index), BFS-level Dinic — same seed ⇒ same cut.
        internal static Dictionary<(int,int),int> MinCutAssign(int A, int B, List<(int gy,int gx)> band,
            Dictionary<(int,int),int> idx, int[,] dist, int[,] rid, bool[,] isLand, bool[,] feature,
            int gh, int gw, int K)
        {
            int N = band.Count, S = N, T = N + 1, V = N + 2;
            const long INF = 1L << 50;
            long CutCap(int i, int j) => (feature[band[i].gy,band[i].gx] && feature[band[j].gy,band[j].gx]) ? CutOnFeature : CutOffFeature;

            var dinic = new Dinic(V);
            int[] dx4 = { 1, -1, 0, 0 }, dy4 = { 0, 0, 1, -1 };
            // intra-band undirected edges (each unordered pair once)
            for (int i=0;i<N;i++)
            {
                var (gy,gx)=band[i];
                for (int d=0; d<2; d++)   // +x,+y only → each pair once
                {
                    int ax=gx+dx4[d], ay=gy+dy4[d];
                    if (idx.TryGetValue((ay,ax), out int j))
                        dinic.AddUndirected(i, j, CutCap(i, j));
                }
            }
            // anchors to source/sink via frozen cores
            for (int i=0;i<N;i++)
            {
                var (gy,gx)=band[i];
                bool touchA=false, touchB=false;
                for (int d=0; d<4; d++){ int ax=gx+dx4[d], ay=gy+dy4[d]; if (ax<0||ax>=gw||ay<0||ay>=gh) continue;
                    if (!isLand[ay,ax]) continue;
                    if (rid[ay,ax]==A && dist[ay,ax]>K) touchA=true;
                    if (rid[ay,ax]==B && dist[ay,ax]>K) touchB=true; }
                if (touchA) dinic.AddDirected(S, i, INF);
                if (touchB) dinic.AddDirected(i, T, INF);
            }
            // STAY BIAS (the size-metric stand-in): wire each band cell to its CURRENT owner's terminal with
            // a small capacity. A cell only flips when a feature-aligned cut saves MORE than StayBias — so
            // where there's no feature, the cut stays on the current border (zero spurious churn), and where
            // a strong feature sits off-center, the cut still relocates and pays the territory. This is the
            // miniature of Daniel's rule: cede ONLY for a good boundary, resist gratuitous movement.
            for (int i=0;i<N;i++)
            {
                int cur = rid[band[i].gy, band[i].gx];
                if (cur==A) dinic.AddDirected(S, i, StayBias);
                else if (cur==B) dinic.AddDirected(i, T, StayBias);
            }
            // (Stay-bias guarantees every current-A cell has a source edge and every current-B cell a sink
            // edge, so both terminals are always connected — no thin-region fallback needed.)
            dinic.MaxFlow(S, T);
            bool[] srcSide = dinic.MinCutSourceSide(S);   // reachable from S in residual = A side

            var owner = new Dictionary<(int,int),int>(N);
            for (int i=0;i<N;i++) owner[band[i]] = srcSide[i] ? A : B;
            return owner;
        }

        // ───────────────────────── render ─────────────────────────
        static void RenderPair(string outDir, string seed, int n, Row r, IWorldSampler sampler,
            int[,] rid, bool[,] isLand, BiomeType[,] biome, bool[,] feature, int min, int gh, int gw)
        {
            // bbox of the band
            int minx=gw,maxx=0,miny=gh,maxy=0;
            foreach (var (gy,gx) in r.Band){ if(gx<minx)minx=gx; if(gx>maxx)maxx=gx; if(gy<miny)miny=gy; if(gy>maxy)maxy=gy; }
            int pad=3; minx=Math.Max(0,minx-pad); miny=Math.Max(0,miny-pad); maxx=Math.Min(gw-1,maxx+pad); maxy=Math.Min(gh-1,maxy+pad);
            int Wt=maxx-minx+1, Ht=maxy-miny+1;
            int scale=Math.Max(4, 520/Math.Max(Wt,Ht));
            int pw=Wt*scale, ph=Ht*scale, gap=22;
            int W=pw*2+gap, H=ph;
            byte[] img=new byte[W*H*3];

            int Owner((int gy,int gx) c) => r.NewOwner.TryGetValue(c, out var o) ? o : rid[c.gy, c.gx];

            void Panel(int xoff, bool relocated)
            {
                for (int ty=0;ty<Ht;ty++) for (int tx=0;tx<Wt;tx++)
                {
                    int gx=minx+tx, gy=miny+ty;
                    int py0=(Ht-1-ty)*scale, px0=xoff+tx*scale;
                    (byte r,byte g,byte b) col;
                    if (!isLand[gy,gx]) col=(40,60,98);
                    else col=BiomeCol(biome[gy,gx]);
                    // feature zones: brighten
                    if (isLand[gy,gx] && feature[gy,gx]) col=Mix(col,(255,238,150),0.35);
                    // owner tint
                    int own = relocated ? Owner((gy,gx)) : rid[gy,gx];
                    if (own==r.A) col=Mix(col,(255,170,70),0.30);
                    else if (own==r.B) col=Mix(col,(110,150,255),0.30);
                    for (int dy=0;dy<scale;dy++) for (int dx=0;dx<scale;dx++){ int o=((py0+dy)*W+(px0+dx))*3; img[o]=col.r; img[o+1]=col.g; img[o+2]=col.b; }
                }
                // draw the A|B boundary edges, THICK, colour-coded: GREEN where the seam runs between two
                // feature cells (on-feature — the win), RED where it crosses open ground (the residual).
                int t2 = Math.Max(2, scale/3);
                for (int ty=0;ty<Ht;ty++) for (int tx=0;tx<Wt;tx++)
                {
                    int gx=minx+tx, gy=miny+ty;
                    if (!isLand[gy,gx]) continue;
                    int self = relocated ? Owner((gy,gx)) : rid[gy,gx];
                    if (self!=r.A && self!=r.B) continue;
                    int[] ddx={1,0}, ddy={0,1};
                    for (int d=0; d<2; d++){ int ax=gx+ddx[d], ay=gy+ddy[d]; if (ax<0||ax>=gw||ay<0||ay>=gh) continue; if(!isLand[ay,ax])continue;
                        int o2 = relocated ? Owner((ay,ax)) : rid[ay,ax]; if (o2!=r.A&&o2!=r.B) continue; if (o2==self) continue;
                        bool onFeat = feature[gy,gx] && feature[ay,ax];
                        (byte r,byte g,byte b) sc = onFeat ? ((byte)40,(byte)230,(byte)90) : ((byte)235,(byte)50,(byte)40);
                        int py0=(Ht-1-ty)*scale, px0=xoff+tx*scale;
                        if (d==0) for (int yy=0;yy<scale;yy++) for(int t=-t2;t<=t2;t++){ int X=px0+scale-1+t, Y=py0+yy; if(X>=xoff&&X<xoff+pw&&Y>=0&&Y<H){int o=(Y*W+X)*3;img[o]=sc.r;img[o+1]=sc.g;img[o+2]=sc.b;} }
                        else for (int xx=0;xx<scale;xx++) for(int t=-t2;t<=t2;t++){ int X=px0+xx, Y=py0+t; if(X>=xoff&&X<xoff+pw&&Y>=0&&Y<H){int o=(Y*W+X)*3;img[o]=sc.r;img[o+1]=sc.g;img[o+2]=sc.b;} }
                    }
                }
            }
            Panel(0,false);
            Panel(pw+gap,true);
            for (int y=0;y<H;y++) for (int x=pw;x<pw+gap;x++){ int o=(y*W+x)*3; img[o]=12;img[o+1]=13;img[o+2]=16; }
            string p=Path.Combine(outDir,$"{seed}_negotiate_ex{n}.png");
            PngWriter.Write(p,W,H,img);
            Console.WriteLine($"  → {p} (LEFT current, RIGHT relocated; bright=feature zone, orange=A, blue=B)");
        }

        static (byte,byte,byte) BiomeCol(BiomeType b)=>b switch{
            BiomeType.Meadows=>(96,124,64), BiomeType.Swamp=>(84,80,54), BiomeType.Mountain=>(188,192,200),
            BiomeType.BlackForest=>(52,84,60), BiomeType.Plains=>(164,150,88), BiomeType.AshLands=>(138,64,50),
            BiomeType.DeepNorth=>(200,214,226), BiomeType.Mistlands=>(104,90,114), _=>(70,70,76) };
        static (byte r,byte g,byte b) Mix((byte r,byte g,byte b) a,(int r,int g,int b) t,double k)
            => ((byte)(a.r+(t.r-a.r)*k),(byte)(a.g+(t.g-a.g)*k),(byte)(a.b+(t.b-a.b)*k));

        static void Histogram(List<double> vals, int[] edges)
        {
            for (int i=0;i<edges.Length-1;i++)
            {
                int lo=edges[i], hi=edges[i+1];
                int c = vals.Count(v => v>=lo && v<hi);
                string bar = new string('█', Math.Min(50, c));
                Console.WriteLine($"  [{lo,3},{hi,3}) {c,4}  {bar}");
            }
        }
        static string Pct(int a, int b)=> b>0 ? $"{100.0*a/b:F0}%" : "n/a";
    }

    /// <summary>Deterministic Dinic max-flow / min-cut over integer capacities (throwaway, probe-local).</summary>
    sealed class Dinic
    {
        struct Edge { public int to, rev; public long cap; }
        readonly List<Edge>[] g;
        readonly int n;
        int[] level, it;

        public Dinic(int n){ this.n=n; g=new List<Edge>[n]; for(int i=0;i<n;i++) g[i]=new List<Edge>(); }

        public void AddDirected(int s,int t,long cap)
        {
            g[s].Add(new Edge{to=t,rev=g[t].Count,cap=cap});
            g[t].Add(new Edge{to=s,rev=g[s].Count-1,cap=0});
        }
        public void AddUndirected(int u,int v,long cap)
        {
            g[u].Add(new Edge{to=v,rev=g[v].Count,cap=cap});
            g[v].Add(new Edge{to=u,rev=g[u].Count-1,cap=cap});
        }

        bool Bfs(int s,int t)
        {
            level=new int[n]; for(int i=0;i<n;i++) level[i]=-1;
            var q=new Queue<int>(); level[s]=0; q.Enqueue(s);
            while(q.Count>0){ int u=q.Dequeue(); foreach(var e in g[u]) if(e.cap>0 && level[e.to]<0){ level[e.to]=level[u]+1; q.Enqueue(e.to); } }
            return level[t]>=0;
        }
        long Dfs(int u,int t,long f)
        {
            if(u==t) return f;
            for(; it[u]<g[u].Count; it[u]++)
            {
                var e=g[u][it[u]];
                if(e.cap>0 && level[e.to]==level[u]+1)
                {
                    long d=Dfs(e.to,t,Math.Min(f,e.cap));
                    if(d>0){ var ed=g[u][it[u]]; ed.cap-=d; g[u][it[u]]=ed; var re=g[e.to][e.rev]; re.cap+=d; g[e.to][e.rev]=re; return d; }
                }
            }
            return 0;
        }
        public long MaxFlow(int s,int t)
        {
            long flow=0;
            while(Bfs(s,t)){ it=new int[n]; long f; while((f=Dfs(s,t,long.MaxValue))>0) flow+=f; }
            return flow;
        }
        // after MaxFlow: nodes reachable from source in the residual graph = source side of the min cut.
        public bool[] MinCutSourceSide(int s)
        {
            var vis=new bool[n]; var q=new Queue<int>(); vis[s]=true; q.Enqueue(s);
            while(q.Count>0){ int u=q.Dequeue(); foreach(var e in g[u]) if(e.cap>0 && !vis[e.to]){ vis[e.to]=true; q.Enqueue(e.to); } }
            return vis;
        }
    }
}
