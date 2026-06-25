namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// A categorical field sampled in world space — returns an opaque biome ordinal at a point. Used to
    /// contour-hug a region-vs-region border onto the actual biome TRANSITION (the crisp line where one
    /// biome flips to another), as opposed to <see cref="IScalarField"/> which hugs a continuous isoline
    /// (a coastline). Pure Tier-1: the implementation (a biome sampler wrapper) is supplied by
    /// <c>WorldZones.Runtime</c>, so the snap math stays headless-testable with synthetic categories.
    /// See docs/design/region-render-seam.md.
    /// </summary>
    public interface ICategoryField
    {
        /// <summary>The opaque category ordinal (e.g. biome) at a world-space point (metres).</summary>
        int CategoryAt(double worldX, double worldZ);
    }
}
