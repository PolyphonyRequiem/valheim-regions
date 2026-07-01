using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards the Tier-1 render-seam geometry (<see cref="RegionBoundaryExtractor"/> +
    /// <see cref="RegionBoundaryGraph"/>) against the REAL Niflheim world (seed ForTheWort), run
    /// through the production façade. These are the headless regression net for the boundary math —
    /// the layer that can silently break (cf. the deleted-oracle anxiety in ROADMAP Part 2 #1).
    /// See docs/design/region-render-seam.md.
    /// </summary>
    public class RegionBoundaryGeometryTests
    {
        private const string NiflheimSeed = "ForTheWort";

        private static RegionWorld BuildNiflheim() =>
            WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions { IncludeInlandWater = true });

        private static RegionBoundaryGraph Graph() => BuildNiflheim().BuildBoundaryGraph();

        [Fact]
        public void Graph_IsNonEmpty_AndEverySegmentLandsOnTheZoneEdgeLattice()
        {
            var g = Graph();
            Assert.NotEmpty(g.Segments);
            Assert.NotEmpty(g.Rings);

            // Every endpoint sits on the 64·n+32 corner lattice (world = c·64 − 32 for integer c).
            foreach (var s in g.Segments.Take(5000))
            {
                AssertOnLattice(s.A);
                AssertOnLattice(s.B);
                // A seam is exactly one zone edge: 64 m long.
                Assert.True(Math.Abs(s.Length - ZoneGrid.ZoneSize) < 1e-6,
                    $"segment length {s.Length} != 64 ({s.KeyA}|{s.KeyB})");
            }
        }

        private static void AssertOnLattice(WzVec2 p)
        {
            // world = c*64 - 32  ⟺  (world + 32) / 64 is an integer.
            double cx = (p.X + 32.0) / 64.0;
            double cz = (p.Z + 32.0) / 64.0;
            Assert.True(Math.Abs(cx - Math.Round(cx)) < 1e-6, $"X {p.X} off lattice");
            Assert.True(Math.Abs(cz - Math.Round(cz)) < 1e-6, $"Z {p.Z} off lattice");
        }

        [Fact]
        public void EverySegment_HasARealKeyA_AndCanonicalOrdering()
        {
            var g = Graph();
            var realKeys = new HashSet<string>(BuildNiflheim().Regions.Select(r => r.RegionKey), StringComparer.Ordinal);

            foreach (var s in g.Segments)
            {
                // KeyA is always a real region (a seam always has a region on at least one side).
                Assert.False(string.IsNullOrEmpty(s.KeyA));
                Assert.Contains(s.KeyA, realKeys);

                if (s.KeyB == null)
                {
                    Assert.True(s.IsCoastline);
                }
                else
                {
                    // Both real → canonical ordering KeyA ≤ KeyB, and never a self-seam.
                    Assert.Contains(s.KeyB, realKeys);
                    Assert.True(string.CompareOrdinal(s.KeyA, s.KeyB) <= 0, "keys not canonically ordered");
                    Assert.NotEqual(s.KeyA, s.KeyB);
                }
            }
        }

        [Fact]
        public void Segments_AreDeduplicated_DrawnExactlyOnce()
        {
            var g = Graph();
            // Two distinct segments must never occupy the same undirected edge: the stroke-once promise.
            var seen = new HashSet<(double, double, double, double)>();
            foreach (var s in g.Segments)
            {
                // canonical endpoint order so A→B and B→A collapse
                var key = s.A.X < s.B.X || (s.A.X == s.B.X && s.A.Z <= s.B.Z)
                    ? (s.A.X, s.A.Z, s.B.X, s.B.Z)
                    : (s.B.X, s.B.Z, s.A.X, s.A.Z);
                Assert.True(seen.Add(key), $"duplicate seam at {s.A}-{s.B} ({s.KeyA}|{s.KeyB})");
            }
        }

        [Fact]
        public void EveryRegion_HasExactlyOneOuterRing()
        {
            var world = BuildNiflheim();
            var g = world.BuildBoundaryGraph();

            foreach (var r in world.Regions)
            {
                var rings = g.RingsFor(r.RegionKey);
                int outerCount = rings.Count(x => !x.IsHole);
                // A contiguous proto-region (single BFS component) has exactly one outer boundary.
                Assert.True(outerCount == 1,
                    $"region {r.RegionKey} ({r.Name}) has {outerCount} outer rings (expected 1)");
                Assert.NotNull(g.OuterRing(r.RegionKey));
            }
        }

        [Fact]
        public void OuterRings_AreCCW_HolesAreCW()
        {
            var g = Graph();
            foreach (var ring in g.Rings)
            {
                if (ring.IsHole)
                    Assert.True(ring.SignedArea < 0, $"hole ring {ring.RegionKey} not CW");
                else
                    Assert.True(ring.SignedArea > 0, $"outer ring {ring.RegionKey} not CCW");
            }
        }

        [Fact]
        public void OuterRingArea_Approximates_LandFootprint()
        {
            // The outer ring encloses land + any inland-water/hole area it wraps, so ring area should
            // be >= the region's official land area and within a sane multiple of total territory.
            var world = BuildNiflheim();
            var g = world.BuildBoundaryGraph();
            const double zoneM2 = ZoneGrid.ZoneSize * (double)ZoneGrid.ZoneSize;

            foreach (var r in world.Regions)
            {
                RegionRing outer = g.OuterRing(r.RegionKey);
                if (outer == null) continue;

                // net enclosed area = outer + holes (holes are signed negative)
                double net = g.RingsFor(r.RegionKey).Sum(x => x.SignedArea);
                double landM2 = r.LandZones * zoneM2;

                Assert.True(net > 0, $"region {r.RegionKey} net ring area not positive");
                // Net (land + attributed inland water, since the id-grid tags both) is at least land,
                // minus a one-zone-fringe tolerance.
                Assert.True(net >= landM2 - zoneM2,
                    $"region {r.RegionKey} net area {net:F0} < land {landM2:F0}");
            }
        }

        [Fact]
        public void PointInsideOuterRing_ResolvesToSameRegion_TheIndependentOracle()
        {
            // THE cross-check: the boundary geometry and the point-query lookup are computed by
            // DIFFERENT code paths. A point well inside a region's outer ring must resolve to that
            // same region via RegionAt. If the extractor mis-keyed a seam, these disagree.
            var world = BuildNiflheim();
            var g = world.BuildBoundaryGraph();

            int probed = 0, agree = 0;
            foreach (var r in world.Regions.OrderByDescending(x => x.LandZones).Take(40))
            {
                RegionRing outer = g.OuterRing(r.RegionKey);
                if (outer == null) continue;

                // Use the region centroid; nudge to a guaranteed-interior lattice cell centre if the
                // centroid happens to land in a hole/concavity by sampling the densest interior point.
                WzVec2 probe = InteriorPoint(outer, world, r.RegionKey);
                probed++;

                RegionInfo hit = world.RegionAt(probe.X, probe.Z);
                if (hit != null && string.Equals(hit.RegionKey, r.RegionKey, StringComparison.Ordinal))
                    agree++;
            }

            Assert.True(probed > 0, "no regions probed");
            // Allow a small slack for centroid-in-concavity cases; the geometry is right if the vast
            // majority agree (a mis-keyed extractor would tank this wholesale).
            double rate = (double)agree / probed;
            Assert.True(rate >= 0.9, $"interior→region agreement only {rate:P0} ({agree}/{probed})");
        }

        // Find an interior point: scan the region's zones for one whose 4-neighbourhood is all the
        // same region (a firm interior cell), return its centre. Falls back to the centroid.
        private static WzVec2 InteriorPoint(RegionRing outer, RegionWorld world, string regionKey)
        {
            ZoneGrid grid = world.Grid;
            int min = grid.MinIndex;
            int[,] idGrid = world.RegionIdGrid;
            int size = grid.Size;

            int targetId = world.Regions.First(r => r.RegionKey == regionKey).TransientId;

            for (int gy = 1; gy < size - 1; gy++)
            for (int gx = 1; gx < size - 1; gx++)
            {
                if (idGrid[gy, gx] != targetId) continue;
                if (idGrid[gy, gx - 1] == targetId && idGrid[gy, gx + 1] == targetId &&
                    idGrid[gy - 1, gx] == targetId && idGrid[gy + 1, gx] == targetId)
                {
                    int zx = gx + min, zy = gy + min;
                    return new WzVec2(zx * (double)ZoneGrid.ZoneSize, zy * (double)ZoneGrid.ZoneSize);
                }
            }
            // No firm interior cell (thin region) — fall back to centroid.
            var info = world.GetByKey(regionKey);
            return new WzVec2(info.CentroidX, info.CentroidZ);
        }
    }

    /// <summary>
    /// Guards the contour-hug refiner (<see cref="RegionBoundaryRefiner"/>) — the sub-64 m detail
    /// layer. Synthetic-field tests prove the marching math headlessly; a real-Niflheim test proves
    /// coastlines snap toward sea level. The coarse graph is unchanged (additive layer).
    /// See docs/design/region-render-seam.md.
    /// </summary>
    public class RegionBoundaryRefinerTests
    {
        private const string NiflheimSeed = "ForTheWort";

        // A synthetic height field: height = X (metres). Sea level iso = 30 → the shoreline is the
        // vertical world line X=30, regardless of Z. Any coastline sample should snap to X≈30.
        private sealed class RampField : IScalarField
        {
            public double IsoLevel => 30.0;
            public double Sample(double worldX, double worldZ) => worldX;
        }

        [Fact]
        public void RefineSegment_SnapsSamplePointsOntoTheSyntheticIsoline()
        {
            // A segment near the X=30 shoreline, offset so refinement must displace it onto X=30.
            var seg = new BorderSegment(new WzVec2(50, 0), new WzVec2(50, 64), "r.0.0", null);
            var field = new RampField();
            var opt = new SegmentRefineOptions { Subdivisions = 4, MaxDisplacement = 40, MarchStep = 2 };

            RefinedBorder refined = RegionBoundaryRefiner.RefineSegment(seg, field, opt);

            Assert.True(refined.Hugged, "segment within reach of the isoline should hug it");
            foreach (var p in refined.Polyline)
                Assert.True(Math.Abs(p.X - 30.0) < 0.5, $"point X={p.X:F2} did not snap to the X=30 shoreline");
        }

        [Fact]
        public void RefineSegment_LeavesPointOnLattice_WhenNoIsolineInReach()
        {
            // Shoreline X=30 is 200 m away — beyond MaxDisplacement → honest no-hug, points stay put.
            var seg = new BorderSegment(new WzVec2(230, 0), new WzVec2(230, 64), "r.0.0", null);
            var field = new RampField();
            var opt = new SegmentRefineOptions { Subdivisions = 4, MaxDisplacement = 40, MarchStep = 4 };

            RefinedBorder refined = RegionBoundaryRefiner.RefineSegment(seg, field, opt);

            Assert.False(refined.Hugged);
            foreach (var p in refined.Polyline)
                Assert.True(Math.Abs(p.X - 230.0) < 1e-6, "no isoline in reach → point must stay on the lattice");
        }

        [Fact]
        public void RefineSegment_PreservesKeysAndEndpointsCount()
        {
            var seg = new BorderSegment(new WzVec2(50, 0), new WzVec2(50, 64), "r.1.2", "r.3.4");
            var refined = RegionBoundaryRefiner.RefineSegment(seg, new RampField(),
                new SegmentRefineOptions { Subdivisions = 4 });
            Assert.Equal("r.1.2", refined.KeyA);
            Assert.Equal("r.3.4", refined.KeyB);
            Assert.Equal(5, refined.Polyline.Count); // Subdivisions+1
        }

        [Fact]
        public void RefineCoastlines_OnRealNiflheim_PullsSamplesTowardSeaLevel()
        {
            RegionWorld world = WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions { IncludeInlandWater = true });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var field = new HeightScalarField(PortWorldSampler.FromSeed(NiflheimSeed));

            IReadOnlyList<RefinedBorder> coasts = RegionBoundaryRefiner.RefineCoastlines(graph, field);
            Assert.NotEmpty(coasts);

            // Every refined coast carries a coastline key pair (KeyB null) and a real polyline.
            foreach (var c in coasts)
            {
                Assert.False(string.IsNullOrEmpty(c.KeyA));
                Assert.Null(c.KeyB);
                Assert.True(c.Polyline.Count >= 2);
            }

            // The refinement should move the MAJORITY of coast points onto the field's iso-level (the
            // coast contour). Measure against the field's OWN IsoLevel — not a hardcoded constant — so
            // the test follows the configured coast depth (default = the 25 m shelf midpoint).
            int hugged = 0; double sumAbs = 0; int n = 0;
            foreach (var c in coasts)
            {
                if (!c.Hugged) continue;
                hugged++;
                foreach (var p in c.Polyline)
                {
                    double h = field.Sample(p.X, p.Z);
                    sumAbs += Math.Abs(h - field.IsoLevel);
                    n++;
                }
            }
            Assert.True(hugged > 0, "expected some coastlines to hug the shoreline");
            double meanAbs = sumAbs / n;
            // Hugged points should sit within a few metres of the coast iso (vs. the ~tens-of-metres a
            // zone-center staircase lands at). Generous bound — this is a "did it actually snap" gate.
            Assert.True(meanAbs < 5.0, $"hugged coast points mean |height-iso| = {meanAbs:F2} m (expected < 5)");
        }

        [Fact]
        public void Despike_PullsBackAnOutlierPoint_PreservesEndpoints()
        {
            // A clean line with one point spiked +30m off to the side.
            var pts = new[]
            {
                new WzVec2(0, 0), new WzVec2(10, 0), new WzVec2(20, 30), // <- spike
                new WzVec2(30, 0), new WzVec2(40, 0)
            };
            var outp = PolylineSmoother.Despike(pts, maxDeviation: 24.0);
            Assert.Equal(5, outp.Count);
            // Endpoints unchanged.
            Assert.Equal(0.0, outp[0].X, 6); Assert.Equal(40.0, outp[4].X, 6);
            // The spiked middle point pulled back toward its neighbours' midpoint (z≈0, not 30).
            Assert.True(Math.Abs(outp[2].Z) < 1.0, $"spike not pulled back (z={outp[2].Z})");
        }

        [Fact]
        public void Chaikin_RoundsCorners_StaysInConvexHull()
        {
            // An L-shaped staircase corner. Chaikin must round it WITHOUT leaving the bounding box
            // [0,10]x[0,10] (hull-bounded — the property that keeps a smoothed coast on the coast).
            var pts = new[] { new WzVec2(0, 0), new WzVec2(10, 0), new WzVec2(10, 10) };
            var outp = PolylineSmoother.Chaikin(pts, iterations: 2);
            Assert.True(outp.Count > pts.Length, "Chaikin should add points");
            foreach (var p in outp)
            {
                Assert.InRange(p.X, 0.0, 10.0);
                Assert.InRange(p.Z, 0.0, 10.0);
            }
        }

        // ── SmoothGaussian: the terrain-scale (σ-in-metres) low-pass (region-boundary-negotiation.md) ──

        [Fact]
        public void SmoothGaussian_PinsEndpoints()
        {
            // A noisy zig-zag along +X. Whatever σ does in the middle, both endpoints must not move
            // (junctions where borders meet are pinned).
            var pts = MakeNoisyLine(count: 60, spacingM: 4.0, noiseAmpM: 6.0, seed: 1);
            var outp = PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 30.0);
            Assert.True(outp.Count >= 2);
            Assert.Equal(pts[0].X, outp[0].X, 6);
            Assert.Equal(pts[0].Z, outp[0].Z, 6);
            Assert.Equal(pts[^1].X, outp[^1].X, 6);
            Assert.Equal(pts[^1].Z, outp[^1].Z, 6);
        }

        [Fact]
        public void SmoothGaussian_SigmaZeroOrTiny_IsIdentity()
        {
            var pts = MakeNoisyLine(count: 20, spacingM: 4.0, noiseAmpM: 5.0, seed: 2);
            // σ ≤ 0 is the off-by-default legacy path: return the input reference unchanged.
            Assert.Same(pts, PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 0.0));
            Assert.Same(pts, PolylineSmoother.SmoothGaussian(pts, sigmaMeters: -5.0));
            // A degenerate <3-point polyline is returned as-is regardless of σ.
            var two = new[] { new WzVec2(0, 0), new WzVec2(10, 0) };
            Assert.Same(two, PolylineSmoother.SmoothGaussian(two, sigmaMeters: 30.0));
        }

        [Fact]
        public void SmoothGaussian_IsDeterministic()
        {
            var pts = MakeNoisyLine(count: 80, spacingM: 3.0, noiseAmpM: 7.0, seed: 3);
            var a = PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 30.0);
            var b = PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 30.0);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].X, b[i].X, 12);
                Assert.Equal(a[i].Z, b[i].Z, 12);
            }
        }

        [Fact]
        public void SmoothGaussian_ReducesNoise_AndLargerSigmaReducesMore()
        {
            // A straight baseline (z=0) corrupted with perpendicular noise. Smoothing must shrink the mean
            // |z| deviation, and a larger σ must shrink it more (the low-pass gets stronger with width).
            var pts = MakeNoisyLine(count: 120, spacingM: 3.0, noiseAmpM: 8.0, seed: 4);
            double rawDev = MeanAbsZ(pts);
            double dev10 = MeanAbsZ(PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 10.0));
            double dev30 = MeanAbsZ(PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 30.0));
            Assert.True(dev10 < rawDev, $"σ=10 should reduce noise ({dev10:F2} !< {rawDev:F2})");
            Assert.True(dev30 < dev10, $"σ=30 should smooth more than σ=10 ({dev30:F2} !< {dev10:F2})");
        }

        [Fact]
        public void SmoothGaussian_StaysNearTheCurve_DoesNotWanderOff()
        {
            // The smoothed curve must not invent a large excursion: every output point stays within a
            // bound of the original line's corridor. Here the true line is z=0 ± noiseAmp, so no smoothed
            // point should sit further than the noise amplitude off the axis (a low-pass can only pull IN
            // toward the mean, never overshoot beyond the input envelope by more than rounding).
            const double amp = 8.0;
            var pts = MakeNoisyLine(count: 150, spacingM: 3.0, noiseAmpM: amp, seed: 5);
            var outp = PolylineSmoother.SmoothGaussian(pts, sigmaMeters: 30.0);
            foreach (var p in outp)
                Assert.True(Math.Abs(p.Z) <= amp + 1e-6, $"smoothed point wandered to z={p.Z:F2} (envelope ±{amp})");
        }

        [Fact]
        public void ResampleUniform_ProducesEvenSpacing_PreservesEndpoints()
        {
            // Non-uniform input (a 5 m hop then a 40 m jump) resampled to 4 m steps: interior gaps ≈ 4 m,
            // endpoints preserved exactly.
            var pts = new[] { new WzVec2(0, 0), new WzVec2(5, 0), new WzVec2(45, 0) };
            var outp = PolylineSmoother.ResampleUniform(pts, step: 4.0);
            Assert.Equal(0.0, outp[0].X, 6);
            Assert.Equal(45.0, outp[^1].X, 6);
            Assert.True(outp.Count > pts.Length, "resampling a 45 m line at 4 m should add points");
            // Every consecutive interior gap (except the final remainder into the pinned endpoint) ≈ step.
            for (int i = 1; i < outp.Count - 1; i++)
            {
                double gap = Dist(outp[i - 1], outp[i]);
                Assert.InRange(gap, 3.0, 5.0);
            }
        }

        // ── SmoothGaussianClosed: the fill-RING (closed-loop) σ low-pass — fork A (shared-border-primitive.md).
        //    A fill ring is a CLOSED membership loop; the OPEN SmoothGaussian pins endpoints and kinks at the
        //    closure seam, so the ring path needs this wrap-around variant. ──

        [Fact]
        public void SmoothGaussianClosed_SigmaZeroOrTiny_IsIdentity()
        {
            var ring = MakeNoisyRing(count: 80, radiusM: 500, noiseAmpM: 20, seed: 11);
            // σ ≤ 0 is the off-by-default legacy path: return the input reference unchanged (byte-identical).
            Assert.Same(ring, PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 0.0));
            Assert.Same(ring, PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: -5.0));
            // A degenerate <3-point loop is returned as-is regardless of σ.
            var two = new[] { new WzVec2(0, 0), new WzVec2(10, 0) };
            Assert.Same(two, PolylineSmoother.SmoothGaussianClosed(two, sigmaMeters: 30.0));
        }

        [Fact]
        public void SmoothGaussianClosed_IsDeterministic()
        {
            var ring = MakeNoisyRing(count: 120, radiusM: 600, noiseAmpM: 25, seed: 12);
            var a = PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 30.0);
            var b = PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 30.0);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].X, b[i].X, 12);
                Assert.Equal(a[i].Z, b[i].Z, 12);
            }
        }

        [Fact]
        public void SmoothGaussianClosed_ReducesRadialNoise_AndLargerSigmaReducesMore()
        {
            // A clean circle corrupted with radial noise. Smoothing must shrink the mean |r - R| deviation,
            // and a larger σ must shrink it more (the low-pass gets stronger with width) — the same physical
            // guarantee as the open smoother, now around a closed loop.
            const double R = 600;
            var ring = MakeNoisyRing(count: 200, radiusM: R, noiseAmpM: 24, seed: 13);
            double raw = MeanAbsRadius(ring, R);
            double d10 = MeanAbsRadius(PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 10.0), R);
            double d30 = MeanAbsRadius(PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 30.0), R);
            Assert.True(d10 < raw, $"σ=10 should reduce ring noise ({d10:F2} !< {raw:F2})");
            Assert.True(d30 < d10, $"σ=30 should smooth a ring more than σ=10 ({d30:F2} !< {d10:F2})");
        }

        [Fact]
        public void SmoothGaussianClosed_PreservesWinding()
        {
            // A CCW noisy ring must stay CCW (positive signed area). The fill baker's point-in-polygon AND
            // the ring refiner's watertight winding-sign guard both depend on orientation never flipping.
            var ring = MakeNoisyRing(count: 100, radiusM: 500, noiseAmpM: 20, seed: 14);
            double before = ClosedSignedArea(ring);
            double after = ClosedSignedArea(PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 30.0));
            Assert.True(before > 0, "test fixture should be CCW (positive area)");
            Assert.True(after > 0, $"smoothing flipped winding (area {before:F0} → {after:F0})");
            // A low-pass shrinks a noisy loop slightly (noise inflates area; smoothing removes it) but must
            // neither collapse nor balloon it.
            Assert.InRange(after / before, 0.75, 1.05);
        }

        [Fact]
        public void SmoothGaussianClosed_SmoothsAcrossClosureSeam_NoKink()
        {
            // The whole reason this variant exists: a fill ring is CLOSED, so the OPEN smoother (which pins
            // endpoints) leaves a jagged corner where the loop closes. The closed smoother wraps its window
            // MODULO n and smooths through the seam. Measure "kink" as each vertex's deviation from its
            // ring-neighbour midpoint, computed MODULO n so the seam is included. Closed must show NO seam
            // spike; the open smoother on the same ring must (its two pinned noisy ends ARE the seam corner).
            var ring = MakeNoisyRing(count: 160, radiusM: 700, noiseAmpM: 22, seed: 15);
            var closed = PolylineSmoother.SmoothGaussianClosed(ring, sigmaMeters: 30.0);
            var open = PolylineSmoother.SmoothGaussian(ring, sigmaMeters: 30.0);   // pins endpoints ⇒ seam kink
            double kinkClosed = MaxModularMidpointDev(closed);
            double kinkOpen = MaxModularMidpointDev(open);
            Assert.True(kinkClosed < kinkOpen,
                $"closed-loop smoothing should have NO seam kink vs the open smoother ({kinkClosed:F2} !< {kinkOpen:F2})");
            // Absolute: the closed loop's worst local midpoint deviation is tiny (a clean smooth circle).
            Assert.True(kinkClosed < 6.0, $"closed ring worst kink = {kinkClosed:F2} m (expected < 6)");
        }

        [Fact]
        public void ResampleUniformClosed_EvenSpacingAllTheWayAround_KeepsFirstNotLast()
        {
            // A closed square (first != last) resampled fine: every edge around the loop — INCLUDING the
            // closing edge n-1 → 0 — is ~step, and the output stays a closed loop (no duplicated closure).
            var square = new[] { new WzVec2(0, 0), new WzVec2(100, 0), new WzVec2(100, 100), new WzVec2(0, 100) };
            var outp = PolylineSmoother.ResampleUniformClosed(square, step: 5.0);
            Assert.True(outp.Count > square.Length, "resampling a 400 m perimeter at 5 m should add points");
            Assert.True(Dist(outp[0], outp[^1]) > 1e-3, "closed output must keep first != last (no dup closure vertex)");
            int n = outp.Count, within = 0;
            for (int i = 0; i < n; i++)
            {
                double gap = Dist(outp[i], outp[(i + 1) % n]);   // includes the closing edge
                if (gap >= 3.0 && gap <= 7.5) within++;
            }
            // Every gap is ~step except at most ONE that carries the folded sub-step closure remainder.
            Assert.True(within >= n - 1, $"closed resample spacing uneven: only {within}/{n} edges within [3,7.5] m");
        }

        // Build a polyline along +X at fixed spacing with deterministic perpendicular (±z) noise, so
        // tests never depend on wall-clock randomness. Baseline is z=0; noise is bounded by noiseAmpM.
        private static WzVec2[] MakeNoisyLine(int count, double spacingM, double noiseAmpM, int seed)
        {
            var rng = new Random(seed);
            var pts = new WzVec2[count];
            for (int i = 0; i < count; i++)
            {
                double z = (i == 0 || i == count - 1) ? 0.0 : (rng.NextDouble() * 2 - 1) * noiseAmpM;
                pts[i] = new WzVec2(i * spacingM, z);
            }
            return pts;
        }

        private static double MeanAbsZ(IReadOnlyList<WzVec2> pts)
        {
            double s = 0; foreach (var p in pts) s += Math.Abs(p.Z); return s / pts.Count;
        }

        private static double Dist(WzVec2 a, WzVec2 b)
        {
            double dx = a.X - b.X, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dz * dz);
        }

        // Build a CLOSED ring: a circle of radius R with deterministic radial noise, first != last (the fill
        // ring convention). Used by the SmoothGaussianClosed suite.
        private static WzVec2[] MakeNoisyRing(int count, double radiusM, double noiseAmpM, int seed)
        {
            var rng = new Random(seed);
            var pts = new WzVec2[count];
            for (int i = 0; i < count; i++)
            {
                double ang = 2.0 * Math.PI * i / count;
                double r = radiusM + (rng.NextDouble() * 2 - 1) * noiseAmpM;
                pts[i] = new WzVec2(r * Math.Cos(ang), r * Math.Sin(ang));
            }
            return pts;
        }

        private static double MeanAbsRadius(IReadOnlyList<WzVec2> pts, double R)
        {
            double s = 0; foreach (var p in pts) s += Math.Abs(Math.Sqrt(p.X * p.X + p.Z * p.Z) - R); return s / pts.Count;
        }

        // Signed area of a CLOSED loop (first != last): the closing edge n-1 → 0 is included via modulo.
        private static double ClosedSignedArea(IReadOnlyList<WzVec2> v)
        {
            double s = 0; int n = v.Count;
            for (int i = 0; i < n; i++) { WzVec2 a = v[i], b = v[(i + 1) % n]; s += a.X * b.Z - b.X * a.Z; }
            return s / 2.0;
        }

        // Worst-case local jaggedness of a CLOSED loop: max over vertices of the distance from each vertex to
        // the midpoint of its two ring neighbours (indices modulo n, so the closure seam is measured too).
        private static double MaxModularMidpointDev(IReadOnlyList<WzVec2> v)
        {
            double worst = 0; int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                WzVec2 prev = v[(i - 1 + n) % n], next = v[(i + 1) % n];
                double mx = (prev.X + next.X) * 0.5, mz = (prev.Z + next.Z) * 0.5;
                double dx = v[i].X - mx, dz = v[i].Z - mz;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d > worst) worst = d;
            }
            return worst;
        }

        [Fact]
        public void RefineCoastlinesSmoothed_OnRealNiflheim_ChainsAndStaysNearIso()
        {
            var sampler = PortWorldSampler.FromSeed(NiflheimSeed);
            RegionWorld world = WorldZonesRuntime.Build(
                sampler, new RegionBuildOptions { IncludeInlandWater = true });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var field = new HeightScalarField(sampler); // default 25 m coast iso

            IReadOnlyList<RefinedBorder> arcs = RegionBoundaryRefiner.RefineCoastlinesSmoothed(graph, field);

            // Chaining collapses the ~12k per-segment pieces into far fewer continuous arcs.
            Assert.NotEmpty(arcs);
            Assert.True(arcs.Count < graph.Segments.Count / 10,
                $"chaining should produce far fewer arcs than segments ({arcs.Count} vs {graph.Segments.Count})");

            // Smoothed arcs still sit near the coast iso (smoothing must not drag them off the contour).
            int hugged = 0; double sumAbs = 0; int n = 0;
            foreach (var a in arcs)
            {
                if (!a.Hugged) continue;
                hugged++;
                foreach (var p in a.Polyline) { sumAbs += Math.Abs(field.Sample(p.X, p.Z) - field.IsoLevel); n++; }
            }
            Assert.True(hugged > 0);
            double meanAbs = sumAbs / n;
            // Looser than the un-smoothed gate (Chaikin intentionally cuts corners off the exact
            // contour) but still close — proves smoothing rounds without wandering off the coast.
            Assert.True(meanAbs < 8.0, $"smoothed coast mean |height-iso| = {meanAbs:F2} m (expected < 8)");
        }

        [Fact]
        public void RefineBiomeSeams_OnRealNiflheim_ProducesArcsForRegionPairs()
        {
            var sampler = PortWorldSampler.FromSeed(NiflheimSeed);
            RegionWorld world = WorldZonesRuntime.Build(
                sampler, new RegionBuildOptions { IncludeInlandWater = true, UseFeatureAwareBorders = true });
            RegionBoundaryGraph graph = world.BuildBoundaryGraph();
            var biomeField = new BiomeCategoryField(sampler);

            IReadOnlyList<RefinedBorder> seams = RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField);

            // Region-region seams only — every arc carries TWO real region keys (no coastline here).
            Assert.NotEmpty(seams);
            foreach (var s in seams)
            {
                Assert.False(string.IsNullOrEmpty(s.KeyA));
                Assert.False(string.IsNullOrEmpty(s.KeyB));
                Assert.True(s.Polyline.Count >= 2);
            }
            // At least some seams hug a biome flip (feature-aware borders put many seams ON biome edges).
            Assert.Contains(seams, s => s.Hugged);
        }
    }
}
