namespace WorldZones.Regions
{
    /// <summary>
    /// Describes whether a zone is non-water, inland water, or ocean-connected water.
    /// </summary>
    public enum WaterConnectivityKind
    {
        /// <summary>Zone is not water and is excluded from water connectivity processing.</summary>
        NotWater = 0,

        /// <summary>Water zone is enclosed and eligible for inland-water attribution.</summary>
        InlandWater = 1,

        /// <summary>Water zone is connected to world-edge water and excluded from inland attribution.</summary>
        OceanConnectedWater = 2
    }
}
