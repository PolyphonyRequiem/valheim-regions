using System.Collections.Generic;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The seam a consumer supplies to put LOCATIONS (POIs, dungeons, bosses, structures, traders) into
    /// the gazetteer — the location analogue of <see cref="IWorldSampler"/>. A source answers ONE
    /// question: "what locations does this world contain, and what is each one's placement state?" It
    /// knows nothing about regions; <see cref="WorldZonesRuntime.Build"/> performs the region join.
    ///
    /// <para>Two implementations ship, mirroring the sampler split:</para>
    /// <list type="bullet">
    ///   <item><b><see cref="PortLocationSource"/></b> (this assembly, offline) — runs the verified
    ///   <c>LocationModel</c> port from the seed. Headless, no game. Emits only the registration plan
    ///   (<see cref="PlacementStatus.Registered"/> / <see cref="PlacementStatus.Candidate"/>); it cannot
    ///   know realization, so it never reports <see cref="PlacementStatus.Realized"/>.</item>
    ///   <item><b>ValheimLiveLocationSource</b> (the mod project — needs game assemblies) — reads the
    ///   live <c>ZoneSystem.m_locationInstances</c>. Exact by definition, and the only source that can
    ///   report <see cref="PlacementStatus.Realized"/> + drive the live realization overlay.</item>
    /// </list>
    ///
    /// <para>The split keeps this assembly Unity-free: the interface + the offline implementation live
    /// here; only the plugin touches game types.</para>
    /// </summary>
    public interface ILocationSource
    {
        /// <summary>
        /// Enumerate every location the world contains, with its placement state. Positions are world
        /// metres (registration coordinates — realization never moves them). Order is not significant;
        /// the build sorts/joins. A source MUST set <see cref="LocationRecord.IsUnique"/> for unique
        /// locations so the build can group candidates.
        /// </summary>
        IEnumerable<LocationRecord> EnumerateLocations();
    }

    /// <summary>
    /// One location as reported by an <see cref="ILocationSource"/>, before the region join. The build
    /// turns this into a <see cref="GazetteerLocation"/> (adding RegionKey + the resolved
    /// <see cref="PlacementStatus"/> + candidate-group wiring).
    /// </summary>
    public readonly struct LocationRecord
    {
        /// <summary>Valheim prefab name.</summary>
        public readonly string PrefabName;

        /// <summary>World X (metres).</summary>
        public readonly float X;

        /// <summary>World Z (metres).</summary>
        public readonly float Z;

        /// <summary>True for a unique location (one of N candidate sites; exactly one ever spawns). The
        /// build groups all records sharing a unique prefab into a <see cref="CandidateGroup"/>.</summary>
        public readonly bool IsUnique;

        /// <summary>
        /// True only if a LIVE source has observed this exact site spawned (<c>m_placed</c>). An offline
        /// source always passes false. The build maps true → <see cref="PlacementStatus.Realized"/>.
        /// </summary>
        public readonly bool IsRealized;

        public LocationRecord(string prefabName, float x, float z, bool isUnique, bool isRealized = false)
        {
            this.PrefabName = prefabName;
            this.X = x;
            this.Z = z;
            this.IsUnique = isUnique;
            this.IsRealized = isRealized;
        }
    }
}
