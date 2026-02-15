using System;
using System.Drawing;

class QuickMapReader
{
    static void Main(string[] args)
    {
        var mapPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
        using var bitmap = new Bitmap(mapPath);
        
        Console.WriteLine($"Map size: {bitmap.Width}x{bitmap.Height}");
        Console.WriteLine("");
        
        float worldSize = 21000f;
        float pixelToWorld = worldSize / bitmap.Width;
        
        CheckCoord(bitmap, 0, 0, pixelToWorld);
        CheckCoord(bitmap, -686, 1744, pixelToWorld);
        CheckCoord(bitmap, 2564, -1189, pixelToWorld);
    }
    
    static void CheckCoord(Bitmap bitmap, float worldX, float worldZ, float pixelToWorld)
    {
        int px = (int)((worldX / pixelToWorld) + bitmap.Width / 2f);
        int py = (int)((worldZ / pixelToWorld) + bitmap.Height / 2f);
        
        var pixel = bitmap.GetPixel(px, py);
        
        Console.WriteLine($"World ({worldX,6:F0}, {worldZ,6:F0}) -> Pixel ({px,4}, {py,4}) = RGB({pixel.R,3}, {pixel.G,3}, {pixel.B,3})");
        Console.WriteLine($"  Looks like: {GuessColor(pixel)}");
        Console.WriteLine("");
    }
    
    static string GuessColor(Color c)
    {
        if (c.R < 20 && c.G < 20 && c.B > 100) return "OCEAN (dark blue)";
        if (c.R > 240 && c.G > 240 && c.B > 240) return "MOUNTAIN (white)";
        if (c.R > 180 && c.G > 180 && c.B < 100) return "MEADOWS (yellow-green)";
        if (c.R < 100 && c.G > 50 && c.B < 100) return "BLACKFOREST (dark green)";
        if (c.R > 150 && c.G > 80 && c.B < 120) return "SWAMP (brown)";
        if (c.R > 180 && c.G > 180 && c.B < 50) return "PLAINS (yellow)";
        if (c.R > 80 && c.R < 150 && c.G > 80 && c.G < 150 && c.B > 80 && c.B < 150) return "MISTLANDS (gray)";
        if (c.R > 100 && c.G > 150 && c.B > 200) return "SHALLOWS (light blue)";
        return "UNKNOWN";
    }
}
