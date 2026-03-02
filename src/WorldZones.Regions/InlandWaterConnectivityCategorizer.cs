using System;
using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// Classifies water zones as inland or ocean-connected and extracts inland water bodies.
    /// </summary>
    public static class InlandWaterConnectivityCategorizer
    {
        private static readonly (int dx, int dy)[] Neighbors = { (1, 0), (-1, 0), (0, -1), (0, 1) };

        /// <summary>
        /// Builds a per-zone connectivity map and list of connected inland-water bodies.
        /// </summary>
        public static (WaterConnectivityKind[,] connectivityGrid, List<InlandWaterBody> inlandBodies) Categorize(ZoneGrid grid)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            var connectivity = new WaterConnectivityKind[size, size];
            var oceanConnected = new bool[size, size];
            var queue = new Queue<Vector2i>();

            for (int y = min; y <= max; y++)
            {
                for (int x = min; x <= max; x++)
                {
                    if (!IsWater(grid[x, y]))
                    {
                        connectivity[y - min, x - min] = WaterConnectivityKind.NotWater;
                        continue;
                    }

                    if (!IsBoundaryZone(x, y, min, max))
                    {
                        continue;
                    }

                    int gy = y - min;
                    int gx = x - min;
                    if (oceanConnected[gy, gx])
                    {
                        continue;
                    }

                    oceanConnected[gy, gx] = true;
                    queue.Enqueue(new Vector2i(x, y));
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                foreach (var (dx, dy) in Neighbors)
                {
                    int nx = current.x + dx;
                    int ny = current.y + dy;
                    if (nx < min || nx > max || ny < min || ny > max)
                    {
                        continue;
                    }

                    if (!IsWater(grid[nx, ny]))
                    {
                        continue;
                    }

                    int ngy = ny - min;
                    int ngx = nx - min;
                    if (oceanConnected[ngy, ngx])
                    {
                        continue;
                    }

                    oceanConnected[ngy, ngx] = true;
                    queue.Enqueue(new Vector2i(nx, ny));
                }
            }

            var inlandBodies = new List<InlandWaterBody>();
            var inlandVisited = new bool[size, size];
            int waterBodyId = 0;

            for (int y = min; y <= max; y++)
            {
                for (int x = min; x <= max; x++)
                {
                    if (!IsWater(grid[x, y]))
                    {
                        continue;
                    }

                    int gy = y - min;
                    int gx = x - min;
                    if (oceanConnected[gy, gx])
                    {
                        connectivity[gy, gx] = WaterConnectivityKind.OceanConnectedWater;
                        continue;
                    }

                    connectivity[gy, gx] = WaterConnectivityKind.InlandWater;

                    if (inlandVisited[gy, gx])
                    {
                        continue;
                    }

                    var body = new InlandWaterBody
                    {
                        WaterBodyId = waterBodyId++,
                        WaterConnectivity = WaterConnectivityKind.InlandWater
                    };

                    queue.Enqueue(new Vector2i(x, y));
                    inlandVisited[gy, gx] = true;

                    while (queue.Count > 0)
                    {
                        var current = queue.Dequeue();
                        body.Zones.Add(current);

                        foreach (var (dx, dy) in Neighbors)
                        {
                            int nx = current.x + dx;
                            int ny = current.y + dy;
                            if (nx < min || nx > max || ny < min || ny > max)
                            {
                                continue;
                            }

                            if (!IsWater(grid[nx, ny]))
                            {
                                continue;
                            }

                            int ngy = ny - min;
                            int ngx = nx - min;
                            if (oceanConnected[ngy, ngx] || inlandVisited[ngy, ngx])
                            {
                                continue;
                            }

                            inlandVisited[ngy, ngx] = true;
                            connectivity[ngy, ngx] = WaterConnectivityKind.InlandWater;
                            queue.Enqueue(new Vector2i(nx, ny));
                        }
                    }

                    inlandBodies.Add(body);
                }
            }

            return (connectivity, inlandBodies);
        }

        private static bool IsBoundaryZone(int x, int y, int min, int max)
        {
            return x == min || x == max || y == min || y == max;
        }

        private static bool IsWater(DepthClass depth)
        {
            return depth == DepthClass.Shallow || depth == DepthClass.Deep;
        }
    }
}
