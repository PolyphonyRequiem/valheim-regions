using System;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Represents the environmental zone types present in Valheim.
    /// Uses flags pattern matching Valheim's internal Heightmap.Biome enum.
    /// </summary>
    [Flags]
    public enum BiomeType
    {
        /// <summary>Uninitialized or invalid biome.</summary>
        None = 0,
        
        /// <summary>Starting biome, relatively safe grasslands.</summary>
        Meadows = 1,
        
        /// <summary>Dense forest with trolls and greydwarves.</summary>
        BlackForest = 2,
        
        /// <summary>Dangerous wetlands with draugr and leeches.</summary>
        Swamp = 4,
        
        /// <summary>Cold, high-altitude regions with wolves and drakes.</summary>
        Mountain = 8,
        
        /// <summary>Dangerous flatlands with fulings and deathsquitos.</summary>
        Plains = 16,
        
        /// <summary>Deep water surrounding the world.</summary>
        Ocean = 256,
        
        /// <summary>Misty forests with gjall and seekers.</summary>
        Mistlands = 512,
        
        /// <summary>Volcanic wasteland in the south (as named in Valheim source).</summary>
        AshLands = 1024,
        
        /// <summary>Frozen tundra in the north.</summary>
        DeepNorth = 2048
    }
}
