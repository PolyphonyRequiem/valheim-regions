using System;
public class QuickCheck {
    public static void Main() {
        var gen = new WorldZones.WorldGen.WorldGenerator(\"1\");
        float x = -2692f;
        float z = 563f;
        var biome = gen.GetBiome(x, z);
        var height = gen.GetBaseHeight(x, z);
        Console.WriteLine($\"Coordinate: ({x}, {z})\");
        Console.WriteLine($\"Biome: {biome}\");
        Console.WriteLine($\"Height: {height:F4}\");
    }
}
