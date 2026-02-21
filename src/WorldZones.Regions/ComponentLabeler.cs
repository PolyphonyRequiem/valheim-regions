using System;
using System.Collections.Generic;
using WorldZones.WorldGen;

namespace WorldZones.Regions
{
    /// <summary>
    /// Result of connected-component labeling for land zones.
    /// </summary>
    public class LandComponent
    {
        /// <summary>Zero-based component identifier.</summary>
        public int Id { get; }

        /// <summary>All zone coordinates belonging to this component.</summary>
        public List<Vector2i> Zones { get; } = new List<Vector2i>();

        public LandComponent(int id) => Id = id;
    }

    /// <summary>
    /// Flood-fill connected-component analysis over a classified <see cref="ZoneGrid"/>.
    /// Uses 4-neighbor adjacency (N/S/E/W).
    /// </summary>
    public static class ComponentLabeler
    {
        private static readonly (int dx, int dy)[] Neighbors = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>
        /// Labels all connected land components (DepthClass.Land) in the grid.
        /// Returns a list of components sorted by descending zone count.
        /// Also populates <paramref name="labelGrid"/> (same dimensions as grid)
        /// with per-zone component IDs (-1 for non-land zones).
        /// </summary>
        public static List<LandComponent> LabelLand(ZoneGrid grid, out int[,] labelGrid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            labelGrid = new int[size, size];
            // Initialize all to -1 (unlabeled)
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    labelGrid[y, x] = -1;

            var components = new List<LandComponent>();
            int nextId = 0;

            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    int gy = zy - min;
                    int gx = zx - min;

                    if (grid[zx, zy] != DepthClass.Land)
                        continue;
                    if (labelGrid[gy, gx] >= 0)
                        continue;

                    // BFS flood fill
                    var component = new LandComponent(nextId);
                    var queue = new Queue<Vector2i>();
                    var start = new Vector2i(zx, zy);
                    queue.Enqueue(start);
                    labelGrid[gy, gx] = nextId;

                    while (queue.Count > 0)
                    {
                        var cur = queue.Dequeue();
                        component.Zones.Add(cur);

                        foreach (var (dx, dy) in Neighbors)
                        {
                            int nx = cur.x + dx;
                            int ny = cur.y + dy;

                            if (nx < min || nx > max || ny < min || ny > max)
                                continue;

                            int ngy = ny - min;
                            int ngx = nx - min;

                            if (labelGrid[ngy, ngx] >= 0)
                                continue;
                            if (grid[nx, ny] != DepthClass.Land)
                                continue;

                            labelGrid[ngy, ngx] = nextId;
                            queue.Enqueue(new Vector2i(nx, ny));
                        }
                    }

                    components.Add(component);
                    nextId++;
                }
            }

            // Sort by descending zone count (largest component first)
            components.Sort((a, b) => b.Zones.Count.CompareTo(a.Zones.Count));

            return components;
        }

        /// <summary>
        /// Labels all connected shelf components (DepthClass.Land ∪ DepthClass.Shallow)
        /// in the grid using 4-neighbor adjacency.
        /// Each shelf is mapped to the <see cref="LandComponent"/>s it contains via
        /// <paramref name="landLabelGrid"/> (produced by <see cref="LabelLand"/>).
        /// Returns components sorted by descending zone count.
        /// </summary>
        public static List<ShelfComponent> LabelShelf(
            ZoneGrid grid,
            int[,] landLabelGrid,
            out int[,] shelfLabelGrid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (landLabelGrid == null)
                throw new ArgumentNullException(nameof(landLabelGrid));

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            shelfLabelGrid = new int[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    shelfLabelGrid[y, x] = -1;

            var components = new List<ShelfComponent>();
            int nextId = 0;

            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    int gy = zy - min;
                    int gx = zx - min;

                    var dc = grid[zx, zy];
                    if (dc != DepthClass.Land && dc != DepthClass.Shallow)
                        continue;
                    if (shelfLabelGrid[gy, gx] >= 0)
                        continue;

                    // BFS flood fill over Land ∪ Shallow
                    var component = new ShelfComponent(nextId);
                    var landIds = new HashSet<int>();
                    var queue = new Queue<Vector2i>();
                    queue.Enqueue(new Vector2i(zx, zy));
                    shelfLabelGrid[gy, gx] = nextId;

                    while (queue.Count > 0)
                    {
                        var cur = queue.Dequeue();
                        component.Zones.Add(cur);

                        // Track which land components are inside this shelf
                        int cgy = cur.y - min;
                        int cgx = cur.x - min;
                        int landLabel = landLabelGrid[cgy, cgx];
                        if (landLabel >= 0)
                            landIds.Add(landLabel);

                        foreach (var (dx, dy) in Neighbors)
                        {
                            int nx = cur.x + dx;
                            int ny = cur.y + dy;

                            if (nx < min || nx > max || ny < min || ny > max)
                                continue;

                            int ngy = ny - min;
                            int ngx = nx - min;

                            if (shelfLabelGrid[ngy, ngx] >= 0)
                                continue;

                            var ndc = grid[nx, ny];
                            if (ndc != DepthClass.Land && ndc != DepthClass.Shallow)
                                continue;

                            shelfLabelGrid[ngy, ngx] = nextId;
                            queue.Enqueue(new Vector2i(nx, ny));
                        }
                    }

                    component.ContainedLandComponentIds.AddRange(landIds);
                    component.ContainedLandComponentIds.Sort();
                    components.Add(component);
                    nextId++;
                }
            }

            components.Sort((a, b) => b.Zones.Count.CompareTo(a.Zones.Count));

            return components;
        }
    }
}
