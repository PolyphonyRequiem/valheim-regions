using WorldZones.WorldGen;

namespace WorldZones.Regions
{
    /// <summary>
    /// A land component too small to warrant its own proto-region.
    /// Minor islets are tracked but not assigned to any proto-region,
    /// keeping proto-region geometry contiguous on land.
    /// </summary>
    public class MinorIslet
    {
        /// <summary>
        /// The ID of the <see cref="LandComponent"/> this islet represents.
        /// </summary>
        public int LandComponentId { get; }

        /// <summary>Number of land zones in this islet.</summary>
        public int AreaZones { get; }

        public MinorIslet(int landComponentId, int areaZones)
        {
            LandComponentId = landComponentId;
            AreaZones = areaZones;
        }
    }
}
