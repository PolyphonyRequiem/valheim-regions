namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// A scalar field sampled in world space, used to trace a sub-zone contour for a boundary segment.
    /// The contour-hug refiner walks the <c>value == <see cref="IsoLevel"/></c> isoline of this field.
    ///
    /// <para>Pure abstraction (Tier-1): the actual field comes from the world sampler
    /// (<c>GetHeight</c> for a coastline at sea level, a biome-indicator for a biome seam), supplied by
    /// <c>WorldZones.Runtime</c>. Tier-1 never imports the sampler — it consumes this delegate-like
    /// seam, so the marching math stays headless-testable with synthetic fields. See
    /// docs/design/region-render-seam.md.</para>
    /// </summary>
    public interface IScalarField
    {
        /// <summary>The field value at a world-space point (metres).</summary>
        double Sample(double worldX, double worldZ);

        /// <summary>The iso-level the boundary should hug (e.g. sea level 30 for a coast).</summary>
        double IsoLevel { get; }
    }
}
