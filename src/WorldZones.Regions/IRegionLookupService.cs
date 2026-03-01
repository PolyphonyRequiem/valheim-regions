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

        public string RegionName { get; set; }

        public RegionResolutionReason ResolutionReason { get; set; }
    }
}
