namespace WorldZones.Regions
{
    /// <summary>
    /// An opaque per-zone "biome diversity" weight that biases SEED PLACEMENT — the seeding-side
    /// lever of the region model (see docs/design/region-borders.md, "the SEEDING lever"). Where the
    /// cost field (<see cref="RegionCostField"/>) decides WHERE a border falls between seeds, this
    /// decides HOW MANY seeds a land component receives: a component spanning many biomes gets more
    /// seeds, so it splits into smaller regions that each span fewer biomes (raising the
    /// dominant-biome fraction). Routing alone cannot do this — a single seed in the middle of four
    /// biomes owns four biomes no matter how its border routes; only adding seeds changes composition.
    ///
    /// <para>Regions stays <b>biome-blind by design</b>: it does not know the number is a biome count,
    /// only the weight. The consumer (<c>WorldZones.Runtime</c>) computes the field from biomes and
    /// hands it down, so the topology library never imports WorldGen — the same separation the cost
    /// field uses. A <c>null</c> field means the legacy area-only seed budget
    /// (<c>max(1, area / target)</c>), i.e. byte-identical to the pre-lever behaviour — the graceful,
    /// reversible fallback.</para>
    ///
    /// <para>Indexed <c>[gy, gx]</c> (grid-local; <c>gx = zx − grid.MinIndex</c>), matching
    /// <c>regionIdGrid</c>. <see cref="Weight"/> is a per-zone diversity in [0, 1] (0 = a mono-biome
    /// neighbourhood, 1 = a maximally mixed junction). <see cref="Aggressiveness"/> is the single
    /// tunable knob: a component's seed budget is scaled by <c>1 + Aggressiveness · meanWeight</c>, so
    /// 0 reproduces the legacy budget and larger values split diverse components harder. "How
    /// aggressive" is partly an in-world-walk judgment — this is a starting dial, not a locked value.</para>
    /// </summary>
    public sealed class SeedingField
    {
        private readonly double[,] weight;

        /// <summary>Grid extent along each axis (square).</summary>
        public int Size { get; }

        /// <summary>
        /// The seed-budget exaggeration knob. A component's seed count is multiplied by
        /// <c>1 + Aggressiveness · (mean weight over the component)</c>. 0 = legacy area-only budget;
        /// higher = more seeds (finer regions) in biome-diverse components. Opaque to Regions — it is
        /// just the scalar in the budget arithmetic.
        /// </summary>
        public double Aggressiveness { get; }

        /// <summary>
        /// The PLACEMENT-bias knob, separate from the budget. 0 = pure farthest-point (legacy). When
        /// &gt; 0, the farthest-point candidate score is multiplied by <c>1 − PlacementBias · weight</c>,
        /// so a high-diversity (junction) candidate is discounted and seeds prefer biome INTERIORS
        /// (low-weight cells). The intent: give each biome patch in a diverse component its own seat so
        /// the cost-field walls split it cleanly, instead of the extra seeds landing blindly. Bounded to
        /// [0, 1); clamped on read. Opaque to Regions (it only multiplies the existing distance score).
        /// </summary>
        public double PlacementBias { get; }

        public SeedingField(double[,] weight, double aggressiveness, double placementBias = 0.0)
        {
            this.weight = weight ?? throw new System.ArgumentNullException(nameof(weight));
            int h = weight.GetLength(0), w = weight.GetLength(1);
            if (h != w) throw new System.ArgumentException("seeding field must be square", nameof(weight));
            this.Size = h;
            this.Aggressiveness = aggressiveness;
            this.PlacementBias = placementBias < 0.0 ? 0.0 : (placementBias > 0.999 ? 0.999 : placementBias);
        }

        /// <summary>Per-zone diversity weight at cell (gx, gy), clamped to [0, 1].</summary>
        public double Weight(int gx, int gy)
        {
            double c = this.weight[gy, gx];
            return c < 0.0 ? 0.0 : (c > 1.0 ? 1.0 : c);
        }
    }
}
