using System;
using System.Collections.Generic;
using WorldZones.Runtime;

namespace WorldZones.Mod.RegionOverlay.Integration
{
    /// <summary>
    /// The LIVE <see cref="ILocationSource"/> — backed by the running game's
    /// <c>ZoneSystem.m_locationInstances</c> (the game's own registration set, populated at world load
    /// by <c>GenerateLocations</c>). This is the game-side counterpart to the headless
    /// <see cref="PortLocationSource"/>, and it is the ONLY source that is runtime-exact: it reports the
    /// game's actual placements, not a reproduction.
    ///
    /// <para>
    /// Like <see cref="ValheimWorldSampler"/>, it lives in the mod project and takes its data via an
    /// injected snapshot delegate rather than referencing <c>ZoneSystem</c> directly — the plugin reads
    /// <c>ZoneSystem.instance.GetLocationList()</c> (or <c>m_locationInstances</c>) and hands the rows in.
    /// That keeps the game-type coupling at the single plugin seam and this class trivially testable.
    /// </para>
    ///
    /// <para><b>The realization overlay.</b> Each row carries the game's live <c>m_placed</c> flag. A
    /// placed row maps to <see cref="PlacementStatus.Realized"/> — the "actually generated into the
    /// world" signal. Because the game deletes losing unique candidates on first realization
    /// (<c>RemoveUnplacedLocations</c>), a snapshot taken after a unique resolves contains only the
    /// winner; before resolution it contains all N candidates. Re-snapshot to observe changes, or use
    /// <see cref="LocationRealizedHandler"/> wired from a <c>ZoneSystem.SpawnLocation</c> patch for a live
    /// push.</para>
    /// </summary>
    public sealed class ValheimLiveLocationSource : ILocationSource
    {
        private readonly Func<IEnumerable<LiveLocationRow>> snapshot;

        /// <summary>
        /// </summary>
        /// <param name="snapshot">Returns the current location rows from the game. The plugin supplies a
        /// closure over <c>ZoneSystem.instance.GetLocationList()</c>, mapping each
        /// <c>LocationInstance</c> to a <see cref="LiveLocationRow"/> (prefab name from
        /// <c>m_location.m_prefab.Name</c>/<c>m_prefabName</c>, position from <c>m_position</c>, unique
        /// from <c>m_location.m_unique</c>, realized from <c>m_placed</c>). Re-invoked on each
        /// <see cref="EnumerateLocations"/> so a fresh build sees current realization state.</param>
        public ValheimLiveLocationSource(Func<IEnumerable<LiveLocationRow>> snapshot)
        {
            this.snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <inheritdoc />
        public IEnumerable<LocationRecord> EnumerateLocations()
        {
            foreach (var row in this.snapshot())
                yield return new LocationRecord(row.PrefabName, row.X, row.Z, row.IsUnique, row.IsPlaced);
        }
    }

    /// <summary>
    /// One row from the live <c>ZoneSystem.m_locationInstances</c>, as extracted by the plugin. A plain
    /// struct so the game-type read stays in the plugin and this integration class needs no Valheim
    /// reference in its own signature.
    /// </summary>
    public readonly struct LiveLocationRow
    {
        /// <summary>Prefab name (<c>m_location.m_prefab.Name</c> / <c>m_prefabName</c>).</summary>
        public readonly string PrefabName;
        /// <summary>World X (<c>m_position.x</c>).</summary>
        public readonly float X;
        /// <summary>World Z (<c>m_position.z</c>).</summary>
        public readonly float Z;
        /// <summary>Unique location? (<c>m_location.m_unique</c>).</summary>
        public readonly bool IsUnique;
        /// <summary>Realized on the ground? (<c>m_placed</c>).</summary>
        public readonly bool IsPlaced;

        public LiveLocationRow(string prefabName, float x, float z, bool isUnique, bool isPlaced)
        {
            this.PrefabName = prefabName;
            this.X = x;
            this.Z = z;
            this.IsUnique = isUnique;
            this.IsPlaced = isPlaced;
        }
    }
}
