using System;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Where a gazetteer location stands between the world's PLAN (registration, computed at worldgen
    /// from the seed) and the world's STATE (realization, decided at runtime when a zone first loads).
    /// See <c>docs/design/location-port.md</c> for the two-phase model.
    /// </summary>
    public enum PlacementStatus
    {
        /// <summary>
        /// A normal (non-unique) location in the world's plan. It is GUARANTEED to spawn the moment its
        /// zone first loads — the only reason it isn't on the ground yet is that nobody has been there.
        /// Offline/from-seed, every non-unique location is Registered.
        /// </summary>
        Registered = 0,

        /// <summary>
        /// One of N candidate sites for a UNIQUE location (Haldor the trader, the PlaceOfMystery sites,
        /// Hildir's camp). At most ONE site in the <see cref="CandidateGroup"/> ever spawns; WHICH one is
        /// decided at runtime by exploration order, NOT by the seed — so neither the game nor this library
        /// can predict the winner in advance. All candidates report this status until one is realized.
        /// </summary>
        Candidate = 1,

        /// <summary>
        /// Confirmed on the ground (<c>m_placed == true</c> in the live world). This is the "actually
        /// generated into the world" signal, and it applies to BOTH a realized normal location and the
        /// winning candidate of a unique group. Only a live (in-game) source can report this — an offline
        /// build never sees realization, so it emits only <see cref="Registered"/> / <see cref="Candidate"/>.
        /// </summary>
        Realized = 2,
    }

    /// <summary>
    /// One location in the gazetteer — a prefab placed (or potentially placed) at a world position,
    /// joined to the region that contains it. The unit a modder consumes via the location API.
    /// </summary>
    public sealed class GazetteerLocation
    {
        /// <summary>Valheim prefab name (e.g. "Crypt4", "Vendor_BlackForest", "Eikthyrnir").</summary>
        public string PrefabName { get; set; } = "";

        /// <summary>World X (metres). Fixed at registration; realization never moves it.</summary>
        public float X { get; set; }

        /// <summary>World Z (metres). Fixed at registration; realization never moves it.</summary>
        public float Z { get; set; }

        /// <summary>Durable key of the region containing this location, or null if it falls outside any
        /// region (ocean / islet). Joined via <see cref="RegionWorld.RegionAt(float,float)"/>.</summary>
        public string RegionKey { get; set; }

        /// <summary>Registered vs Candidate vs Realized — see <see cref="PlacementStatus"/>.</summary>
        public PlacementStatus Status { get; set; }

        /// <summary>
        /// For a <see cref="PlacementStatus.Candidate"/> (or the realized winner of a unique group), the
        /// prefab name that keys its <see cref="CandidateGroup"/>. Null for a plain Registered location.
        /// Lets a consumer tie "these N sites resolve to 1" together.
        /// </summary>
        public string CandidateGroupKey { get; set; }

        /// <summary>True for a unique-location candidate or its realized winner; false for a normal
        /// location. Convenience over <c>CandidateGroupKey != null</c>.</summary>
        public bool IsUnique => this.CandidateGroupKey != null;
    }
}
