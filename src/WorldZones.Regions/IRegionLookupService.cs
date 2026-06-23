namespace WorldZones.Regions
{
    public interface IRegionLookupService
    {
        RegionLookupResult ResolveCurrent(float worldX, float worldZ);
    }

    public enum RegionResolutionReason
    {
        Resolved,
        Unassigned,
        OutOfBounds,
        DataUnavailable
    }

    public sealed class RegionLookupResult
    {
        public bool HasRegion { get; set; }

        public int? RegionId { get; set; }

        /// <summary>
        /// Durable, coordinate-derived identity of the resolved region (null if unresolved).
        /// Prefer this over <see cref="RegionId"/> for persistence and anything player-facing —
        /// the int ID is a transient seed-list index.
        /// </summary>
        public string RegionKey { get; set; }

        public string RegionName { get; set; }

        public RegionResolutionReason ResolutionReason { get; set; }
    }
}
