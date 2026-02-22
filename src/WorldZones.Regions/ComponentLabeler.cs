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
    /// Configuration for shelf component labeling.
    /// </summary>
    public class ShelfLabelingOptions
    {
        /// <summary>
        /// Maximum number of consecutive ShelfWater (Shallow) zones that may
        /// be traversed between two Land zones within the same shelf component.
        /// Gaps wider than this sever the connection, preventing long
        /// coastal-shallow "highway" connections around deep channels.
        /// Land resets the counter to 0; each Shallow zone increments by 1.
        /// Each zone is 64 m, so the default of 2 ≈ 128 m.
        /// </summary>
        public int MaxShallowDistanceFromLandZones { get; set; } = 2;
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
        /// Computes multi-source BFS distance (in zones) from every Land or Shallow
        /// zone to the nearest Land zone. Land zones have distance 0. Shallow zones
        /// get their shortest 4-neighbor path distance through Land∪Shallow. Deep
        /// zones (water deeper than ShelfMaxDepth) are impassable and keep distance
        /// <see cref="int.MaxValue"/>.
        /// </summary>
        /// <returns>A 2D array [size, size] indexed by grid offsets (gy, gx).</returns>
        public static int[,] ComputeDistanceToLand(ZoneGrid grid)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            var dist = new int[size, size];
            // Initialize all to MaxValue
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    dist[y, x] = int.MaxValue;

            // Multi-source BFS seeded from all Land zones
            var queue = new Queue<Vector2i>();
            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    if (grid[zx, zy] == DepthClass.Land)
                    {
                        int gy = zy - min;
                        int gx = zx - min;
                        dist[gy, gx] = 0;
                        queue.Enqueue(new Vector2i(zx, zy));
                    }
                }
            }

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                int cgy = cur.y - min;
                int cgx = cur.x - min;
                int curDist = dist[cgy, cgx];

                foreach (var (dx, dy) in Neighbors)
                {
                    int nx = cur.x + dx;
                    int ny = cur.y + dy;

                    if (nx < min || nx > max || ny < min || ny > max)
                        continue;

                    int ngy = ny - min;
                    int ngx = nx - min;

                    // Only propagate through Land or Shallow (DeepWater blocks)
                    var ndc = grid[nx, ny];
                    if (ndc != DepthClass.Land && ndc != DepthClass.Shallow)
                        continue;

                    int newDist = curDist + 1;
                    if (newDist < dist[ngy, ngx])
                    {
                        dist[ngy, ngx] = newDist;
                        queue.Enqueue(new Vector2i(nx, ny));
                    }
                }
            }

            return dist;
        }

        /// <summary>
        /// Labels all connected shelf components (Land ∪ ShelfWater) in the grid
        /// using 4-neighbor adjacency and a 0-1 BFS that tracks
        /// <em>consecutive shallow depth</em> — the number of Shallow zones
        /// traversed since the last Land zone along the current path.
        /// Stepping onto a Land zone resets the counter to 0; stepping onto a
        /// Shallow zone increments it by 1.  If the counter would exceed
        /// <see cref="ShelfLabelingOptions.MaxShallowDistanceFromLandZones"/>,
        /// the zone is not included.  DeepWater zones (depth &gt; ShelfMaxDepth)
        /// are impassable and always block traversal — no biome type is used.
        /// <para>
        /// Components are seeded only from unvisited Land zones.
        /// </para>
        /// Each shelf is mapped to the <see cref="LandComponent"/>s it contains via
        /// <paramref name="landLabelGrid"/> (produced by <see cref="LabelLand"/>).
        /// Returns components sorted by descending zone count.
        /// </summary>
        public static List<ShelfComponent> LabelShelf(
            ZoneGrid grid,
            int[,] landLabelGrid,
            out int[,] shelfLabelGrid,
            ShelfLabelingOptions options = null)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (landLabelGrid == null)
                throw new ArgumentNullException(nameof(landLabelGrid));

            var opts = options ?? new ShelfLabelingOptions();
            int maxShallowDepth = opts.MaxShallowDistanceFromLandZones;

            int size = grid.Size;
            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            shelfLabelGrid = new int[size, size];
            var shallowDepth = new int[size, size];
            var finalized = new bool[size, size];

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    shelfLabelGrid[y, x] = -1;
                    shallowDepth[y, x] = int.MaxValue;
                }

            var components = new List<ShelfComponent>();
            int nextId = 0;

            // Only seed components from Land zones (non-ocean biome).
            for (int zy = min; zy <= max; zy++)
            {
                for (int zx = min; zx <= max; zx++)
                {
                    int gy = zy - min;
                    int gx = zx - min;

                    if (grid[zx, zy] != DepthClass.Land)
                        continue;
                    if (finalized[gy, gx])
                        continue;

                    // 0-1 BFS: Land transitions cost 0 (deque front, depth resets),
                    // Shallow transitions cost 1 (deque back, depth increments).
                    var component = new ShelfComponent(nextId);
                    var landIds = new HashSet<int>();
                    var deque = new LinkedList<Vector2i>();

                    shallowDepth[gy, gx] = 0;
                    shelfLabelGrid[gy, gx] = nextId;
                    deque.AddFirst(new Vector2i(zx, zy));

                    while (deque.Count > 0)
                    {
                        var cur = deque.First.Value;
                        deque.RemoveFirst();

                        int cgy = cur.y - min;
                        int cgx = cur.x - min;

                        if (finalized[cgy, cgx])
                            continue;
                        finalized[cgy, cgx] = true;

                        component.Zones.Add(cur);

                        int landLabel = landLabelGrid[cgy, cgx];
                        if (landLabel >= 0)
                            landIds.Add(landLabel);

                        int curDepth = shallowDepth[cgy, cgx];

                        foreach (var (dx, dy) in Neighbors)
                        {
                            int nx = cur.x + dx;
                            int ny = cur.y + dy;

                            if (nx < min || nx > max || ny < min || ny > max)
                                continue;

                            int ngy = ny - min;
                            int ngx = nx - min;

                            if (finalized[ngy, ngx])
                                continue;

                            var ndc = grid[nx, ny];
                            if (ndc == DepthClass.Deep)
                                continue;

                            // Land resets depth to 0; Shallow increments from parent.
                            int newDepth = (ndc == DepthClass.Land) ? 0 : curDepth + 1;

                            if (newDepth > maxShallowDepth)
                                continue;

                            if (newDepth < shallowDepth[ngy, ngx])
                            {
                                shallowDepth[ngy, ngx] = newDepth;
                                shelfLabelGrid[ngy, ngx] = nextId;

                                if (ndc == DepthClass.Land)
                                    deque.AddFirst(new Vector2i(nx, ny));
                                else
                                    deque.AddLast(new Vector2i(nx, ny));
                            }
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
