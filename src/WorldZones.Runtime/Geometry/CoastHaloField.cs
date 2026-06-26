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

        private CoastHaloField(double[,] signed, bool[,] oceanSide, double cell,
                               double originX, double originZ, double band)
        {
            this.signedDistance = signed;
            this.isOceanSide = oceanSide;
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
        public static CoastHaloField Build(
            IScalarField height,
            double originX, double originZ, double cell,
            int width, int height_,
            double bandMeters = DefaultBandMeters)
        {
            if (height == null) throw new ArgumentNullException(nameof(height));
            if (cell <= 0) throw new ArgumentOutOfRangeException(nameof(cell));
            if (width <= 0 || height_ <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (bandMeters <= 0) throw new ArgumentOutOfRangeException(nameof(bandMeters));

            int h = height_, w = width;
            var land = new bool[h, w];
            for (int gy = 0; gy < h; gy++)
            {
                double wz = originZ + (gy + 0.5) * cell;
                for (int gx = 0; gx < w; gx++)
                {
                    double wx = originX + (gx + 0.5) * cell;
                    land[gy, gx] = height.Sample(wx, wz) >= SeaLevel;
                }
            }

            bool[,] ocean = FloodOceanFromEdge(land, h, w);
            double[,] signed = SignedShorelineDistance(land, ocean, h, w, cell, bandMeters);
            return new CoastHaloField(signed, ocean, cell, originX, originZ, bandMeters);
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
                if (d <= 0.0 && d >= -band) return 1.0 - (-d / band);   // water side, peak at shore
                if (d > 0.0 && d < this.Cell * 0.5) return 1.0;          // thin land lip = attached
                return 0.0;
            }
            // Inland
            if (d >= 0.0 && d <= band) return 1.0 - (d / band);          // land side, peak at shore
            return 0.0;
        }

        /// <summary>Signed shoreline distance (m) at a texel (+land / −ocean), clamped to ±band.</summary>
        public double SignedDistanceAt(int gy, int gx) => this.signedDistance[gy, gx];

        /// <summary>True if the texel is ocean (edge-connected water), false for land or lake.</summary>
        public bool IsOceanAt(int gy, int gx) => this.isOceanSide[gy, gx];
    }
}
