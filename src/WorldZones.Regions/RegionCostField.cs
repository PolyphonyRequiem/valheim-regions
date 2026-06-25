namespace WorldZones.Regions
{
    /// <summary>
    /// An opaque per-zone traversal cost for cost-weighted region growth — the "topographic relief" in
    /// the watershed-segmentation model (see docs/design/region-borders.md). Regions stays **biome-blind
    /// by design**: it does not know WHY a cell is expensive, only the number. The consumer
    /// (<c>WorldZones.Runtime</c>) computes the field from biomes/shore/terrain and hands it down, so the
    /// topology library never imports WorldGen. A <c>null</c> field means flat cost-1 everywhere, which
    /// makes Dijkstra growth degrade EXACTLY to the legacy unweighted BFS — the graceful, reversible
    /// fallback.
    ///
    /// <para>Indexed <c>[gy, gx]</c> (grid-local; <c>gx = zx − grid.MinIndex</c>), matching
    /// <c>regionIdGrid</c>. The value is the cost to ENTER that cell during growth: high on a feature a
    /// border should fall on (a wall the two regions meet at), low in a biome interior (cheap to fill).</para>
    /// </summary>
    public sealed class RegionCostField
    {
        private readonly double[,] cost;

        /// <summary>Grid extent along each axis (square).</summary>
        public int Size { get; }

        public RegionCostField(double[,] cost)
        {
            this.cost = cost ?? throw new System.ArgumentNullException(nameof(cost));
            int h = cost.GetLength(0), w = cost.GetLength(1);
            if (h != w) throw new System.ArgumentException("cost field must be square", nameof(cost));
            this.Size = h;
        }

        /// <summary>Cost to enter cell (gx, gy). Clamped to ≥ a tiny epsilon so Dijkstra stays well-formed.</summary>
        public double EnterCost(int gx, int gy)
        {
            double c = this.cost[gy, gx];
            return c > 1e-6 ? c : 1e-6;
        }
    }
}
