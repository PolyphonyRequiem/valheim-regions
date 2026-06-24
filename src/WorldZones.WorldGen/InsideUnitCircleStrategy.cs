using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Candidate algorithms for <see cref="UnityRandom.InsideUnitCircle"/>.
    /// <para>
    /// <c>UnityEngine.Random.insideUnitCircle</c> is a NATIVE C++ binding
    /// (<c>GetRandomUnitCircle</c>) — its exact draw count and formula are NOT in Unity's public
    /// C# reference source, and the modding community has documented BOTH a rejection-sampling form
    /// (variable draws) and a polar/sqrt form (2 fixed draws) across Unity versions. Rather than guess
    /// (a wrong choice desyncs the RNG stream inside <c>WorldGenerator.GetTerrainDelta</c>, which the
    /// location placement loop calls per candidate), we keep the strategy SWAPPABLE and let the
    /// validation harness (<c>WorldZones.Cli locations --validate</c>) sweep all strategies against a
    /// real world <c>.db</c>'s placed locations and pick the one that reproduces them bit-for-bit.
    /// </para>
    /// </summary>
    public enum InsideUnitCircleStrategy
    {
        /// <summary>Polar: radius = sqrt(value) drawn FIRST, then angle = value * 2π. 2 draws.</summary>
        PolarRadiusFirst,
        /// <summary>Polar: angle = value * 2π drawn FIRST, then radius = sqrt(value). 2 draws.</summary>
        PolarAngleFirst,
        /// <summary>Rejection: draw (x,y) in [-1,1]² until x²+y² ≤ 1. Variable draws (2 per rejection).</summary>
        Rejection,
    }
}
