namespace WorldZones.Regions
{
    /// <summary>
    /// A land-only proto-region produced by multi-source BFS from a seed zone.
    /// Each proto-region is a contiguous set of <see cref="DepthClass.Land"/> zones
    /// assigned to the nearest seed via unweighted BFS.
    /// </summary>
    public class ProtoRegion
    {
        /// <summary>Zero-based region identifier (matches the seed index).</summary>
        public int Id { get; }

        /// <summary>The zone coordinate where this region was seeded.</summary>
        public Vector2i Seed { get; }

        /// <summary>Number of land zones assigned to this region.</summary>
        public int AreaZones { get; set; }

        /// <summary>Number of land zones assigned to this region.</summary>
        public int LandAreaZones { get; set; }

        /// <summary>Number of inland-water zones assigned to this region.</summary>
        public int InlandWaterAreaZones { get; set; }

        /// <summary>Total territory zones assigned to this region (land + inland water).</summary>
        public int TotalAreaZones => this.LandAreaZones + this.InlandWaterAreaZones;

        public ProtoRegion(int id, Vector2i seed)
        {
            Id = id;
            Seed = seed;
        }
    }
}
