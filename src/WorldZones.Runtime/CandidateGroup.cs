using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Runtime
{
    /// <summary>
    /// A world-scoped set of candidate sites for ONE unique location (e.g. Haldor the trader registers
    /// ~10 sites map-wide; exactly one ever spawns). The group lives at the <see cref="RegionWorld"/>
    /// level — not a region — because its candidates span many regions, and the "exactly one wins"
    /// constraint is global. A region merely <em>contains</em> some of a group's candidate sites
    /// (each <see cref="GazetteerLocation"/> back-references the group via
    /// <see cref="GazetteerLocation.CandidateGroupKey"/>).
    ///
    /// <para>
    /// <b>Resolution.</b> Offline / from-seed, <see cref="Resolved"/> is false and every candidate is
    /// <see cref="PlacementStatus.Candidate"/> — the seed does not determine the winner. In a live game,
    /// when a player first loads a candidate's zone it spawns, the game deletes the losing candidates,
    /// and a live source reports <see cref="Resolved"/> = true with <see cref="RealizedSite"/> set.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Post-resolution visibility.</b> Once resolved in a live world, the losing candidates are
    /// gone from the game's own state (<c>RemoveUnplacedLocations</c>), so a live read after resolution
    /// sees only the winner. The full N-candidate set is visible only before resolution (offline, a
    /// fresh world, or a live session pre-discovery). This is the true runtime fact, not a gap.
    /// </para>
    /// </summary>
    public sealed class CandidateGroup
    {
        private readonly List<GazetteerLocation> sites;

        internal CandidateGroup(string prefabName, List<GazetteerLocation> sites)
        {
            this.PrefabName = prefabName;
            this.sites = sites;
        }

        /// <summary>Prefab name this group resolves to (e.g. "Vendor_BlackForest"). Also the value each
        /// member carries in <see cref="GazetteerLocation.CandidateGroupKey"/>.</summary>
        public string PrefabName { get; }

        /// <summary>All candidate sites for this unique, across the whole world. Before resolution this
        /// is the full registered set; after a live resolution it may be just the winner (see the
        /// post-resolution note above).</summary>
        public IReadOnlyList<GazetteerLocation> Candidates => this.sites;

        /// <summary>Number of candidate sites currently known.</summary>
        public int CandidateCount => this.sites.Count;

        /// <summary>True once a live source has observed the unique spawn (one site
        /// <see cref="PlacementStatus.Realized"/>). Always false for an offline build.</summary>
        public bool Resolved => this.sites.Any(s => s.Status == PlacementStatus.Realized);

        /// <summary>The site that actually spawned, once <see cref="Resolved"/>; otherwise null.</summary>
        public GazetteerLocation RealizedSite =>
            this.sites.FirstOrDefault(s => s.Status == PlacementStatus.Realized);

        /// <summary>Distinct region keys this group's candidates touch (a unique "could appear in" any
        /// of these regions). Excludes candidates outside any region (ocean/islet).</summary>
        public IReadOnlyList<string> CandidateRegionKeys =>
            this.sites.Where(s => s.RegionKey != null).Select(s => s.RegionKey).Distinct().ToList();
    }
}
