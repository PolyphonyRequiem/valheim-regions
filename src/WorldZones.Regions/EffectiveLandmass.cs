using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Regions
{
    /// <summary>
    /// An effective landmass is a group of <see cref="LandComponent"/>s that are
    /// connected via shared <see cref="ShelfComponent"/>s. Two land components
    /// belong to the same effective landmass if they share a shelf component
    /// (i.e. they are reachable through shallow water).
    /// <para>
    /// This is used for proto-region seed placement: each effective landmass
    /// gets at least one seed, and seed count scales with total land area.
    /// </para>
    /// </summary>
    public class EffectiveLandmass
    {
        /// <summary>Zero-based identifier.</summary>
        public int Id { get; }

        /// <summary>IDs of the <see cref="LandComponent"/>s in this group.</summary>
        public List<int> LandComponentIds { get; }

        /// <summary>Total number of land zones across all member land components.</summary>
        public int TotalLandZones { get; }

        public EffectiveLandmass(int id, List<int> landComponentIds, int totalLandZones)
        {
            Id = id;
            LandComponentIds = landComponentIds;
            TotalLandZones = totalLandZones;
        }

        /// <summary>
        /// Builds effective landmass groups from land and shelf components using
        /// Union-Find to merge land components that share a shelf.
        /// Returns groups sorted by descending total land zone count.
        /// </summary>
        public static List<EffectiveLandmass> Build(
            List<LandComponent> landComponents,
            List<ShelfComponent> shelfComponents)
        {
            if (landComponents == null) throw new ArgumentNullException(nameof(landComponents));
            if (shelfComponents == null) throw new ArgumentNullException(nameof(shelfComponents));

            if (landComponents.Count == 0)
                return new List<EffectiveLandmass>();

            // Build ID → index map (land component IDs may not be contiguous after sorting)
            var idToIndex = new Dictionary<int, int>(landComponents.Count);
            for (int i = 0; i < landComponents.Count; i++)
                idToIndex[landComponents[i].Id] = i;

            int n = landComponents.Count;
            var parent = new int[n];
            var rank = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;

            // Union land components that share a shelf
            foreach (var shelf in shelfComponents)
            {
                if (shelf.ContainedLandComponentIds.Count < 2)
                    continue;

                int firstIdx = -1;
                foreach (var lcId in shelf.ContainedLandComponentIds)
                {
                    if (!idToIndex.TryGetValue(lcId, out int idx))
                        continue;

                    if (firstIdx < 0)
                    {
                        firstIdx = idx;
                    }
                    else
                    {
                        Union(parent, rank, firstIdx, idx);
                    }
                }
            }

            // Collect groups
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(parent, i);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    groups[root] = list;
                }
                list.Add(landComponents[i].Id);
            }

            // Build result
            var result = new List<EffectiveLandmass>(groups.Count);
            int nextId = 0;

            // Sort keys for determinism
            var sortedRoots = groups.Keys.ToList();
            sortedRoots.Sort();

            foreach (var root in sortedRoots)
            {
                var lcIds = groups[root];
                int totalZones = 0;
                foreach (var lcId in lcIds)
                {
                    if (idToIndex.TryGetValue(lcId, out int idx))
                        totalZones += landComponents[idx].Zones.Count;
                }

                result.Add(new EffectiveLandmass(nextId, lcIds, totalZones));
                nextId++;
            }

            // Sort by descending total land zones
            result.Sort((a, b) => b.TotalLandZones.CompareTo(a.TotalLandZones));

            return result;
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]]; // path compression
                x = parent[x];
            }
            return x;
        }

        private static void Union(int[] parent, int[] rank, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra == rb) return;

            if (rank[ra] < rank[rb])
                parent[ra] = rb;
            else if (rank[ra] > rank[rb])
                parent[rb] = ra;
            else
            {
                parent[rb] = ra;
                rank[ra]++;
            }
        }
    }
}
