using System;
using WorldZones.Regions;

namespace WorldZones.Mod.RegionOverlay.Integration
{
    public sealed class ValheimWorldDataProvider : IWorldDataProvider
    {
        private readonly Func<float, float, float> terrainHeightResolver;

        public ValheimWorldDataProvider(string worldId, Func<float, float, float> terrainHeightResolver, float waterLevel = ZoneClassifier.DefaultWaterLevel)
        {
            this.WorldId = string.IsNullOrWhiteSpace(worldId)
                ? throw new ArgumentException("worldId must not be null or empty", nameof(worldId))
                : worldId;
            this.terrainHeightResolver = terrainHeightResolver ?? throw new ArgumentNullException(nameof(terrainHeightResolver));
            this.WaterLevel = waterLevel;
        }

        public string WorldId { get; }

        public float WaterLevel { get; }

        public float GetTerrainHeight(float worldX, float worldZ)
        {
            return this.terrainHeightResolver(worldX, worldZ);
        }
    }
}