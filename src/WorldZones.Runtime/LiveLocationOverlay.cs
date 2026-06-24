using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The LIVE REALIZATION OVERLAY — lays the running world's STATE over a built <see cref="RegionWorld"/>'s
    /// PLAN. It tracks which planned locations have actually spawned (<see cref="PlacementStatus.Realized"/>)
    /// and pushes events when that changes, so a consumer mod can react the instant a POI — or the winning
    /// site of a unique like Haldor — materializes.
    ///
    /// <para>
    /// This is the difference between "what CAN be here" (the offline/seed plan) and "what IS here, right
    /// now" (the live world). It exists only when a world is running to read: the mod plugin drives it from
    /// a Harmony patch on <c>ZoneSystem.PlaceLocations</c> via <see cref="NotifyRealized"/>. The overlay
    /// itself is Unity-free (it lives in the pure runtime); only the patch that feeds it touches game types.
    /// </para>
    ///
    /// <para>
    /// Snapshot vs push: <see cref="CandidateGroup.Resolved"/> already gives a snapshot answer at build
    /// time. This overlay adds the PUSH — <see cref="OnLocationRealized"/> for any location and
    /// <see cref="OnUniqueResolved"/> the moment a candidate group collapses to its winner. A consumer that
    /// only needs the snapshot can ignore the overlay entirely.
    /// </para>
    /// </summary>
    public sealed class LiveLocationOverlay
    {
        private readonly RegionWorld world;
        // Fast lookup: prefab -> its locations, for matching a realization notification to a planned site.
        private readonly Dictionary<string, List<GazetteerLocation>> byPrefab;
        // Candidate groups already reported resolved, so we fire OnUniqueResolved exactly once each.
        private readonly HashSet<string> resolvedGroups = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Tolerance (metres) for matching a realization position to a planned candidate. The
        /// game realizes at the registered position, so this is tiny — guards float round-trips only.</summary>
        private const float MatchToleranceMeters = 1.0f;

        public LiveLocationOverlay(RegionWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.byPrefab = new Dictionary<string, List<GazetteerLocation>>(StringComparer.Ordinal);
            foreach (var loc in world.AllLocations)
            {
                if (!this.byPrefab.TryGetValue(loc.PrefabName, out var list))
                    this.byPrefab[loc.PrefabName] = list = new List<GazetteerLocation>();
                list.Add(loc);
            }
        }

        /// <summary>The region world this overlay tracks.</summary>
        public RegionWorld World => this.world;

        /// <summary>Raised when a planned location is observed realized on the ground (its zone loaded and
        /// it spawned). Carries the now-<see cref="PlacementStatus.Realized"/> <see cref="GazetteerLocation"/>.
        /// Fires once per location.</summary>
        public event Action<GazetteerLocation> OnLocationRealized;

        /// <summary>Raised when a unique <see cref="CandidateGroup"/> collapses to its winner — i.e. the
        /// first time one of its candidates realizes. Carries the resolved group (its
        /// <see cref="CandidateGroup.RealizedSite"/> is now set). Fires once per group.</summary>
        public event Action<CandidateGroup> OnUniqueResolved;

        /// <summary>
        /// Tell the overlay a location has spawned at (x,z). Called by the mod's
        /// <c>ZoneSystem.PlaceLocations</c> Harmony patch with the realized instance's prefab + position.
        /// Idempotent: re-notifying an already-realized site is a no-op (the game can re-run PlaceLocations
        /// for a zone). Returns the matched location, or null if it didn't correspond to a planned site
        /// (e.g. a location type outside the catalogue the plan was built from).
        /// </summary>
        public GazetteerLocation NotifyRealized(string prefabName, float x, float z)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            if (!this.byPrefab.TryGetValue(prefabName, out var candidates)) return null;

            // Nearest planned site of this prefab within tolerance.
            GazetteerLocation match = null;
            float best = MatchToleranceMeters * MatchToleranceMeters;
            foreach (var loc in candidates)
            {
                float dx = loc.X - x, dz = loc.Z - z;
                float d2 = dx * dx + dz * dz;
                if (d2 <= best) { best = d2; match = loc; }
            }
            if (match == null) return null;

            if (match.Status == PlacementStatus.Realized) return match; // idempotent

            match.Status = PlacementStatus.Realized;
            this.OnLocationRealized?.Invoke(match);

            // If this was a unique candidate, its group just resolved (first realization wins).
            if (match.CandidateGroupKey != null && this.resolvedGroups.Add(match.CandidateGroupKey))
            {
                var group = this.world.CandidateGroups
                    .FirstOrDefault(g => g.PrefabName == match.CandidateGroupKey);
                if (group != null) this.OnUniqueResolved?.Invoke(group);
            }

            return match;
        }

        /// <summary>Count of locations currently observed realized.</summary>
        public int RealizedCount => this.world.AllLocations.Count(l => l.Status == PlacementStatus.Realized);
    }
}
