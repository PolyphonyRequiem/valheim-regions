using WorldZones.Regions;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    public sealed class StandaloneWorldDataProvider : IWorldDataProvider
    {
        private readonly WorldGenerator worldGenerator;

        public StandaloneWorldDataProvider(string worldId, WorldGenerator worldGenerator)
        {
            this.WorldId = worldId;
            this.worldGenerator = worldGenerator;
        }

        public string WorldId { get; }

        public float WaterLevel => ZoneClassifier.DefaultWaterLevel;

        public float GetTerrainHeight(float worldX, float worldZ)
        {
            var biome = this.worldGenerator.GetBiome(worldX, worldZ);
            return this.worldGenerator.GetBiomeHeight(biome, worldX, worldZ);
        }
    }
}