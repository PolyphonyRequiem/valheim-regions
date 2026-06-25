using WorldZones.Runtime.Geometry;

namespace WorldZones.Runtime
{
    /// <summary>
    /// An <see cref="IScalarField"/> over the world sampler's terrain height — the field a coastline
    /// hugs. The iso-level is sea level (30 m world units, matching
    /// <c>ZoneClassifier.DefaultWaterLevel</c>): the <c>GetHeight == 30</c> contour IS the shoreline.
    ///
    /// <para>Lives in <c>WorldZones.Runtime</c> (not Tier-1 Geometry) because it reads the sampler;
    /// the Tier-1 refiner stays game-free by consuming the <see cref="IScalarField"/> seam. See
    /// docs/design/region-render-seam.md.</para>
    /// </summary>
    public sealed class HeightScalarField : IScalarField
    {
        private readonly IWorldSampler sampler;

        /// <summary>Sea level in world metres — the contour a coastline hugs (vanilla water = 30).</summary>
        public const double SeaLevel = 30.0;

        public HeightScalarField(IWorldSampler sampler, double isoLevel = SeaLevel)
        {
            this.sampler = sampler ?? throw new System.ArgumentNullException(nameof(sampler));
            this.IsoLevel = isoLevel;
        }

        public double IsoLevel { get; }

        public double Sample(double worldX, double worldZ)
            => this.sampler.GetHeight((float)worldX, (float)worldZ);
    }
}
