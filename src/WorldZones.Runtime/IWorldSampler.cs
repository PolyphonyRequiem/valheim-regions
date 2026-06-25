using System;
using WorldZones.WorldGen;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The single seam a consumer supplies to get regions for a world. It answers the only three
    /// questions the region pipeline + rich aggregation ask of the world:
    /// <list type="bullet">
    ///   <item><see cref="WorldId"/> — stable per-world identity that names + persistence key off.</item>
    ///   <item><see cref="GetHeight"/> — terrain height in world metres (drives land/water classification).</item>
    ///   <item><see cref="GetBiome"/> — biome at a world coordinate (drives region character + naming).</item>
    /// </list>
    ///
    /// <para>
    /// This is the substrate's boundary. A consumer never re-implements the ~70-line bootstrap that
    /// used to be copy-pasted across the overlay plugin, the CLI, and the gazetteer: they implement
    /// (or reuse) <see cref="IWorldSampler"/> and call <see cref="WorldZonesRuntime.Build"/>.
    /// </para>
    ///
    /// <para>
    /// Two implementations ship: <see cref="PortWorldSampler"/> wraps the verified offline port
    /// (headless — CLI, tests, dataset export). A Valheim-backed sampler that forwards to the live
    /// <c>WorldGenerator</c> lives in the mod project (it needs the game assemblies); it implements
    /// this same interface so the pure runtime never takes a Unity dependency.
    /// </para>
    /// </summary>
    public interface IWorldSampler
    {
        /// <summary>Stable identity of the world. Names + persistence derive from this; the same
        /// world MUST yield the same WorldId across the mod, the CLI, and any consumer, or region
        /// names silently diverge between what a player sees and what a dataset publishes.</summary>
        string WorldId { get; }

        /// <summary>Terrain height in world metres at the given world coordinate.</summary>
        float GetHeight(float worldX, float worldZ);

        /// <summary>Biome at the given world coordinate.</summary>
        BiomeType GetBiome(float worldX, float worldZ);
    }

    /// <summary>
    /// Optional capability a sampler MAY implement to expose river proximity — the crisp border
    /// feature that is otherwise invisible to region growth (rivers only enter <see cref="IWorldSampler.GetHeight"/>
    /// as a carved bed, which the biome/shore cost field never reads). When a sampler implements this,
    /// <see cref="RegionCostFieldBuilder"/> can add river walls to the cost field. The offline
    /// <see cref="PortWorldSampler"/> implements it (forwarding to the port's pregenerated rivers); a
    /// live Valheim sampler forwards to <c>WorldGenerator.GetRiverWeight</c>. Samplers that don't care
    /// about rivers simply don't implement it — the feature degrades off, no break.
    /// </summary>
    public interface IRiverSampler
    {
        /// <summary>River proximity weight (0 = none, →1 at centre) + local width (m) at a world point.</summary>
        void GetRiverWeight(float worldX, float worldZ, out float weight, out float width);
    }

    /// <summary>
    /// <see cref="IWorldSampler"/> backed by the verified offline worldgen port
    /// (<see cref="WorldGenerator"/>). This is the headless sampler — it needs no Valheim/Unity
    /// assemblies, so it powers the CLI, the tests, the gazetteer export, and any offline consumer.
    ///
    /// <para>
    /// Height is resolved as <c>GetBiomeHeight(GetBiome(x,z), x,z)</c> — identical to the legacy
    /// <c>StandaloneWorldDataProvider</c> the gazetteer used, so region geometry is byte-for-byte
    /// unchanged by routing through the runtime.
    /// </para>
    /// </summary>
    public sealed class PortWorldSampler : IWorldSampler, IRiverSampler
    {
        private readonly WorldGenerator worldGenerator;

        /// <summary>
        /// Wraps a <see cref="WorldGenerator"/>. <paramref name="worldId"/> defaults to the seed
        /// string the generator was constructed from — pass it explicitly only to override the
        /// identity used for naming/persistence (rarely needed).
        /// </summary>
        public PortWorldSampler(WorldGenerator worldGenerator, string worldId)
        {
            this.worldGenerator = worldGenerator ?? throw new ArgumentNullException(nameof(worldGenerator));
            this.WorldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
        }

        /// <summary>Convenience: build a sampler straight from a seed string. The seed doubles as the
        /// WorldId, which is the correct, collision-free identity for an offline/dataset context.</summary>
        public static PortWorldSampler FromSeed(string seed)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed));
            return new PortWorldSampler(new WorldGenerator(seed), seed);
        }

        public string WorldId { get; }

        public float GetHeight(float worldX, float worldZ)
        {
            BiomeType biome = this.worldGenerator.GetBiome(worldX, worldZ);
            return this.worldGenerator.GetBiomeHeight(biome, worldX, worldZ);
        }

        public BiomeType GetBiome(float worldX, float worldZ)
        {
            return this.worldGenerator.GetBiome(worldX, worldZ);
        }

        /// <summary>River proximity at a world point — forwards to the port's pregenerated rivers.</summary>
        public void GetRiverWeight(float worldX, float worldZ, out float weight, out float width)
        {
            this.worldGenerator.GetRiverWeightPublic(worldX, worldZ, out weight, out width);
        }
    }
}
