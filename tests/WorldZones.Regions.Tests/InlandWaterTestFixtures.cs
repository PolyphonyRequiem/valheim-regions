using System.Collections.Generic;
using WorldZones.Regions;

namespace WorldZones.Regions.Tests
{
    internal static class InlandWaterTestFixtures
    {
        public static ZoneGrid SmallGrid()
        {
            return new ZoneGrid(64f);
        }

        public static ZoneGrid MediumGrid()
        {
            return new ZoneGrid(192f);
        }

        public static void FillAll(ZoneGrid grid, DepthClass depth)
        {
            foreach (var coordinate in grid.AllCoords())
            {
                grid[coordinate] = depth;
            }
        }

        public static void FillRect(ZoneGrid grid, int x0, int y0, int x1, int y1, DepthClass depth)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    grid[x, y] = depth;
                }
            }
        }

        public static ProtoRegionResult GenerateLand(
            ZoneGrid grid,
            int targetZonesPerRegion,
            int seedRng,
            out int[,] regionIdGrid,
            out List<Vector2i> seeds,
            InlandWaterAttributionOptions inlandWaterOptions = null,
            int minRegionZones = ProtoRegionGenerator.DefaultMinRegionZones,
            int minComponentZonesForProto = ProtoRegionGenerator.DefaultMinComponentZonesForProto)
        {
            var land = ComponentLabeler.LabelLand(grid, out _);
            return ProtoRegionGenerator.GenerateLand(
                grid,
                land,
                targetZonesPerRegion,
                seedRng,
                out regionIdGrid,
                out seeds,
                minRegionZones,
                minComponentZonesForProto,
                inlandWaterOptions);
        }
    }
}
