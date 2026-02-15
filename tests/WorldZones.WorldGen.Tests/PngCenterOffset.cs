using System;
using System.Drawing;
using System.IO;
using Xunit;
using Xunit.Abstractions;

namespace WorldZones.WorldGen.Tests
{
    public class PngCenterOffset
    {
        readonly ITestOutputHelper output;
        
        public PngCenterOffset(ITestOutputHelper output)
        {
            this.output = output;
        }
        
        [Fact]
        public void FindCenterOffsetDirection()
        {
            var pngPath = @"C:\Users\dangreen\projects\valheim\worldzones\data\seeds\HHcLC5acQt\Map_HHcLC5acQt.png";
            if (!File.Exists(pngPath)) return;
            
            using var png = new Bitmap(pngPath);
            
            this.output.WriteLine("FINDING CENTER OFFSET BY ANGLE");
            this.output.WriteLine("===============================");
            this.output.WriteLine($"Testing center: ({png.Width/2}, {png.Height/2})");
            this.output.WriteLine("");
            
            int centerX = png.Width / 2;  // Back to original
            int centerY = png.Height / 2;
            int numAngles = 36;
            
            // For each angle, find at what radius it stops being a biome color
            int[] transitionRadius = new int[numAngles];
            
            for (int i = 0; i < numAngles; i++)
            {
                double angleDeg = i * 360.0 / numAngles;
                double angleRad = angleDeg * Math.PI / 180.0;
                
                // Find transition radius for this angle
                for (int radius = 3400; radius < 3600; radius++)
                {
                    int px = centerX + (int)(radius * Math.Cos(angleRad));
                    int py = centerY + (int)(radius * Math.Sin(angleRad));
                    
                    if (px < 0 || px >= png.Width || py < 0 || py >= png.Height)
                    {
                        transitionRadius[i] = radius;
                        break;
                    }
                    
                    var pixel = png.GetPixel(px, py);
                    
                    if (!IsBiomeColor(pixel))
                    {
                        transitionRadius[i] = radius;
                        break;
                    }
                }
                
                // Show ALL directions for analysis
                if (i % 1 == 0)  // Every angle
                {
                    this.output.WriteLine($"Angle {angleDeg,3:F0}° ({GetDirection(angleDeg),-5}): transition at radius {transitionRadius[i]}");
                }
            }
            
            // Find min and max
            int minRadius = int.MaxValue;
            int maxRadius = int.MinValue;
            int minAngleIndex = -1;
            int maxAngleIndex = -1;
            
            for (int i = 0; i < numAngles; i++)
            {
                if (transitionRadius[i] < minRadius)
                {
                    minRadius = transitionRadius[i];
                    minAngleIndex = i;
                }
                if (transitionRadius[i] > maxRadius)
                {
                    maxRadius = transitionRadius[i];
                    maxAngleIndex = i;
                }
            }
            
            double minAngleDeg = minAngleIndex * 360.0 / numAngles;
            double maxAngleDeg = maxAngleIndex * 360.0 / numAngles;
            
            this.output.WriteLine("");
            this.output.WriteLine("ANALYSIS:");
            this.output.WriteLine($"  Earliest transition: {minRadius}px at angle {minAngleDeg:F0}° ({GetDirection(minAngleDeg)})");
            this.output.WriteLine($"  Latest transition:   {maxRadius}px at angle {maxAngleDeg:F0}° ({GetDirection(maxAngleDeg)})");
            this.output.WriteLine($"  Difference: {maxRadius - minRadius} pixels");
            this.output.WriteLine("");
            
            // The center is offset TOWARD the direction that transitions latest
            this.output.WriteLine("CONCLUSION:");
            this.output.WriteLine($"  The center is offset toward {GetDirection(maxAngleDeg)}");
            this.output.WriteLine($"  (because that direction runs into the edge latest)");
            this.output.WriteLine("");
            this.output.WriteLine($"  To fix: shift center ~{(maxRadius - minRadius)/2} pixels toward {GetDirection((maxAngleDeg + 180) % 360)}");
        }
        
        string GetDirection(double angleDeg)
        {
            if (angleDeg < 22.5 || angleDeg >= 337.5) return "East";
            if (angleDeg < 67.5) return "NE";
            if (angleDeg < 112.5) return "North";
            if (angleDeg < 157.5) return "NW";
            if (angleDeg < 202.5) return "West";
            if (angleDeg < 247.5) return "SW";
            if (angleDeg < 292.5) return "South";
            if (angleDeg < 337.5) return "SE";
            return "?";
        }
        
        bool IsBiomeColor(Color c)
        {
            int tolerance = 3;
            
            if (IsColor(c, 0, 0, 153, tolerance)) return true;
            if (IsColor(c, 102, 102, 255, tolerance)) return true;
            if (IsColor(c, 52, 94, 59, tolerance)) return true;
            if (IsColor(c, 145, 167, 91, tolerance)) return true;
            if (IsColor(c, 82, 82, 82, tolerance)) return true;
            if (IsColor(c, 255, 255, 255, tolerance)) return true;
            if (IsColor(c, 199, 199, 49, tolerance)) return true;
            if (IsColor(c, 163, 113, 87, tolerance)) return true;
            if (IsColor(c, 255, 0, 0, tolerance)) return true;
            
            return false;
        }
        
        bool IsColor(Color c, int r, int g, int b, int tolerance)
        {
            return Math.Abs(c.R - r) <= tolerance && 
                   Math.Abs(c.G - g) <= tolerance && 
                   Math.Abs(c.B - b) <= tolerance;
        }
    }
}
