using System;
using WorldZones.Runtime.Geometry;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards <see cref="CoastHaloField"/> — the soft coast-fade fill. Uses SYNTHETIC height fields
    /// (a half-plane sea, an enclosed lake) so the invariants are exact and the tests are fast (no
    /// real-Niflheim build). The load-bearing guarantees: (1) lake water grows NO halo (only
    /// edge-connected ocean does), (2) the signed distance is + on land / − on ocean and peaks at the
    /// shore, (3) the two modes fade on opposite sides, (4) the band clamps the fade reach.
    /// </summary>
    public class CoastHaloFieldTests
    {
        /// <summary>A height field that is a vertical coastline: water (height 10) for worldX &lt; 0,
        /// land (height 40) for worldX ≥ 0. Sea level is 30, so the shore is the x=0 line.</summary>
        private sealed class HalfPlaneSea : IScalarField
        {
            public double IsoLevel => CoastHaloField.SeaLevel;
            public double Sample(double worldX, double worldZ) => worldX < 0 ? 10.0 : 40.0;
        }

        /// <summary>Land everywhere (height 40) EXCEPT a square enclosed pond (height 10) well inside
        /// the window — a LAKE, not connected to any window edge.</summary>
        private sealed class EnclosedLake : IScalarField
        {
            public double IsoLevel => CoastHaloField.SeaLevel;
            public double Sample(double worldX, double worldZ)
            {
                // pond occupies world [200,400] × [200,400]; everything else is land.
                bool inPond = worldX >= 200 && worldX <= 400 && worldZ >= 200 && worldZ <= 400;
                return inPond ? 10.0 : 40.0;
            }
        }

        // A 600×600 m window at 8 m texels, origin at the world origin.
        private const double Origin = 0.0, Cell = 8.0;
        private const int N = 75; // 600 / 8

        [Fact]
        public void HalfPlaneSea_LandIsPositive_OceanIsNegative_PeaksAtShore()
        {
            var f = CoastHaloField.Build(new HalfPlaneSea(), -300, -300, Cell, N, N, bandMeters: 96);

            // A texel deep on the land side (worldX large +) → positive, clamped to band.
            int gxLandDeep = (int)((250 - (-300)) / Cell);
            int gyMid = N / 2;
            Assert.True(f.SignedDistanceAt(gyMid, gxLandDeep) > 0, "deep land must be positive");

            // A texel deep on the water side (worldX large −) → negative.
            int gxSeaDeep = (int)((-250 - (-300)) / Cell);
            Assert.True(f.SignedDistanceAt(gyMid, gxSeaDeep) < 0, "deep water must be negative");

            // Near the shore (worldX ≈ 0) the magnitude is small (≈ 0 at the seam).
            int gxShore = (int)((0 - (-300)) / Cell);
            Assert.True(Math.Abs(f.SignedDistanceAt(gyMid, gxShore)) <= Cell * 1.5,
                "the shoreline texel distance must be ~0");
        }

        [Fact]
        public void SeawardMode_FadesOnWaterSide_InlandMode_FadesOnLandSide()
        {
            var f = CoastHaloField.Build(new HalfPlaneSea(), -300, -300, Cell, N, N, bandMeters: 96);
            int gyMid = N / 2;
            int gxWater = (int)((-40 - (-300)) / Cell);  // 40 m into the water
            int gxLand = (int)((40 - (-300)) / Cell);     // 40 m into the land

            // Seaward: alpha on the water side > 0, on the deep land side ~0.
            Assert.True(f.Alpha(CoastHaloMode.Seaward, gyMid, gxWater) > 0.2, "seaward fades into water");
            Assert.True(f.Alpha(CoastHaloMode.Seaward, gyMid, gxLand) < 0.2, "seaward is ~off on land");

            // Inland: the mirror image.
            Assert.True(f.Alpha(CoastHaloMode.Inland, gyMid, gxLand) > 0.2, "inland fades onto land");
            Assert.True(f.Alpha(CoastHaloMode.Inland, gyMid, gxWater) <= 0.0, "inland is off in water");
        }

        [Fact]
        public void OffMode_IsAlwaysZero()
        {
            var f = CoastHaloField.Build(new HalfPlaneSea(), -300, -300, Cell, N, N);
            for (int gy = 0; gy < f.Height; gy += 11)
                for (int gx = 0; gx < f.Width; gx += 11)
                    Assert.Equal(0.0, f.Alpha(CoastHaloMode.Off, gy, gx));
        }

        [Fact]
        public void Alpha_PeaksAtShore_AndDecaysToZeroAtBand()
        {
            double band = 96;
            var f = CoastHaloField.Build(new HalfPlaneSea(), -300, -300, Cell, N, N, bandMeters: band);
            int gyMid = N / 2;

            // Walk from the shore into the water; seaward alpha must be (weakly) monotone DOWN.
            double prev = 2.0;
            for (double m = 0; m <= band; m += Cell)
            {
                int gx = (int)((-m - (-300)) / Cell);
                if (gx < 0 || gx >= f.Width) continue;
                double a = f.Alpha(CoastHaloMode.Seaward, gyMid, gx);
                Assert.True(a <= prev + 1e-9, $"seaward alpha must not increase moving out to sea (at {m} m)");
                prev = a;
            }
            // Past the band, alpha is 0.
            int gxPast = (int)((-(band + 32) - (-300)) / Cell);
            if (gxPast >= 0)
                Assert.Equal(0.0, f.Alpha(CoastHaloMode.Seaward, gyMid, gxPast));
        }

        [Fact]
        public void EnclosedLake_GrowsNoHalo_OnlyEdgeOceanDoes()
        {
            var f = CoastHaloField.Build(new EnclosedLake(), 0, 0, Cell, N, N, bandMeters: 96);

            // A texel just OUTSIDE the lake (land at worldX≈150, worldZ≈300) must NOT carry a halo:
            // the lake is excluded as a shoreline source, so inland alpha there is 0 (its nearest
            // ocean is none → clamped to +band → alpha 0).
            int gxNearLake = (int)((150 - 0) / Cell);
            int gzMidLake = (int)((300 - 0) / Cell);
            Assert.Equal(0.0, f.Alpha(CoastHaloMode.Inland, gzMidLake, gxNearLake));
            Assert.Equal(0.0, f.Alpha(CoastHaloMode.Seaward, gzMidLake, gxNearLake));

            // And the lake water itself is NOT ocean.
            int gxInLake = (int)((300 - 0) / Cell);
            Assert.False(f.IsOceanAt(gzMidLake, gxInLake), "enclosed pond must be a lake, not ocean");
        }

        [Fact]
        public void Build_RejectsBadArguments()
        {
            Assert.Throws<ArgumentNullException>(() =>
                CoastHaloField.Build(null, 0, 0, Cell, N, N));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CoastHaloField.Build(new HalfPlaneSea(), 0, 0, 0, N, N));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CoastHaloField.Build(new HalfPlaneSea(), 0, 0, Cell, 0, N));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CoastHaloField.Build(new HalfPlaneSea(), 0, 0, Cell, N, N, bandMeters: 0));
        }
    }
}
