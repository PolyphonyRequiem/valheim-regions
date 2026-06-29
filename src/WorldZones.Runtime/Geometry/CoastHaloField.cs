using System;
using System.Collections.Generic;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>The two coast-halo render modes — which side of the shoreline the fade falls on.</summary>
    public enum CoastHaloMode
    {
        /// <summary>No halo.</summary>
        Off = 0,

        /// <summary>Fade sits on the WATER side: strong at the shore, fading OUT into the ocean
        /// (the "surf glow" / coast-halo aesthetic). Mode A.</summary>
        Seaward = 1,

        /// <summary>Fade sits on the LAND side: strong at the shore, fading IN toward the region
        /// interior (the "territory glow" aesthetic). Mode B.</summary>
        Inland = 2,
    }

    /// <summary>
    /// A baked, pure-geometry coast-halo field: a fine grid over a world window carrying, per texel,
    /// the SIGNED distance (world metres) to the nearest TRUE-OCEAN shoreline — positive on the land
    /// side, negative on the water side. A consumer turns that distance + a <see cref="CoastHaloMode"/>
    /// into a fade alpha via <see cref="Alpha"/>. This is the soft-fill answer to the "blocky fill" gap:
    /// because the edge is a FADE (no hard boundary), there is no staircase to fight — the 64 m zone
    /// lattice never enters here; distance is measured from the real <see cref="IScalarField"/> shoreline
    /// at the field's own resolution.
    ///
    /// <para><b>True-ocean only.</b> Land/water is the height field vs <see cref="SeaLevel"/>. But only
    /// water CONNECTED to the window edge (a flood fill) counts as ocean and grows a halo; water fully
    /// enclosed by land is a LAKE and is left alone (no halo), so a lake-dotted region doesn't sprout a
    /// glow around every pond. This matches the locked design (docs/design/region-borders.md → "Daniel's
    /// end-state": stop at true ocean; enclosed water = lake).</para>
    ///
    /// <para><b>Coordinate frame.</b> Texel <c>[gy, gx]</c> covers the world square whose MIN corner is
    /// <c>(OriginX + gx·Cell, OriginZ + gy·Cell)</c>; the classified point is the texel CENTRE. The
    /// origin/cell are caller-supplied so the field can share the <see cref="RegionTextureBaker"/> /
    /// <see cref="RegionBoundaryExtractor"/> lattice (origin <c>minIndex·64 − 32</c>, cell a divisor of
    /// 64) with no half-texel drift, or cover an arbitrary window at an arbitrary resolution.</para>
    ///
    /// <para>Pure (reads only an <see cref="IScalarField"/> height seam, no Unity, no game types) so it
    /// runs under the net8 headless test net. See docs/design/region-render-seam.md.</para>
    /// </summary>
    public sealed class CoastHaloField
    {
        /// <summary>Sea level in world metres — the land/water threshold (vanilla water = 30).</summary>
        public const double SeaLevel = 30.0;

        /// <summary>Default halo band width (metres): the fade spans this far from the shoreline.
        /// 96 m ≈ 1.5 zones, the locked default (docs/design/region-borders.md).</summary>
        public const double DefaultBandMeters = 96.0;

        private readonly double[,] signedDistance;   // [gy, gx], +land / −water, clamped to ±maxDist
        private readonly bool[,] isOceanSide;         // texel is water AND connected to the window edge
        private readonly float[,] depthGate;          // [gy, gx], seaward glow scale ∈ [0,1]; 1 everywhere when gating off
        private readonly int[,] nearestRegionId;      // [gy, gx], region id of the nearest owned-land coast, −1 when none / not provided

        /// <summary>Texel size in world metres.</summary>
        public double Cell { get; }

        /// <summary>World-X of the field's min corner (texel [*,0]'s left edge).</summary>
        public double OriginX { get; }

        /// <summary>World-Z of the field's min corner (texel [0,*]'s bottom edge).</summary>
        public double OriginZ { get; }

        /// <summary>Texel rows (gy extent).</summary>
        public int Height { get; }

        /// <summary>Texel columns (gx extent).</summary>
        public int Width { get; }

        /// <summary>The band width (m) the field was built for — the fade's reach from the shore.</summary>
        public double BandMeters { get; }

        private CoastHaloField(double[,] signed, bool[,] oceanSide, float[,] depthGate, int[,] nearestRegionId,
                               double cell, double originX, double originZ, double band)
        {
            this.signedDistance = signed;
            this.isOceanSide = oceanSide;
            this.depthGate = depthGate;
            this.nearestRegionId = nearestRegionId;
            this.Cell = cell;
            this.OriginX = originX;
            this.OriginZ = originZ;
            this.Height = signed.GetLength(0);
            this.Width = signed.GetLength(1);
            this.BandMeters = band;
        }

        /// <summary>
        /// Build the field over a world window. <paramref name="height"/> is the terrain height seam
        /// (e.g. <see cref="HeightScalarField"/>); land is <c>height ≥ <see cref="SeaLevel"/></c>. Only
        /// water reachable from the window edge is treated as ocean (the lake exclusion). The signed
        /// distance is computed by a multi-source BFS from every shoreline texel and clamped to the band
        /// (texels beyond the band carry ±band, alpha 0 there — no need for an exact far distance).
        /// </summary>
        /// <param name="height">Terrain-height field; <c>Sample(x,z)</c> in world metres.</param>
        /// <param name="originX">World-X of the min corner.</param>
        /// <param name="originZ">World-Z of the min corner.</param>
        /// <param name="cell">Texel size (m). Smaller = smoother fade, more texels.</param>
        /// <param name="width">Texel columns.</param>
        /// <param name="height_">Texel rows.</param>
        /// <param name="bandMeters">Fade reach from the shoreline (m). Distances are clamped here.</param>
        /// <param name="depthFadeMeters">When &gt; 0, the SEAWARD glow additionally fades to 0 over water
        ///   deeper than this many metres below sea level — so the glow hugs the coast and cannot haze
        ///   the open sea (validated fix, docs/design/region-atlas-render.md). 0 disables depth-gating
        ///   (byte-identical to the prior behaviour). Requires <paramref name="height"/> to sample depth.</param>
        /// <param name="regionIdAt">Optional: world (x,z) → owned-land region id (&lt; 0 = unowned). When
        ///   supplied, every water texel within the band records the region id of the NEAREST owned-land
        ///   coast, so a consumer can colour the seaward glow per region (Atlas biome glow). Null leaves
        ///   <see cref="NearestRegionIdAt"/> returning −1 everywhere (single-colour glow, prior behaviour).</param>
        public static CoastHaloField Build(
            IScalarField height,
            double originX, double originZ, double cell,
            int width, int height_,
            double bandMeters = DefaultBandMeters,
            double depthFadeMeters = 0.0,
            Func<double, double, int> regionIdAt = null,
            double costFloodDeepWeight = 0.0)
        {
            if (height == null) throw new ArgumentNullException(nameof(height));
            if (cell <= 0) throw new ArgumentOutOfRangeException(nameof(cell));
            if (width <= 0 || height_ <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (bandMeters <= 0) throw new ArgumentOutOfRangeException(nameof(bandMeters));

            int h = height_, w = width;
            var land = new bool[h, w];
            var depthBelow = new float[h, w];   // metres below sea level per texel (0 on land)
            var ownedLand = new int[h, w];      // region id at a land texel, −1 if unowned / no sampler
            for (int gy = 0; gy < h; gy++)
            {
                double wz = originZ + (gy + 0.5) * cell;
                for (int gx = 0; gx < w; gx++)
                {
                    double wx = originX + (gx + 0.5) * cell;
                    double hm = height.Sample(wx, wz);
                    bool isLand = hm >= SeaLevel;
                    land[gy, gx] = isLand;
                    depthBelow[gy, gx] = (float)Math.Max(0.0, SeaLevel - hm);
                    ownedLand[gy, gx] = (isLand && regionIdAt != null) ? regionIdAt(wx, wz) : -1;
                }
            }

            bool[,] ocean = FloodOceanFromEdge(land, h, w);
            double[,] signed = SignedShorelineDistance(land, ocean, h, w, cell, bandMeters);
            // C-cost apron (2026-06-28): when costFloodDeepWeight>0, the SEAWARD reach is cost-flooded —
            // each metre offshore costs ×(1 + depth/deepWeight), so the apron sprawls over shallow
            // shelves/archipelago and retracts at deep drop-offs (terrain-shaped extent, not a fixed
            // buffer). Land/inland side keeps raw distance. deepWeight=0 = legacy fixed-band (unchanged).
            if (costFloodDeepWeight > 0.0)
                signed = ApplySeawardCost(signed, ocean, depthBelow, h, w, cell, bandMeters, costFloodDeepWeight);

            // Per-texel seaward depth-gate ∈ [0,1]: 1 at the shoreline, fading to 0 by depthFadeMeters.
            // When depthFadeMeters ≤ 0, gating is OFF → all 1 → no behavioural change.
            var gate = new float[h, w];
            for (int gy = 0; gy < h; gy++)
                for (int gx = 0; gx < w; gx++)
                {
                    if (depthFadeMeters <= 0.0) { gate[gy, gx] = 1f; continue; }
                    float g = (float)(1.0 - depthBelow[gy, gx] / depthFadeMeters);
                    gate[gy, gx] = g <= 0f ? 0f : g >= 1f ? 1f : g;
                }

            // Per-texel nearest owned-land region id, propagated across water by multi-source BFS from
            // every owned-land coast texel (the same hop walk the distance transform uses). A water
            // texel within the band ends up tagged with the region whose coast is nearest — so the
            // seaward glow can be coloured per region (Atlas biome glow). −1 everywhere when no
            // regionIdAt sampler was supplied (single-colour glow, prior behaviour preserved).
            var nearestRegion = BuildNearestRegion(land, ocean, ownedLand, h, w, cell, bandMeters, regionIdAt != null);

            return new CoastHaloField(signed, ocean, gate, nearestRegion, cell, originX, originZ, bandMeters);
        }

        /// <summary>
        /// Propagate the nearest owned-land region id outward across water within the band. Multi-source
        /// BFS seeded at owned-land texels that border ocean (a coast with a known region); each water
        /// texel takes the region of the first (nearest) seed to reach it. Returns all −1 when
        /// <paramref name="enabled"/> is false (no region sampler).
        /// </summary>
        private static int[,] BuildNearestRegion(
            bool[,] land, bool[,] ocean, int[,] ownedLand, int h, int w, double cell, double band, bool enabled)
        {
            var region = new int[h, w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) region[y, x] = -1;
            if (!enabled) return region;

            var dist = new double[h, w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) dist[y, x] = double.MaxValue;
            var queue = new Queue<(int, int)>();

            // Seed: an owned-land texel (region ≥ 0) that is 4-adjacent to ocean — a coast with identity.
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (!land[y, x] || ownedLand[y, x] < 0) continue;
                    bool bordersOcean = false;
                    for (int d = 0; d < 4 && !bordersOcean; d++)
                    {
                        int ny = y + (d == 0 ? -1 : d == 1 ? 1 : 0);
                        int nx = x + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                        if (ocean[ny, nx]) bordersOcean = true;
                    }
                    if (bordersOcean) { region[y, x] = ownedLand[y, x]; dist[y, x] = 0.0; queue.Enqueue((y, x)); }
                }

            // BFS outward across water up to the band (land texels are not overwritten; they keep their own id).
            while (queue.Count > 0)
            {
                var (y, x) = queue.Dequeue();
                double dnext = dist[y, x] + cell;
                if (dnext > band + cell) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ny = y + (d == 0 ? -1 : d == 1 ? 1 : 0);
                    int nx = x + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                    if (land[ny, nx]) continue;               // only propagate across water
                    if (dist[ny, nx] <= dnext) continue;
                    dist[ny, nx] = dnext;
                    region[ny, nx] = region[y, x];
                    queue.Enqueue((ny, nx));
                }
            }
            return region;
        }

        /// <summary>
        /// Ocean = water (not land) reachable from the window edge by 4-connectivity. Enclosed water
        /// (a lake) is unreachable from the edge and stays false, so it grows no halo.
        /// </summary>
        private static bool[,] FloodOceanFromEdge(bool[,] land, int h, int w)
        {
            var ocean = new bool[h, w];
            var queue = new Queue<(int, int)>();

            void Seed(int y, int x)
            {
                if (y < 0 || y >= h || x < 0 || x >= w) return;
                if (land[y, x] || ocean[y, x]) return;
                ocean[y, x] = true;
                queue.Enqueue((y, x));
            }

            for (int x = 0; x < w; x++) { Seed(0, x); Seed(h - 1, x); }
            for (int y = 0; y < h; y++) { Seed(y, 0); Seed(y, w - 1); }

            while (queue.Count > 0)
            {
                var (y, x) = queue.Dequeue();
                Seed(y - 1, x); Seed(y + 1, x); Seed(y, x - 1); Seed(y, x + 1);
            }
            return ocean;
        }

        /// <summary>
        /// Signed distance (m) to the nearest OCEAN shoreline, via multi-source BFS seeded at every
        /// shoreline texel (a land texel 4-adjacent to ocean, or an ocean texel 4-adjacent to land).
        /// BFS hop count × cell approximates Euclidean distance closely enough for a soft fade and is
        /// O(texels). Positive on land, negative on ocean. Lake water (not ocean) is NOT a shore source,
        /// so a lake shore grows no fade; land near only a lake reads the far clamp (+band) → alpha 0.
        /// Clamped to ±band: anything past the band is set to ±band exactly (alpha 0 there anyway).
        /// </summary>
        private static double[,] SignedShorelineDistance(
            bool[,] land, bool[,] ocean, int h, int w, double cell, double band)
        {
            const double INF = double.MaxValue;
            var dist = new double[h, w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) dist[y, x] = INF;

            var queue = new Queue<(int, int)>();

            // Seed: a texel is ON the shore if it borders the OTHER class across the ocean/land line.
            // Ocean-vs-land only (lake water is excluded as a source).
            bool IsOcean(int y, int x) => ocean[y, x];
            bool IsLand(int y, int x) => land[y, x];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool me = IsLand(y, x);
                    bool other = IsOcean(y, x);
                    if (!me && !other) continue; // lake water: not a shoreline source
                    bool border = false;
                    for (int d = 0; d < 4 && !border; d++)
                    {
                        int ny = y + (d == 0 ? -1 : d == 1 ? 1 : 0);
                        int nx = x + (d == 2 ? -1 : d == 3 ? 1 : 0);
                        if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                        if (me && IsOcean(ny, nx)) border = true;
                        else if (other && IsLand(ny, nx)) border = true;
                    }
                    if (border) { dist[y, x] = 0.0; queue.Enqueue((y, x)); }
                }
            }

            // BFS outward; stop expanding once past the band (those texels keep INF → clamped below).
            int maxHops = (int)Math.Ceiling(band / cell) + 1;
            while (queue.Count > 0)
            {
                var (y, x) = queue.Dequeue();
                double dnext = dist[y, x] + cell;
                if (dnext > band + cell) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ny = y + (d == 0 ? -1 : d == 1 ? 1 : 0);
                    int nx = x + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                    if (dist[ny, nx] <= dnext) continue;
                    dist[ny, nx] = dnext;
                    queue.Enqueue((ny, nx));
                }
            }

            // Apply sign + clamp. Land = +, ocean = −. Lake water and unreached land clamp to +band
            // (so the inland fade doesn't bleed off a lake, and far land/ocean read alpha 0).
            var signed = new double[h, w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    double dm = dist[y, x];
                    if (dm == INF) dm = band;
                    if (dm > band) dm = band;
                    bool waterSide = ocean[y, x];
                    signed[y, x] = waterSide ? -dm : dm;
                }
            }
            return signed;
        }

        /// <summary>
        /// C-cost apron: replace the SEAWARD half of <paramref name="signed"/> with a depth-weighted cost
        /// distance, leaving land (≥0) untouched. Multi-source BFS from ocean texels adjacent to land; each
        /// step's effective cost = cell·(1 + depth/deepWeight), so deep water burns the band budget fast
        /// (apron retracts) while shallow shelves stay cheap (apron sprawls across archipelago). Clamped to
        /// ±band exactly like the distance field, so Alpha()'s fade math is unchanged — only the reach
        /// shape differs. Pure, deterministic, no Unity. deepWeight is the depth (m) that doubles per-step
        /// cost; smaller = harsher deep-water penalty.
        /// </summary>
        private static double[,] ApplySeawardCost(double[,] signed, bool[,] ocean, float[,] depthBelow,
                                                  int h, int w, double cell, double band, double deepWeight)
        {
            var cost = new double[h, w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) cost[y, x] = double.MaxValue;
            var pq = new Queue<(int, int)>();
            // Seed: ocean texels touching land start at cost 0 (the shoreline).
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                if (!ocean[y, x]) continue;
                bool coast = false;
                for (int d = 0; d < 4 && !coast; d++)
                {
                    int ny = y + (d == 0 ? -1 : d == 1 ? 1 : 0), nx = x + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (ny >= 0 && ny < h && nx >= 0 && nx < w && !ocean[ny, nx] && depthBelow[ny, nx] <= 0f) coast = true;
                }
                if (coast) { cost[y, x] = 0.0; pq.Enqueue((y, x)); }
            }
            // Dijkstra-ish flood (uniform-ish edge cost → BFS w/ relaxation is adequate at this resolution).
            while (pq.Count > 0)
            {
                var (y, x) = pq.Dequeue();
                double depth = depthBelow[y, x];
                double step = cell * (1.0 + Math.Max(0.0, depth) / deepWeight);
                double nc = cost[y, x] + step;
                if (nc > band) continue;
                for (int d = 0; d < 4; d++)
                {
                    int ny = y + (d == 0 ? -1 : d == 1 ? 1 : 0), nx = x + (d == 2 ? -1 : d == 3 ? 1 : 0);
                    if (ny < 0 || ny >= h || nx < 0 || nx >= w || !ocean[ny, nx] || cost[ny, nx] <= nc) continue;
                    cost[ny, nx] = nc; pq.Enqueue((ny, nx));
                }
            }
            // Overwrite water side with cost-distance (clamped to band); land side untouched.
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (ocean[y, x]) { double c = cost[y, x] > band ? band : cost[y, x]; signed[y, x] = -c; }
            return signed;
        }

        /// <summary>
        /// The fade alpha [0,1] for a texel under a mode. <see cref="CoastHaloMode.Seaward"/> fades on
        /// the water side (signed &lt; 0), <see cref="CoastHaloMode.Inland"/> on the land side
        /// (signed ≥ 0); both peak at the shore (|signed| = 0) and reach 0 at the band edge. A small
        /// land lip is added to the seaward mode so the glow visually attaches to the coast rather than
        /// floating just offshore. <see cref="CoastHaloMode.Off"/> is always 0.
        /// </summary>
        public double Alpha(CoastHaloMode mode, int gy, int gx)
        {
            if (mode == CoastHaloMode.Off) return 0.0;
            double d = this.signedDistance[gy, gx];
            double band = this.BandMeters;

            if (mode == CoastHaloMode.Seaward)
            {
                double a;
                if (d <= 0.0 && d >= -band) a = 1.0 - (-d / band);   // water side, peak at shore
                else if (d > 0.0 && d < this.Cell * 0.5) a = 1.0;     // thin land lip = attached
                else return 0.0;
                // Depth-gate the seaward fade so it hugs the coast and dies over deep open water
                // (1.0 everywhere when the field was built with depthFadeMeters ≤ 0).
                return a * this.depthGate[gy, gx];
            }
            // Inland
            if (d >= 0.0 && d <= band) return 1.0 - (d / band);          // land side, peak at shore
            return 0.0;
        }

        /// <summary>Signed shoreline distance (m) at a texel (+land / −ocean), clamped to ±band.</summary>
        public double SignedDistanceAt(int gy, int gx) => this.signedDistance[gy, gx];

        /// <summary>True if the texel is ocean (edge-connected water), false for land or lake.</summary>
        public bool IsOceanAt(int gy, int gx) => this.isOceanSide[gy, gx];

        /// <summary>The nearest owned-land region id whose coast this texel's seaward glow belongs to,
        /// or −1 when out of band / no region sampler was supplied at build. A consumer colours the
        /// glow by this region's biome (Atlas) instead of a single halo colour.</summary>
        public int NearestRegionIdAt(int gy, int gx) => this.nearestRegionId[gy, gx];
    }
}
