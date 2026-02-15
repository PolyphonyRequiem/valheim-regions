using System;
using WorldZones.WorldGen;

class QuickOriginTest
{
    static void Main()
    {
        var gen = new WorldGenerator("HHcLC5acQt");
        var biome = gen.GetBiome(0, 0);
        var height = gen.GetBaseHeight(0, 0);

        Console.WriteLine($"Origin (0,0) biome: {biome}");
        Console.WriteLine($"Origin (0,0) height: {height:F4}");
        Console.WriteLine();

        // Test a few known coordinates
        var testCoords = new[] {
            (0f, 0f),
            (100f, 100f),
            (1000f, 1000f),
            (-3476f, 979f),
            (-2692f, 563f),
            (-2099f, 116f)
        };

        foreach (var (x, z) in testCoords)
        {
            var b = gen.GetBiome(x, z);
            var h = gen.GetBaseHeight(x, z);
            Console.WriteLine($"({x,7}, {z,7}): {b,-12} height={h,7:F4}");
        }
    }
}
