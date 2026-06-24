using System;
using System.Collections.Generic;
using WorldZones.WorldGen;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The OFFLINE <see cref="ILocationSource"/> — runs the verified <c>LocationModel</c> port from the
    /// seed to produce the registration plan headlessly (no game, no save, no walk). This is the
    /// location analogue of <see cref="PortWorldSampler"/>.
    ///
    /// <para>
    /// It emits the world's PLAN only: normal locations as <see cref="PlacementStatus.Registered"/>,
    /// unique locations as <see cref="PlacementStatus.Candidate"/>. It can never report
    /// <see cref="PlacementStatus.Realized"/> — realization is a runtime fact, and a live source
    /// (the mod plugin reading <c>m_locationInstances</c>) owns that overlay.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Fidelity.</b> The port is bit-exact in RNG but its terrain is ~99.85% vs the game, so on
    /// the handful of terrain-starved types whose quota the game itself can't fill, the count can differ
    /// by a few and individual sites may swap within a locale. Region-level membership is unaffected for
    /// the overwhelming majority. Treat offline location data as <c>source: computed</c>; prefer a live
    /// source or a decoded <c>.db</c> when exact ground truth matters. See docs/design/location-port.md.
    /// </para>
    /// </summary>
    public sealed class PortLocationSource : ILocationSource
    {
        private readonly int worldSeed;
        private readonly WorldGenerator generator;
        private readonly IReadOnlyList<LocationModel.LocationConfig> catalogue;
        private readonly InsideUnitCircleStrategy strategy;

        /// <summary>
        /// Build a source from a worldgen + an extracted ZoneLocation catalogue.
        /// </summary>
        /// <param name="worldSeed">The world seed hash (= <c>seedName.GetStableHashCode()</c>, the value
        /// <c>World.m_seed</c> holds). Drives the per-type placement RNG.</param>
        /// <param name="generator">The verified worldgen port (supplies biome/height/forest/terrain-delta).</param>
        /// <param name="catalogue">Extracted location configs (data/valheim_locations_catalogue.json via
        /// tools/locations/parse_locations.py). Do not hand-author.</param>
        /// <param name="strategy">insideUnitCircle draw pattern; default polar (empirically the faithful one).</param>
        public PortLocationSource(
            int worldSeed,
            WorldGenerator generator,
            IReadOnlyList<LocationModel.LocationConfig> catalogue,
            InsideUnitCircleStrategy strategy = InsideUnitCircleStrategy.PolarRadiusFirst)
        {
            this.worldSeed = worldSeed;
            this.generator = generator ?? throw new ArgumentNullException(nameof(generator));
            this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
            this.strategy = strategy;
        }

        /// <summary>
        /// Convenience: build straight from a seed string + catalogue. The seed is hashed with Valheim's
        /// stable hash, exactly as <c>World.m_seed</c> is derived, so placement matches the game's RNG.
        /// </summary>
        public static PortLocationSource FromSeed(
            string seed,
            IReadOnlyList<LocationModel.LocationConfig> catalogue,
            InsideUnitCircleStrategy strategy = InsideUnitCircleStrategy.PolarRadiusFirst)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            return new PortLocationSource(seed.GetStableHashCode(), new WorldGenerator(seed), catalogue, strategy);
        }

        /// <inheritdoc />
        public IEnumerable<LocationRecord> EnumerateLocations()
        {
            // Prefab -> isUnique, from the catalogue (the port's PlacedLocation drops the flag).
            var uniqueByPrefab = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var c in this.catalogue)
                if (!string.IsNullOrEmpty(c.PrefabName))
                    uniqueByPrefab[c.PrefabName] = c.Unique;

            var placed = LocationModel.Generate(this.worldSeed, this.generator, this.catalogue, this.strategy);
            foreach (var p in placed)
            {
                bool isUnique = uniqueByPrefab.TryGetValue(p.PrefabName, out var u) && u;
                // Offline never knows realization -> isRealized always false.
                yield return new LocationRecord(p.PrefabName, p.X, p.Z, isUnique, isRealized: false);
            }
        }
    }
}
