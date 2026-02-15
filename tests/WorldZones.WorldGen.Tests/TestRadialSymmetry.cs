using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class TestRadialSymmetry
    {
        readonly ITestOutputHelper output;
        
        public TestRadialSymmetry(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void FindR1AndR2ForMultipleAngles()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            
            // Test EVERY angle (360 total)
            var testAngles = new int[360];
            for (int i = 0; i < 360; i++) testAngles[i] = i;
            
            int minR1 = int.MaxValue;
            int maxR1 = int.MinValue;
            int minR2 = int.MaxValue;
            int maxR2 = int.MinValue;
            
            var allR1Values = new System.Collections.Generic.List<int>();
            var allR2Values = new System.Collections.Generic.List<int>();
            
            this.output.WriteLine("Sample angles:");
            
            foreach (var angle in testAngles)
            {
                double rad = angle * Math.PI / 180.0;
                
                int r1 = -1;
                int r2 = -1;
                
                for (int dist = 3400; dist <= 3600; dist++)
                {
                    int x = centerX + (int)(dist * Math.Cos(rad));
                    int y = centerY + (int)(dist * Math.Sin(rad));
                    
                    if (x < 0 || x >= png.Width || y < 0 || y >= png.Height) continue;
                    
                    var color = png.GetPixel(x, y);
                    bool isBiome = IsBiomeColor(color);
                    bool isVoid = (color.R == 0 && color.G == 0 && color.B == 0);
                    
                    if (isBiome) r1 = dist;
                    if (isVoid && r2 == -1) r2 = dist;
                    if (r2 != -1) break;
                }
                
                // Exclude UI overlay areas (225-315 degrees)
                bool isUIArea = (angle >= 225 && angle <= 315);
                
                if (r1 != -1 && !isUIArea)
                {
                    minR1 = Math.Min(minR1, r1);
                    maxR1 = Math.Max(maxR1, r1);
                    allR1Values.Add(r1);
                }
                
                if (r2 != -1 && !isUIArea)
                {
                    minR2 = Math.Min(minR2, r2);
                    maxR2 = Math.Max(maxR2, r2);
                    allR2Values.Add(r2);
                }
                
                // Print every 30 degrees for sample
                if (angle % 30 == 0)
                {
                    string notes = isUIArea ? " (UI area)" : "";
                    this.output.WriteLine($"  {angle,3}°: R1={r1}, R2={r2}{notes}");
                }
            }
            this.output.WriteLine("");
            
            this.output.WriteLine($"Tested {testAngles.Length} angles");
            this.output.WriteLine("");
            
            // Analyze median 90% (drop top and bottom 5%)
            allR1Values.Sort();
            allR2Values.Sort();
            
            int drop5Count1 = (int)(allR1Values.Count * 0.05);
            int drop5Count2 = (int)(allR2Values.Count * 0.05);
            int drop10Count1 = (int)(allR1Values.Count * 0.10);
            int drop10Count2 = (int)(allR2Values.Count * 0.10);
            int drop25Count1 = (int)(allR1Values.Count * 0.25);
            int drop25Count2 = (int)(allR2Values.Count * 0.25);
            
            var median90R1 = allR1Values.Skip(drop5Count1).Take(allR1Values.Count - 2 * drop5Count1).ToList();
            var median90R2 = allR2Values.Skip(drop5Count2).Take(allR2Values.Count - 2 * drop5Count2).ToList();
            var median80R1 = allR1Values.Skip(drop10Count1).Take(allR1Values.Count - 2 * drop10Count1).ToList();
            var median80R2 = allR2Values.Skip(drop10Count2).Take(allR2Values.Count - 2 * drop10Count2).ToList();
            var median50R1 = allR1Values.Skip(drop25Count1).Take(allR1Values.Count - 2 * drop25Count1).ToList();
            var median50R2 = allR2Values.Skip(drop25Count2).Take(allR2Values.Count - 2 * drop25Count2).ToList();
            
            int median90MinR1 = median90R1.Any() ? median90R1.Min() : 0;
            int median90MaxR1 = median90R1.Any() ? median90R1.Max() : 0;
            int median90MinR2 = median90R2.Any() ? median90R2.Min() : 0;
            int median90MaxR2 = median90R2.Any() ? median90R2.Max() : 0;
            
            int median80MinR1 = median80R1.Any() ? median80R1.Min() : 0;
            int median80MaxR1 = median80R1.Any() ? median80R1.Max() : 0;
            int median80MinR2 = median80R2.Any() ? median80R2.Min() : 0;
            int median80MaxR2 = median80R2.Any() ? median80R2.Max() : 0;
            
            int median50MinR1 = median50R1.Any() ? median50R1.Min() : 0;
            int median50MaxR1 = median50R1.Any() ? median50R1.Max() : 0;
            int median50MinR2 = median50R2.Any() ? median50R2.Min() : 0;
            int median50MaxR2 = median50R2.Any() ? median50R2.Max() : 0;
            
            this.output.WriteLine($"R1 range (excluding UI): {minR1} to {maxR1} (variation: {maxR1 - minR1} pixels)");
            this.output.WriteLine($"R2 range (excluding UI): {minR2} to {maxR2} (variation: {maxR2 - minR2} pixels)");
            this.output.WriteLine("");
            this.output.WriteLine($"Median 90% R1: {median90MinR1} to {median90MaxR1} (variation: {median90MaxR1 - median90MinR1} pixels)");
            this.output.WriteLine($"Median 90% R2: {median90MinR2} to {median90MaxR2} (variation: {median90MaxR2 - median90MinR2} pixels)");
            this.output.WriteLine("");
            this.output.WriteLine($"Median 80% R1: {median80MinR1} to {median80MaxR1} (variation: {median80MaxR1 - median80MinR1} pixels)");
            this.output.WriteLine($"Median 80% R2: {median80MinR2} to {median80MaxR2} (variation: {median80MaxR2 - median80MinR2} pixels)");
            this.output.WriteLine("");
            this.output.WriteLine($"Median 50% R1: {median50MinR1} to {median50MaxR1} (variation: {median50MaxR1 - median50MinR1} pixels)");
            this.output.WriteLine($"Median 50% R2: {median50MinR2} to {median50MaxR2} (variation: {median50MaxR2 - median50MinR2} pixels)");
            this.output.WriteLine("");
            
            // Group R1 values by their actual value and show which angles
            this.output.WriteLine("R1 Distribution by angle:");
            var r1Groups = allR1Values.GroupBy(x => x).OrderBy(g => g.Key);
            foreach (var group in r1Groups)
            {
                var anglesWithThisR1 = new System.Collections.Generic.List<int>();
                for (int i = 0; i < 360; i++)
                {
                    if (i >= 225 && i <= 315) continue; // Skip UI
                    
                    double rad = i * Math.PI / 180.0;
                    int r1Test = -1;
                    
                    for (int dist = 3400; dist <= 3600; dist++)
                    {
                        int x = centerX + (int)(dist * Math.Cos(rad));
                        int y = centerY + (int)(dist * Math.Sin(rad));
                        if (x < 0 || x >= png.Width || y < 0 || y >= png.Height) continue;
                        
                        var color = png.GetPixel(x, y);
                        if (IsBiomeColor(color)) r1Test = dist;
                    }
                    
                    if (r1Test == group.Key) anglesWithThisR1.Add(i);
                }
                
                string angleRanges = DescribeAngleRanges(anglesWithThisR1);
                this.output.WriteLine($"  R1={group.Key}: {group.Count()} angles - {angleRanges}");
            }
            this.output.WriteLine("");
            
            if (median50MaxR1 - median50MinR1 <= 3 && median50MaxR2 - median50MinR2 <= 3)
            {
                this.output.WriteLine("✓ Median 50% variation ≤3 pixels!");
            }
            else if (median80MaxR1 - median80MinR1 <= 3 && median80MaxR2 - median80MinR2 <= 3)
            {
                this.output.WriteLine("✓ Median 80% variation ≤3 pixels!");
            }
            else if (median90MaxR1 - median90MinR1 <= 3 && median90MaxR2 - median90MinR2 <= 3)
            {
                this.output.WriteLine("✓ Median 90% variation ≤3 pixels!");
            }
            else if (maxR1 - minR1 <= 3 && maxR2 - minR2 <= 3)
            {
                this.output.WriteLine("✓ All angles variation ≤3 pixels!");
            }
            else
            {
                this.output.WriteLine("✗ Variation >3 pixels in all buckets");
            }
        }
        
        bool IsBiomeColor(Color c)
        {
            // Use nearest color matching instead of tolerance
            var nearestBiome = GetNearestBiomeColor(c.R, c.G, c.B);
            
            // If it's void, not a biome
            if (nearestBiome == "Void") return false;
            
            // If distance to nearest biome is reasonable (not just random noise)
            // Max distance of ~150 covers gradients but excludes void/far-off colors
            double distance = GetColorDistance(c.R, c.G, c.B, nearestBiome);
            return distance < 150;
        }
        
        string GetNearestBiomeColor(int r, int g, int b)
        {
            var biomeColors = new Dictionary<string, (int r, int g, int b)>
            {
                { "Void", (0, 0, 0) },
                { "Ocean", (0, 0, 153) },
                { "Shallows", (102, 102, 255) },
                { "BlackForest", (52, 94, 59) },
                { "Meadows", (145, 167, 91) },
                { "Mistlands", (82, 82, 82) },
                { "Mountain", (255, 255, 255) },
                { "Plains", (199, 199, 49) },
                { "Swamp", (163, 113, 87) },
                { "AshLands", (255, 0, 0) }
            };
            
            string nearest = "Void";
            double minDistance = double.MaxValue;
            
            foreach (var kvp in biomeColors)
            {
                double dist = Math.Sqrt(
                    Math.Pow(r - kvp.Value.r, 2) +
                    Math.Pow(g - kvp.Value.g, 2) +
                    Math.Pow(b - kvp.Value.b, 2)
                );
                
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearest = kvp.Key;
                }
            }
            
            return nearest;
        }
        
        double GetColorDistance(int r, int g, int b, string biomeName)
        {
            var biomeColors = new Dictionary<string, (int r, int g, int b)>
            {
                { "Void", (0, 0, 0) },
                { "Ocean", (0, 0, 153) },
                { "Shallows", (102, 102, 255) },
                { "BlackForest", (52, 94, 59) },
                { "Meadows", (145, 167, 91) },
                { "Mistlands", (82, 82, 82) },
                { "Mountain", (255, 255, 255) },
                { "Plains", (199, 199, 49) },
                { "Swamp", (163, 113, 87) },
                { "AshLands", (255, 0, 0) }
            };
            
            var biomeRgb = biomeColors[biomeName];
            return Math.Sqrt(
                Math.Pow(r - biomeRgb.r, 2) +
                Math.Pow(g - biomeRgb.g, 2) +
                Math.Pow(b - biomeRgb.b, 2)
            );
        }
        
        string DescribeAngleRanges(System.Collections.Generic.List<int> angles)
        {
            if (angles.Count == 0) return "none";
            if (angles.Count <= 5) return string.Join(",", angles) + "°";
            
            // Find contiguous ranges
            var ranges = new System.Collections.Generic.List<string>();
            int rangeStart = angles[0];
            int prev = angles[0];
            
            for (int i = 1; i < angles.Count; i++)
            {
                if (angles[i] - prev > 5) // Gap of more than 5 degrees = new range
                {
                    if (prev - rangeStart > 5)
                        ranges.Add($"{rangeStart}-{prev}°");
                    else
                        ranges.Add($"{rangeStart}°");
                    rangeStart = angles[i];
                }
                prev = angles[i];
            }
            
            // Add final range
            if (prev - rangeStart > 5)
                ranges.Add($"{rangeStart}-{prev}°");
            else
                ranges.Add($"{rangeStart}°");
            
            return string.Join(", ", ranges);
        }
    }
}
