namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// A normalised map coordinate: <c>(0,0)</c> is the bottom-left of the map frame, <c>(1,1)</c>
    /// the top-right, <c>(0.5,0.5)</c> the centre. The output of <see cref="MapProjector.Project"/>.
    /// A consumer maps this onto whatever surface it owns (a <c>RawImage</c> uvRect, a disc, a UI
    /// rect) — Tier-1 stays surface-agnostic.
    /// </summary>
    public readonly struct MapUv
    {
        public readonly double U;
        public readonly double V;

        public MapUv(double u, double v)
        {
            this.U = u;
            this.V = v;
        }

        public override string ToString() =>
            "uv(" + this.U.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                  + ", " + this.V.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
