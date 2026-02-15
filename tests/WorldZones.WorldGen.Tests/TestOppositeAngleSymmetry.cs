using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class TestOppositeAngleSymmetry
    {
        readonly ITestOutputHelper output;
        
        public TestOppositeAngleSymmetry(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void CompareOppositeAnglesEvery15Degrees()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            int centerX = 4095;
            int centerY = 4095;
            
            this.output.WriteLine("Testing opposite angle symmetry (every 15°)");
            this.output.WriteLine("If centered correctly, opposite angles should have similar R1/R2");
            this.output.WriteLine("");
            this.output.WriteLine("Angle | R1   | R2   | Opposite | R1   | R2   | R1 Diff | R2 Diff");
            this.output.WriteLine("------|------|------|----------|------|------|---------|--------");
            
            var r1Diffs = new List<int>();
            var r2Diffs = new List<int>();
            
            for (int angle = 0; angle < 180; angle += 15)
            {
                int oppositeAngle = angle + 180;
                
                // Skip if either angle is in UI area (225-315)
                bool skipDueToUI = (angle >= 225 && angle <= 315) || (oppositeAngle >= 225 && oppositeAngle <= 315);
                
                var (r1_1, r2_1) = FindR1R2AtAngle(png, centerX, centerY, angle);
                var (r1_2, r2_2) = FindR1R2AtAngle(png, centerX, centerY, oppositeAngle);
                
                int r1Diff = Math.Abs(r1_1 - r1_2);
                int r2Diff = Math.Abs(r2_1 - r2_2);
                
                if (!skipDueToUI)
                {
                    r1Diffs.Add(r1Diff);
                    r2Diffs.Add(r2Diff);
                }
                
                string uiNote = skipDueToUI ? " (UI)" : "";
                this.output.WriteLine($"{angle,3}°  | {r1_1,4} | {r2_1,4} | {oppositeAngle,3}°     | {r1_2,4} | {r2_2,4} | {r1Diff,7} | {r2Diff,7}{uiNote}");
            }
            
            this.output.WriteLine("");
            
            if (r1Diffs.Any() && r2Diffs.Any())
            {
                double avgR1Diff = r1Diffs.Average();
                double avgR2Diff = r2Diffs.Average();
                int maxR1Diff = r1Diffs.Max();
                int maxR2Diff = r2Diffs.Max();
                
                this.output.WriteLine($"Average R1 difference: {avgR1Diff:F2} pixels");
                this.output.WriteLine($"Average R2 difference: {avgR2Diff:F2} pixels");
                this.output.WriteLine($"Max R1 difference: {maxR1Diff} pixels");
                this.output.WriteLine($"Max R2 difference: {maxR2Diff} pixels");
                this.output.WriteLine("");
                
                if (avgR1Diff <= 3 && avgR2Diff <= 3 && maxR1Diff <= 6 && maxR2Diff <= 6)
                {
                    this.output.WriteLine("✓ Excellent symmetry - center is correct!");
                }
                else if (avgR1Diff <= 5 && avgR2Diff <= 5)
                {
                    this.output.WriteLine("✓ Good symmetry - center is likely correct");
                }
                else
                {
                    this.output.WriteLine("✗ Poor symmetry - center might be off");
                }
            }
        }
        
        (int r1, int r2) FindR1R2AtAngle(Bitmap png, int centerX, int centerY, int angle)
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
            
            return (r1, r2);
        }
        
        bool IsBiomeColor(Color c)
        {
            var nearestBiome = GetNearestBiomeColor(c.R, c.G, c.B);
            if (nearestBiome == "Void") return false;
            
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
    }
}
