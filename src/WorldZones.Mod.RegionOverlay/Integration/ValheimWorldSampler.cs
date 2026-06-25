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
    public sealed class ValheimWorldSampler : IWorldSampler, IRiverSampler
    {
        private readonly Func<float, float, float> heightResolver;
        private readonly Func<float, float, BiomeType> biomeResolver;
        private readonly RiverResolver riverResolver;

        /// <summary>Delegate shape for river proximity (out params can't go through Func).</summary>
        public delegate void RiverResolver(float worldX, float worldZ, out float weight, out float width);

        /// <summary>
        /// </summary>
        /// <param name="worldId">Stable world identity for naming + persistence. The plugin passes the
        /// numeric world seed (<c>world.m_seed.ToString()</c>) — KEEP IT NUMERIC: discovery-state file
        /// paths are keyed on this, so changing it orphans every player's saved discovery state.</param>
        /// <param name="heightResolver">Forwards to the game's terrain height (world metres).</param>
        /// <param name="biomeResolver">Forwards to the game's biome lookup.</param>
        /// <param name="riverResolver">Optional — forwards to the game's <c>WorldGenerator.GetRiverWeight</c>
        /// so feature-aware borders can use rivers as a seam IN-GAME, matching the headless gazetteer
        /// (which uses <see cref="PortWorldSampler"/>'s river capability). When null, rivers are simply
        /// not used in-game — a graceful degrade, but pass it so the dataset and what players walk MATCH.</param>
        public ValheimWorldSampler(string worldId,
            Func<float, float, float> heightResolver,
            Func<float, float, BiomeType> biomeResolver,
            RiverResolver riverResolver = null)
        {
            this.WorldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
            this.heightResolver = heightResolver ?? throw new ArgumentNullException(nameof(heightResolver));
            this.biomeResolver = biomeResolver ?? throw new ArgumentNullException(nameof(biomeResolver));
            this.riverResolver = riverResolver;
        }

        public string WorldId { get; }

        public float GetHeight(float worldX, float worldZ) => this.heightResolver(worldX, worldZ);

        public BiomeType GetBiome(float worldX, float worldZ) => this.biomeResolver(worldX, worldZ);

        /// <summary>River proximity — forwards to the game's pregenerated rivers when a resolver was
        /// supplied; otherwise reports "no river" (weight 0) so feature-aware borders fall back to
        /// biome/shore only.</summary>
        public void GetRiverWeight(float worldX, float worldZ, out float weight, out float width)
        {
            if (this.riverResolver != null) this.riverResolver(worldX, worldZ, out weight, out width);
            else { weight = 0f; width = 0f; }
        }
    }
}
