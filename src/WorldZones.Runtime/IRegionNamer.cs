using System.Collections.Generic;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The naming seam. A consumer that wants different region names supplies its own
    /// <see cref="IRegionNamer"/>; the default is <see cref="MultiSchemaRegionNamer"/>.
    ///
    /// <para>
    /// Why a seam: the multi-schema faux-lore naming design is intentionally PROVISIONAL — rosters,
    /// register-mix, and weights are still being tuned. Putting it behind an interface means that
    /// tuning (or a wholesale replacement) is a swap behind the boundary: zero churn for consumers,
    /// zero change to <see cref="WorldZonesRuntime.Build"/>'s signature or to <see cref="RegionInfo"/>.
    /// The API shape sets hard; the name content stays soft.
    /// </para>
    ///
    /// <para>
    /// Naming is whole-world, not per-region, on purpose: it needs the full set to (a) award rare
    /// superlative landmarks ("the Roof of the World" → the single highest region) and (b) run a
    /// deterministic uniqueness pass so no two regions in a world collide. Both must be stable on
    /// <c>RegionKey</c> so a name survives the seed-list churn that border rewrites / authored seeds
    /// / Valheim 1.0 will cause.
    /// </para>
    /// </summary>
    public interface IRegionNamer
    {
        /// <summary>
        /// Assigns a deterministic <see cref="RegionInfo.Name"/> to every region in the world.
        /// Implementations should be pure functions of (worldId, the region set) so the same world
        /// always names identically, regardless of enumeration order.
        /// </summary>
        /// <param name="worldId">Stable world identity (mixed into the hash so two worlds differ).</param>
        /// <param name="regions">All regions in the world; mutated in place to set <c>Name</c>.</param>
        void NameAll(string worldId, IReadOnlyList<RegionInfo> regions);
    }
}
