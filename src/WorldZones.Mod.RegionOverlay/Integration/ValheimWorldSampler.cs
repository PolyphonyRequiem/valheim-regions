using System;
using WorldZones.Runtime;
using WorldZones.WorldGen;

namespace WorldZones.Mod.RegionOverlay.Integration
{
    /// <summary>
    /// <see cref="IWorldSampler"/> backed by the LIVE Valheim <c>WorldGenerator</c>. This is the
    /// game-side counterpart to the headless <see cref="PortWorldSampler"/>: it forwards height and
    /// biome queries to the running game's worldgen so the in-process region build matches what the
    /// player actually walks on.
    ///
    /// <para>
    /// It lives in the mod project (not the pure runtime) precisely because it touches game types —
    /// keeping <c>WorldZones.Runtime</c> free of any Valheim/Unity dependency. The plugin constructs
    /// one of these and hands it to <see cref="WorldZonesRuntime.Build"/>; the runtime never knows it
    /// is talking to the real game.
    /// </para>
    /// </summary>
    public sealed class ValheimWorldSampler : IWorldSampler
    {
        private readonly Func<float, float, float> heightResolver;
        private readonly Func<float, float, BiomeType> biomeResolver;

        /// <summary>
        /// </summary>
        /// <param name="worldId">Stable world identity for naming + persistence. The plugin passes the
        /// numeric world seed (<c>world.m_seed.ToString()</c>) — KEEP IT NUMERIC: discovery-state file
        /// paths are keyed on this, so changing it orphans every player's saved discovery state.</param>
        /// <param name="heightResolver">Forwards to the game's terrain height (world metres).</param>
        /// <param name="biomeResolver">Forwards to the game's biome lookup.</param>
        public ValheimWorldSampler(string worldId,
            Func<float, float, float> heightResolver,
            Func<float, float, BiomeType> biomeResolver)
        {
            this.WorldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
            this.heightResolver = heightResolver ?? throw new ArgumentNullException(nameof(heightResolver));
            this.biomeResolver = biomeResolver ?? throw new ArgumentNullException(nameof(biomeResolver));
        }

        public string WorldId { get; }

        public float GetHeight(float worldX, float worldZ) => this.heightResolver(worldX, worldZ);

        public BiomeType GetBiome(float worldX, float worldZ) => this.biomeResolver(worldX, worldZ);
    }
}
