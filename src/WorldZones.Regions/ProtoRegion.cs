namespace WorldZones.Regions
{
    /// <summary>
    /// A land-only proto-region produced by multi-source BFS from a seed zone.
    /// Each proto-region is a contiguous set of <see cref="DepthClass.Land"/> zones
    /// assigned to the nearest seed via unweighted BFS.
    /// </summary>
    public class ProtoRegion
    {
        /// <summary>
        /// Zero-based region identifier (matches the seed index). This is a TRANSIENT internal
        /// label — it is the seed's position in the seeds list and renumbers when the list changes.
        /// Do NOT persist or derive names from it; use <see cref="RegionKey"/> for durable identity.
        /// </summary>
        public int Id { get; }

        /// <summary>The zone coordinate where this region was originally seeded.</summary>
        public Vector2i Seed { get; }

        /// <summary>
        /// The region's durable identity coordinate (Option B: the MIN seed coordinate among all
        /// seeds that ended up in this region after merges, under <see cref="WorldZones.Regions.RegionKey.Compare"/>).
        /// For an unmerged region this equals <see cref="Seed"/>. Stable under seed-list reordering
        /// and merge-order changes.
        /// </summary>
        public Vector2i IdentityCoord { get; set; }

        /// <summary>Canonical string identity, derived from <see cref="IdentityCoord"/>.</summary>
        public string RegionKey => WorldZones.Regions.RegionKey.From(this.IdentityCoord);

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
            IdentityCoord = seed; // default identity = own seed; overwritten if merges absorb lower seeds
        }
    }
}
