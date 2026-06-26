using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Computes the biome-diversity <see cref="SeedingField"/> that drives the SEEDING lever — the
    /// only lever proven able to move region composition (docs/design/region-borders.md). Like
    /// <see cref="RegionCostFieldBuilder"/>, this lives in <c>WorldZones.Runtime</c> (it reads biomes)
    /// and hands the biome-blind topology library an opaque field of numbers.
    ///
    /// <para><b>The model:</b> the multi-biome-blob oddity (a region that spans 3–4 biomes) is caused
    /// by ONE seed landing in the middle of a diverse landmass. Routing can't fix it — the seed owns
    /// all four biomes however its border falls. The fix is to drop MORE seeds in diverse land so it
    /// splits into smaller, more-mono-biome regions. The per-zone weight measures local biome
    /// diversity; a component's mean weight scales its seed budget up.</para>
    ///
    /// <para><b>Per-zone weight</b> = (distinct LAND biomes in the zone's
    /// <see cref="RegionSeedingFieldOptions.NeighbourhoodRadius"/> window − 1) / (maxDistinct − 1),
    /// clamped to [0, 1]. A zone whose neighbourhood is one biome → 0 (mono, no extra seeds wanted);
    /// a zone straddling several biomes → →1 (a junction that should be split apart). Water cells get
    /// weight 0 (seeds are land-only). The radius trades locality for smoothness: too small and only
    /// the exact seam flags, too large and everything looks diverse.</para>
    /// </summary>
    public static class RegionSeedingFieldBuilder
    {
        /// <summary>
        /// Build the diversity field over the classified grid using the sampler's biome field. Only
        /// land cells carry weight (seed placement is land-only). Indexed <c>[gy, gx]</c> to match
        /// <c>regionIdGrid</c> / the cost field.
        /// </summary>
        public static SeedingField Build(IWorldSampler sampler, ZoneGrid grid, RegionSeedingFieldOptions options = null)
        {
            options ??= RegionSeedingFieldOptions.Default;
            int size = grid.Size, min = grid.MinIndex;

            // Pre-sample biome per land cell once (GetBiome is the expensive call).
            var biome = new BiomeType[size, size];
            var isLand = new bool[size, size];
            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int zx = gx + min, zy = gy + min;
                bool land = grid[zx, zy] == DepthClass.Land;
                isLand[gy, gx] = land;
                if (land)
                {
                    float wx = zx * (float)ZoneGrid.ZoneSize, wz = zy * (float)ZoneGrid.ZoneSize;
                    biome[gy, gx] = sampler.GetBiome(wx, wz);
                }
            }

            int r = options.NeighbourhoodRadius < 1 ? 1 : options.NeighbourhoodRadius;
            // Max distinct land biomes a window can plausibly show: cap the normaliser so a 2-biome
            // straddle already reads as meaningfully diverse (the multi-biome blobs are 3–4-way).
            double denom = options.MaxDistinctBiomes > 1 ? options.MaxDistinctBiomes - 1 : 1;

            var weight = new double[size, size];
            var seen = new HashSet<BiomeType>();
            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                if (!isLand[gy, gx]) { weight[gy, gx] = 0.0; continue; }

                seen.Clear();
                for (int dy = -r; dy <= r; dy++)
                {
                    int ay = gy + dy;
                    if (ay < 0 || ay >= size) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int ax = gx + dx;
                        if (ax < 0 || ax >= size) continue;
                        if (!isLand[ay, ax]) continue; // land biomes only
                        seen.Add(biome[ay, ax]);
                    }
                }

                double diversity = (seen.Count - 1) / denom;
                weight[gy, gx] = diversity < 0.0 ? 0.0 : (diversity > 1.0 ? 1.0 : diversity);
            }

            return new SeedingField(weight, options.Aggressiveness, options.PlacementBias);
        }
    }

    /// <summary>
    /// Tunables for <see cref="RegionSeedingFieldBuilder"/>. Defaults are a measured-then-tuned
    /// starting point, NOT a locked value — "how aggressive to split" is partly an in-world-walk
    /// judgment (client-gated), so treat these as the dial, not the answer (border-borders.md).
    /// </summary>
    public sealed class RegionSeedingFieldOptions
    {
        /// <summary>
        /// Seed-budget exaggeration. A component's seed count = legacy budget ×
        /// <c>(1 + Aggressiveness · meanDiversity)</c>. 0 reproduces the legacy area-only budget;
        /// larger splits diverse components harder. Default 1.0 (a maximally-diverse component gets up
        /// to ~2× its area-budget seeds).
        /// </summary>
        public double Aggressiveness { get; set; } = 1.0;

        /// <summary>
        /// Half-width (in zones) of the neighbourhood window the per-zone diversity is measured over.
        /// Default 2 (a 5×5 ≈ 320 m window) — wide enough to see a junction, local enough not to
        /// smear every zone into "diverse".
        /// </summary>
        public int NeighbourhoodRadius { get; set; } = 2;

        /// <summary>
        /// The distinct-biome count that normalises to weight 1.0. Default 4 (the worst blobs are
        /// 4-way blends), so a 4-biome window reads as maximally diverse and a 2-biome straddle as ~⅓.
        /// </summary>
        public int MaxDistinctBiomes { get; set; } = 4;

        /// <summary>
        /// Placement bias toward biome interiors, [0, 1). 0 = pure farthest-point (the budget lever
        /// alone). &gt; 0 discounts junction candidates so the extra seeds land IN the biome patches
        /// that should each become their own region, rather than blindly. Default 0 — the budget lever
        /// is the primary; turn this up in the lab to test whether biased placement sharpens the split.
        /// </summary>
        public double PlacementBias { get; set; } = 0.0;

        public static RegionSeedingFieldOptions Default => new RegionSeedingFieldOptions();
    }
}
