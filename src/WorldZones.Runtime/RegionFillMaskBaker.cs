using System;
using WorldZones.Regions;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Bakes a HIGH-RESOLUTION region-id raster whose land/water edge follows the COAST ISO (the same
    /// <see cref="HeightScalarField.CoastIso"/> contour the border lines hug), instead of the 64 m zone
    /// staircase. This is the fix for the "fill smear" (grey fill poking past the border line): the
    /// shipped <see cref="WorldZones.Unity.RegionTextureBaker"/> colours one texel per 64 m zone, so its
    /// seaward edge is a coarse staircase that overhangs the real coast by up to ~32 m and disagrees with
    /// the smooth refined-arc border. Here each FINE texel decides land-or-water by sampling the terrain
    /// height at the texel centre against the coast iso — so the fill's coast edge and the line's coast
    /// edge are the SAME curve by construction, and the overhang is gone.
    ///
    /// <para><b>Region label per texel.</b> Land-or-water comes from the iso (fine); WHICH region a land
    /// texel belongs to comes from the underlying 64 m <paramref name="regionIdGrid"/> zone (region
    /// interiors are coarse — internal region-to-region seams are NOT what smears, so they stay 64 m).
    /// A texel is painted iff it is on the land side of the coast iso AND its zone is in a region. The
    /// swamp rescue is honoured: a texel whose biome is Swamp uses the swamp land-floor as its iso, so the
    /// fill covers the swamp the classifier now rescues (rather than re-dropping it at the 25 m coast iso).</para>
    ///
    /// <para>Pure Runtime (reads the sampler; no Unity) so it runs under the headless net8 test net. The
    /// output drops into the existing fill consumer unchanged: same <c>int[,]</c> shape contract and the
    /// same world-aligned origin/span the <see cref="WorldZones.Unity.RegionTextureBaker"/> uses, just at
    /// <c>subdivisions×</c> the resolution. See docs/design/region-render-seam.md + region-borders.md.</para>
    /// </summary>
    public sealed class RegionFillMaskBaker
    {
        /// <summary>Default fine-texel subdivisions per 64 m zone. 4 ⇒ 16 m texels (16× cells), sub-pixel
        /// at large-map zoom, kills the visible staircase at a modest texture-size cost.</summary>
        public const int DefaultSubdivisions = 4;

        private readonly IWorldSampler sampler;
        private readonly double coastIso;
        private readonly double? swampFloor;

        /// <param name="sampler">World height + biome source.</param>
        /// <param name="coastIso">Iso-level (world m) the coast edge hugs — pass
        ///   <see cref="HeightScalarField.CoastIso"/> (25 m) to match the border line exactly.</param>
        /// <param name="swampLandFloor">Swamp land-floor (world m) so the fill honours the swamp rescue;
        ///   null = no swamp special-casing (a swamp texel then uses <paramref name="coastIso"/> like any
        ///   other). Pass the same value as <c>RegionBuildOptions.SwampLandFloorMeters</c>.</param>
        public RegionFillMaskBaker(IWorldSampler sampler,
                                   double coastIso = HeightScalarField.CoastIso,
                                   double? swampLandFloor = null)
        {
            this.sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            this.coastIso = coastIso;
            this.swampFloor = swampLandFloor;
        }

        /// <summary>
        /// Bake the fine region-id raster. Output texel <c>[fy, fx]</c> covers the world square whose MIN
        /// corner is <c>(originX + fx·texel, originZ + fy·texel)</c>, where <c>texel = 64/subdivisions</c>
        /// and the origin is the SAME zone-corner lattice the coarse baker uses
        /// (<c>minIndex·64 − 32</c>) — so the fine texture aligns under the ink with no half-texel drift.
        /// A texel is the region label of its zone when it is on the land side of the iso (and the zone is
        /// in a region); otherwise <c>-1</c> (transparent).
        /// </summary>
        /// <param name="regionIdGrid">Per-zone region label, indexed <c>[gy, gx]</c>; &lt;0 = unassigned.</param>
        /// <param name="minIndex">Zone grid min coord (<c>RegionWorld.Grid.MinIndex</c>).</param>
        /// <param name="subdivisions">Fine texels per zone edge (default 4 ⇒ 16 m). Must be ≥ 1.</param>
        /// <returns>A <c>[gh·sub, gw·sub]</c> int raster of region labels (-1 = water/unassigned).</returns>
        public int[,] Bake(int[,] regionIdGrid, int minIndex, int subdivisions = DefaultSubdivisions)
        {
            if (regionIdGrid == null) throw new ArgumentNullException(nameof(regionIdGrid));
            if (subdivisions < 1) throw new ArgumentOutOfRangeException(nameof(subdivisions));

            const double zone = ZoneGrid.ZoneSize;        // 64
            const double half = ZoneGrid.ZoneSize / 2.0;  // 32
            double texel = zone / subdivisions;
            double originX = minIndex * zone - half;
            double originZ = minIndex * zone - half;

            int gh = regionIdGrid.GetLength(0), gw = regionIdGrid.GetLength(1);
            int fh = gh * subdivisions, fw = gw * subdivisions;
            var outRaster = new int[fh, fw];

            for (int fy = 0; fy < fh; fy++)
            {
                int gy = fy / subdivisions;
                // texel-centre world Z
                double wz = originZ + (fy + 0.5) * texel;
                for (int fx = 0; fx < fw; fx++)
                {
                    int gx = fx / subdivisions;
                    int label = regionIdGrid[gy, gx];
                    if (label < 0) { outRaster[fy, fx] = -1; continue; }    // zone not in a region → water

                    double wx = originX + (fx + 0.5) * texel;
                    outRaster[fy, fx] = IsLand(wx, wz) ? label : -1;
                }
            }
            return outRaster;
        }

        /// <summary>
        /// Fine-grained land test at a world point: terrain height ≥ the coast iso, EXCEPT a Swamp texel
        /// uses the swamp land-floor (when supplied) so the fill covers rescued swamp. This mirrors the
        /// classifier's land rule but at texel resolution, and shares the iso with the border refiner so
        /// the fill edge == the line edge.
        /// </summary>
        private bool IsLand(double wx, double wz)
        {
            double h = this.sampler.GetHeight((float)wx, (float)wz);
            if (h >= this.coastIso) return true;
            if (this.swampFloor.HasValue && h >= this.swampFloor.Value
                && this.sampler.GetBiome((float)wx, (float)wz) == WorldZones.WorldGen.BiomeType.Swamp)
                return true;
            return false;
        }
    }
}
