namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// A pure 2D point in world space (metres), double precision. The Tier-1 geometry currency —
    /// no <c>UnityEngine</c> dependency, so it lives in the headless-testable layer. <see cref="X"/>
    /// is world-X and <see cref="Z"/> is world-Z (Valheim's ground plane is X/Z; Y is up and is not
    /// part of map geometry). See docs/design/region-render-seam.md.
    /// </summary>
    public readonly struct WzVec2
    {
        /// <summary>World-X in metres.</summary>
        public readonly double X;

        /// <summary>World-Z in metres.</summary>
        public readonly double Z;

        public WzVec2(double x, double z)
        {
            this.X = x;
            this.Z = z;
        }

        public override string ToString() =>
            "(" + this.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + ", " + this.Z.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
