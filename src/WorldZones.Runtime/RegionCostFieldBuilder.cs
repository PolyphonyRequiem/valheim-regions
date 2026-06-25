using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Computes the v3 biome-edge cost field that turns the legacy terrain-blind BFS into watershed
    /// segmentation. This lives in <c>WorldZones.Runtime</c> — NOT in <c>WorldZones.Regions</c> —
    /// because it reads biomes, and the topology library is biome-blind by design. The field it
    /// produces is opaque <c>double</c>s; Regions consumes it without knowing what the numbers mean.
    ///
    /// <para><b>The model (measured winner, docs/design/region-borders.md):</b> features must be
    /// EXPENSIVE TO CROSS (walls), not cheap (highways) — a wall stalls each region's growth at the
    /// feature so the two regions MEET there. Cost to ENTER a cell:</para>
    /// <list type="bullet">
    ///   <item><b>biome edge</b> (land cell touching a different LAND biome) = 12 — the load-bearing
    ///   term; the one feature proven crisp + line-like on real terrain at every patch.</item>
    ///   <item><b>shore</b> (land cell touching water) = 8 — the biome edge against the sea.</item>
    ///   <item><b>interior</b> = 1 — cheap to fill a biome's middle.</item>
    /// </list>
    /// The 12/8/1 ratios are the per-feature "exaggeration" weights — tunable via
    /// <see cref="RegionCostFieldOptions"/>. A flat field (all 1) degrades Dijkstra exactly to BFS.
    /// </summary>
    public static class RegionCostFieldBuilder
    {
        private static readonly (int dx, int dy)[] N4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>
        /// Build the cost field over the classified grid using the sampler's biome field. Land cells
        /// only carry meaningful cost (growth is land-only); water cells get the interior baseline
        /// (never entered). Indexed <c>[gy, gx]</c> to match <c>regionIdGrid</c>.
        /// </summary>
        public static RegionCostField Build(IWorldSampler sampler, ZoneGrid grid, RegionCostFieldOptions options = null)
        {
            options ??= RegionCostFieldOptions.Default;
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
                    biome[gy, gx] = sampler.GetBiome(zx * (float)ZoneGrid.ZoneSize, zy * (float)ZoneGrid.ZoneSize);
            }

            var cost = new double[size, size];
            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                if (!isLand[gy, gx])
                {
                    cost[gy, gx] = options.InteriorCost; // never entered (land-only growth), keep well-formed
                    continue;
                }

                BiomeType b = biome[gy, gx];
                bool biomeEdge = false, shore = false;
                foreach (var (dx, dy) in N4)
                {
                    int ax = gx + dx, ay = gy + dy;
                    if (ax < 0 || ax >= size || ay < 0 || ay >= size) continue;
                    if (!isLand[ay, ax]) { shore = true; continue; }
                    if (biome[ay, ax] != b) biomeEdge = true;
                }

                cost[gy, gx] = biomeEdge ? options.BiomeEdgeCost
                             : shore ? options.ShoreCost
                             : options.InteriorCost;
            }

            return new RegionCostField(cost);
        }
    }

    /// <summary>
    /// The per-feature "exaggeration" weights for <see cref="RegionCostFieldBuilder"/>. Defaults are
    /// the measured v3 winner (12/8/1). These are the dial the in-world ESP walk tunes per biome; do
    /// NOT treat them as locked — they are the starting point, not the answer (border-model.md).
    /// </summary>
    public sealed class RegionCostFieldOptions
    {
        /// <summary>Cost to cross a land-vs-land biome transition. Default 12 (the load-bearing wall).</summary>
        public double BiomeEdgeCost { get; set; } = 12.0;

        /// <summary>Cost to cross a land-vs-water shore. Default 8.</summary>
        public double ShoreCost { get; set; } = 8.0;

        /// <summary>Cost to fill a biome interior. Default 1.</summary>
        public double InteriorCost { get; set; } = 1.0;

        public static RegionCostFieldOptions Default => new RegionCostFieldOptions();
    }
}
