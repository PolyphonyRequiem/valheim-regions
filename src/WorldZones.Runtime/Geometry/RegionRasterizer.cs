using System;
using System.Collections.Generic;
using WorldZones.Regions;

namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// Pure Tier-1 scan-conversion of the region RING polygons into a region-id raster at an ARBITRARY
    /// resolution (metres-per-texel), independent of the 64 m zone lattice. This is the shape-accurate
    /// FILL primitive — the headless, bundle-free answer to the "blocky fill" gap.
    ///
    /// <para><b>Why this exists.</b> The shipped fill (<c>RegionTextureBaker</c>) bakes the per-zone
    /// <c>regionIdGrid</c> directly: one texel per 64 m zone, so region edges are a 64 m staircase that
    /// disagrees with the crisp refined-arc border lines (the "blocky fill" + "fill pokes past the line"
    /// report). The locked Path B answer was a per-pixel minimap COMPOSE SHADER, but a true custom
    /// fragment shader requires an AssetBundle compiled in the Unity editor (cf. Jotunn's
    /// <c>MinimapCompose*.shader</c> → bundle), which this headless box cannot produce and which the
    /// mod's no-asset-bundle / no-builtin-sprite doctrine deliberately avoids. Scan-filling the ring
    /// POLYGONS into a higher-resolution id raster gets the SAME win — sharp region edges at the chosen
    /// resolution, each texel exactly one region id (so the colourblind lightness palette stays pure and
    /// the texture keeps <c>FilterMode.Point</c>) — with NO shader and NO bundle. The output drops into
    /// the existing <c>RegionTextureBaker</c> consumer path unchanged (same <c>int[,]</c> shape, same
    /// world-aligned uvRect math), and a future shader backend can use this exact raster as its id
    /// texture, so the primitive is reusable by either fork of Path B.</para>
    ///
    /// <para><b>Coordinate frame (LOAD-BEARING — must match the baker + extractor).</b> Texel
    /// <c>[gy, gx]</c> covers the world square whose MIN corner is
    /// <c>(originX + gx·cell, originZ + gy·cell)</c>; the texel CENTRE (the point classified) is half a
    /// cell in. The default origin mirrors <see cref="RegionBoundaryExtractor"/>'s lattice corner
    /// (<c>minIndex·64 − 32</c>) and the default span is the zone grid's, so at <c>cell = 64</c> this
    /// reproduces the coarse grid exactly (a regression anchor) and at <c>cell &lt; 64</c> it refines
    /// within the SAME world window — the fill and the ink share one frame with no half-texel drift
    /// (the <c>RegionTextureBaker.WorldAlignedUvRect</c> contract, AC-T2-FILL-2).</para>
    ///
    /// <para><b>Winding rule.</b> Uses the standard even-odd (parity) scanline rule, which honours the
    /// CCW-outer / CW-hole convention <see cref="RegionRing"/> guarantees for free: a point inside an
    /// outer ring but also inside a hole ring crosses an even number of that region's edges → correctly
    /// classified as NOT this region (the hole shows through to whatever sits inside it). Pure (no Unity,
    /// no game read) so it runs under the net8 headless test net. See docs/design/region-render-seam.md.</para>
    /// </summary>
    public static class RegionRasterizer
    {
        /// <summary>The default per-texel size: the 64 m zone lattice (reproduces the coarse grid).</summary>
        public const double DefaultCellSize = ZoneGrid.ZoneSize;

        /// <summary>
        /// Scan-fill the region rings of <paramref name="graph"/> into a region-id raster, mapping each
        /// region's durable key to the int label <paramref name="keyToLabel"/> supplies (the same label
        /// the palette is indexed by — typically <c>RegionInfo.TransientId</c> / the grid label). A texel
        /// whose centre is in no region (ocean / outside every ring, or inside a hole with nothing behind
        /// it) is <c>-1</c> (unassigned → transparent in the baker).
        /// </summary>
        /// <param name="graph">The boundary graph carrying the closed <see cref="RegionRing"/> loops.</param>
        /// <param name="keyToLabel">Region durable key → int label. A region whose key is absent here is
        ///   skipped (defensive: a ring with no label record contributes nothing rather than throwing).</param>
        /// <param name="minIndex">The zone grid's minimum coordinate on each axis (<c>RegionWorld.Grid.MinIndex</c>),
        ///   anchoring the raster origin to the extractor/baker lattice.</param>
        /// <param name="gridWidth">Zone-grid width (gx extent) — the world window the raster covers.</param>
        /// <param name="gridHeight">Zone-grid height (gy extent).</param>
        /// <param name="cellSize">Per-texel size in metres. Default 64 (coarse-grid parity); pass e.g. 16
        ///   for 4× linear / 16× areal refinement. Must be &gt; 0 and divide the 64 m window evenly for a
        ///   clean integer raster (enforced: rounded up to cover the window).</param>
        /// <returns>A <c>[gyOut, gxOut]</c> int raster of region labels (<c>-1</c> = unassigned), where
        ///   <c>gxOut = ceil(gridWidth·64 / cellSize)</c> and likewise for height.</returns>
        public static int[,] Rasterize(
            RegionBoundaryGraph graph,
            IReadOnlyDictionary<string, int> keyToLabel,
            int minIndex,
            int gridWidth,
            int gridHeight,
            double cellSize = DefaultCellSize)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (keyToLabel == null) throw new ArgumentNullException(nameof(keyToLabel));
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize must be > 0");
            if (gridWidth <= 0 || gridHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(gridWidth), "grid extents must be > 0");

            const double zone = ZoneGrid.ZoneSize;            // 64
            const double half = ZoneGrid.ZoneSize / 2.0;      // 32
            double originX = minIndex * zone - half;          // SAME lattice corner as RegionBoundaryExtractor.Corner
            double originZ = minIndex * zone - half;
            double spanX = gridWidth * zone;
            double spanZ = gridHeight * zone;

            int outW = (int)Math.Ceiling(spanX / cellSize - 1e-9);
            int outH = (int)Math.Ceiling(spanZ / cellSize - 1e-9);
            if (outW < 1) outW = 1;
            if (outH < 1) outH = 1;

            var raster = new int[outH, outW];
            for (int gy = 0; gy < outH; gy++)
                for (int gx = 0; gx < outW; gx++)
                    raster[gy, gx] = -1;

            // Rasterize region-by-region. Each region paints only its OWN label; a later region cannot
            // overwrite an earlier one (the rings are non-overlapping by construction — they partition the
            // land), so paint order is irrelevant and we never need a z-buffer. We scan each region's outer
            // ring(s) + holes together under one parity rule so holes punch through in the same pass.
            foreach (var kv in GroupRingsByRegion(graph))
            {
                string regionKey = kv.Key;
                if (!keyToLabel.TryGetValue(regionKey, out int label)) continue;   // no label → skip (defensive)
                ScanFillRegion(raster, kv.Value, label, originX, originZ, cellSize, outW, outH);
            }

            return raster;
        }

        private static Dictionary<string, List<RegionRing>> GroupRingsByRegion(RegionBoundaryGraph graph)
        {
            var byKey = new Dictionary<string, List<RegionRing>>(StringComparer.Ordinal);
            foreach (var ring in graph.Rings)
            {
                if (ring == null || ring.Vertices.Count < 3) continue;
                if (!byKey.TryGetValue(ring.RegionKey, out var list))
                {
                    list = new List<RegionRing>();
                    byKey[ring.RegionKey] = list;
                }
                list.Add(ring);
            }
            return byKey;
        }

        /// <summary>
        /// Even-odd scanline fill of one region's full ring set (outer + holes) into <paramref name="raster"/>.
        /// Only texels whose CENTRE is inside the region (odd crossing count over ALL the region's rings)
        /// get <paramref name="label"/>. Bounded to the region's vertical extent so a small region costs
        /// O(its own rows), not the whole raster.
        /// </summary>
        private static void ScanFillRegion(
            int[,] raster, List<RegionRing> rings, int label,
            double originX, double originZ, double cellSize, int outW, int outH)
        {
            // Region vertical extent in world-Z → the texel-row band to scan.
            double minZ = double.MaxValue, maxZ = double.MinValue;
            foreach (var ring in rings)
                foreach (var v in ring.Vertices)
                {
                    if (v.Z < minZ) minZ = v.Z;
                    if (v.Z > maxZ) maxZ = v.Z;
                }
            if (minZ > maxZ) return;

            int gyStart = (int)Math.Floor((minZ - originZ) / cellSize);
            int gyEnd = (int)Math.Ceiling((maxZ - originZ) / cellSize);
            if (gyStart < 0) gyStart = 0;
            if (gyEnd > outH - 1) gyEnd = outH - 1;

            var crossings = new List<double>(16);
            for (int gy = gyStart; gy <= gyEnd; gy++)
            {
                double scanZ = originZ + (gy + 0.5) * cellSize;   // texel-row CENTRE in world-Z
                crossings.Clear();

                // Collect X where each ring edge crosses this scanline. Half-open [zA, zB) rule (a vertex
                // exactly on the scanline counts for the edge going DOWN-to-UP only) avoids double-counting
                // at shared vertices — the standard robust parity convention.
                foreach (var ring in rings)
                {
                    var vs = ring.Vertices;
                    int n = vs.Count;
                    for (int i = 0; i < n; i++)
                    {
                        WzVec2 a = vs[i];
                        WzVec2 b = vs[(i + 1) % n];   // implicitly-closed loop: last → first
                        double zA = a.Z, zB = b.Z;
                        bool upward = zA <= scanZ && zB > scanZ;
                        bool downward = zB <= scanZ && zA > scanZ;
                        if (!upward && !downward) continue;
                        double t = (scanZ - zA) / (zB - zA);
                        crossings.Add(a.X + t * (b.X - a.X));
                    }
                }

                if (crossings.Count < 2) continue;
                crossings.Sort();

                // Fill between successive crossing PAIRS (inside spans). Texel-column centres in [xL, xR)
                // get the label.
                for (int c = 0; c + 1 < crossings.Count; c += 2)
                {
                    double xL = crossings[c], xR = crossings[c + 1];
                    int gxL = (int)Math.Ceiling((xL - originX) / cellSize - 0.5);   // first centre ≥ xL
                    int gxR = (int)Math.Floor((xR - originX) / cellSize - 0.5);     // last centre < xR
                    if (gxL < 0) gxL = 0;
                    if (gxR > outW - 1) gxR = outW - 1;
                    for (int gx = gxL; gx <= gxR; gx++)
                        raster[gy, gx] = label;
                }
            }
        }
    }
}
