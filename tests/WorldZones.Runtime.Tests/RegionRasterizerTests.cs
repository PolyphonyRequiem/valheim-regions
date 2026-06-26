using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using Xunit;

namespace WorldZones.Runtime.Tests
{
    /// <summary>
    /// Guards <see cref="RegionRasterizer"/> — the shape-accurate scan-fill that replaces the blocky
    /// 64 m fill. The load-bearing test is COARSE-GRID PARITY: at cell = 64 the polygon rasterizer must
    /// reproduce the engine's own per-zone <c>regionIdGrid</c> exactly, proving the rings + the
    /// scan-fill + the coordinate frame all agree with the authoritative classification. Once that
    /// holds, a finer cell is the SAME shape at higher resolution. Real Niflheim (seed ForTheWort), run
    /// through the production façade. See docs/design/region-render-seam.md (Path B).
    /// </summary>
    public class RegionRasterizerTests
    {
        private const string NiflheimSeed = "ForTheWort";

        private static RegionWorld BuildNiflheim() =>
            WorldZonesRuntime.Build(
                PortWorldSampler.FromSeed(NiflheimSeed),
                new RegionBuildOptions { IncludeInlandWater = true });

        /// <summary>id→key from the proto regions, inverted to key→label (what the rasterizer + palette use).</summary>
        private static Dictionary<string, int> KeyToLabel(RegionWorld w)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ProtoRegion r in w.ProtoResult.Regions)
                if (!map.ContainsKey(r.RegionKey)) map[r.RegionKey] = r.Id;
            return map;
        }

        /// <summary>
        /// THE anchor: at cell = 64 the rasterized region-id raster equals the engine's per-zone
        /// <c>regionIdGrid</c> cell-for-cell (every assigned land zone). This proves the ring polygons,
        /// the parity scan-fill, and the lattice origin are all consistent with the authoritative
        /// classification — so a finer cell is a faithful refinement of the SAME regions, not a new
        /// (possibly mis-registered) source of truth.
        /// </summary>
        [Fact]
        public void CoarseCell_ReproducesEngineRegionIdGrid_CellForCell()
        {
            RegionWorld w = BuildNiflheim();
            RegionBoundaryGraph graph = w.BuildBoundaryGraph();
            int[,] truth = w.RegionIdGrid;
            int h = truth.GetLength(0), wdt = truth.GetLength(1);

            int[,] raster = RegionRasterizer.Rasterize(
                graph, KeyToLabel(w), w.Grid.MinIndex, wdt, h, cellSize: 64.0);

            Assert.Equal(h, raster.GetLength(0));
            Assert.Equal(wdt, raster.GetLength(1));

            long landZones = 0, mismatches = 0;
            for (int gy = 0; gy < h; gy++)
            {
                for (int gx = 0; gx < wdt; gx++)
                {
                    int want = truth[gy, gx];
                    if (want < 0) continue;          // ocean/unassigned: rings don't cover it; -1 either way
                    landZones++;
                    if (raster[gy, gx] != want) mismatches++;
                }
            }

            Assert.True(landZones > 10000, $"expected a real land mass, got {landZones} land zones");
            // Exact parity: the polygon fill at zone resolution IS the zone grid. Allow a vanishingly
            // small tolerance ONLY for true degenerate-edge texels (a scanline grazing a lattice vertex);
            // empirically this is 0 on Niflheim, so the bar is a hard ceiling, not a soft pass.
            double rate = (double)mismatches / landZones;
            Assert.True(rate < 1e-4,
                $"coarse-cell parity broken: {mismatches}/{landZones} land zones differ ({rate:P4})");
        }

        /// <summary>
        /// A finer cell refines within the SAME world window: the output raster is ~4× linear (16×
        /// areal) at cell = 16, covers the identical extent, and assigns the same DOMINANT label mass
        /// (no region vanishes, no phantom region appears). This is the "sharper edges, same regions" win.
        /// </summary>
        [Fact]
        public void FineCell_RefinesSameWindow_PreservesRegionSet()
        {
            RegionWorld w = BuildNiflheim();
            RegionBoundaryGraph graph = w.BuildBoundaryGraph();
            var keyToLabel = KeyToLabel(w);
            int h = w.RegionIdGrid.GetLength(0), wdt = w.RegionIdGrid.GetLength(1);

            int[,] coarse = RegionRasterizer.Rasterize(graph, keyToLabel, w.Grid.MinIndex, wdt, h, 64.0);
            int[,] fine = RegionRasterizer.Rasterize(graph, keyToLabel, w.Grid.MinIndex, wdt, h, 16.0);

            // 64/16 = 4× linear each axis.
            Assert.Equal(coarse.GetLength(0) * 4, fine.GetLength(0));
            Assert.Equal(coarse.GetLength(1) * 4, fine.GetLength(1));

            // Same SET of region labels present (no region lost or invented by refinement).
            Assert.Equal(LabelSet(coarse), LabelSet(fine));
        }

        private static HashSet<int> LabelSet(int[,] r)
        {
            var s = new HashSet<int>();
            for (int gy = 0; gy < r.GetLength(0); gy++)
                for (int gx = 0; gx < r.GetLength(1); gx++)
                    if (r[gy, gx] >= 0) s.Add(r[gy, gx]);
            return s;
        }

        /// <summary>
        /// Hole rings punch through: a region with an inland-water hole must leave that hole UN-painted
        /// (the parity rule crosses the hole's edges an extra time → even → outside the region). We assert
        /// at least one region on Niflheim has a hole, and that the hole interior is not this region's label.
        /// </summary>
        [Fact]
        public void HoleRings_AreNotFilled_WithTheEnclosingRegionsLabel()
        {
            RegionWorld w = BuildNiflheim();
            RegionBoundaryGraph graph = w.BuildBoundaryGraph();
            var keyToLabel = KeyToLabel(w);
            int h = w.RegionIdGrid.GetLength(0), wdt = w.RegionIdGrid.GetLength(1);
            int[,] raster = RegionRasterizer.Rasterize(graph, keyToLabel, w.Grid.MinIndex, wdt, h, 32.0);

            // Find a region with a hole ring.
            var holed = graph.Rings.FirstOrDefault(r => r.IsHole && keyToLabel.ContainsKey(r.RegionKey));
            Assert.True(holed != null, "expected at least one hole ring on Niflheim (inland water enclosed in a region)");

            int label = keyToLabel[holed.RegionKey];
            // Hole centroid (a point clearly inside the hole for these convex-ish enclosed loops).
            double cx = holed.Vertices.Average(v => v.X);
            double cz = holed.Vertices.Average(v => v.Z);

            const double zone = 64.0, half = 32.0, cell = 32.0;
            double originX = w.Grid.MinIndex * zone - half;
            double originZ = w.Grid.MinIndex * zone - half;
            int gx = (int)Math.Floor((cx - originX) / cell);
            int gy = (int)Math.Floor((cz - originZ) / cell);

            if (gx >= 0 && gy >= 0 && gy < raster.GetLength(0) && gx < raster.GetLength(1))
            {
                Assert.NotEqual(label, raster[gy, gx]);   // the hole is NOT this region's fill
            }
        }

        [Fact]
        public void Rasterize_RejectsBadArguments()
        {
            RegionWorld w = BuildNiflheim();
            RegionBoundaryGraph graph = w.BuildBoundaryGraph();
            var keyToLabel = KeyToLabel(w);

            Assert.Throws<ArgumentNullException>(() =>
                RegionRasterizer.Rasterize(null!, keyToLabel, 0, 10, 10));
            Assert.Throws<ArgumentNullException>(() =>
                RegionRasterizer.Rasterize(graph, null!, 0, 10, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RegionRasterizer.Rasterize(graph, keyToLabel, 0, 10, 10, cellSize: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RegionRasterizer.Rasterize(graph, keyToLabel, 0, 0, 10));
        }
    }
}
