namespace WorldZones.Regions
{
    public interface IWorldDataProvider
    {
        string WorldId { get; }

        float WaterLevel { get; }

        float GetTerrainHeight(float worldX, float worldZ);
    }
}
