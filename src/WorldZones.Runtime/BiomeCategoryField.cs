using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Runtime
{
    /// <summary>
    /// An <see cref="ICategoryField"/> over the world sampler's biome field — the categorical field a
    /// region-vs-region border hugs (the biome transition line). Returns the biome enum ordinal at a
    /// world point. Lives in <c>WorldZones.Runtime</c> (reads the sampler); the Tier-1 biome-seam
    /// refiner consumes the <see cref="ICategoryField"/> seam so it stays game-free. See
    /// docs/design/region-render-seam.md.
    /// </summary>
    public sealed class BiomeCategoryField : ICategoryField
    {
        private readonly IWorldSampler sampler;

        public BiomeCategoryField(IWorldSampler sampler)
        {
            this.sampler = sampler ?? throw new System.ArgumentNullException(nameof(sampler));
        }

        public int CategoryAt(double worldX, double worldZ)
            => (int)this.sampler.GetBiome((float)worldX, (float)worldZ);
    }
}
