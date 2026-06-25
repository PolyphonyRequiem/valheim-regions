using System;
using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// A minimal deterministic min-heap for the cost-weighted region growth (Dijkstra). Hand-rolled
    /// because <c>System.Collections.Generic.PriorityQueue</c> is .NET 6+ and the topology library
    /// ships <c>net472</c> (Valheim/Mono). Ordering is by (distance, then a caller-supplied tie-break)
    /// so the resulting tessellation is bit-reproducible across runs and platforms — the determinism
    /// the whole contract rests on. See docs/design/region-borders.md.
    /// </summary>
    internal sealed class RegionGrowthHeap
    {
        internal readonly struct Node
        {
            public readonly double Dist;
            public readonly int X;
            public readonly int Y;
            public readonly int RegionId;
            // Deterministic tie-break key when two frontiers reach a cell at equal cost: lower wins.
            public readonly long Tie;

            public Node(double dist, int x, int y, int regionId, long tie)
            {
                this.Dist = dist; this.X = x; this.Y = y; this.RegionId = regionId; this.Tie = tie;
            }
        }

        private readonly List<Node> heap = new List<Node>();

        public int Count => this.heap.Count;

        // Lower Dist first; ties broken by the Tie key (deterministic), then RegionId.
        private static bool Less(in Node a, in Node b)
        {
            if (a.Dist != b.Dist) return a.Dist < b.Dist;
            if (a.Tie != b.Tie) return a.Tie < b.Tie;
            return a.RegionId < b.RegionId;
        }

        public void Push(in Node n)
        {
            this.heap.Add(n);
            int i = this.heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (Less(this.heap[i], this.heap[parent]))
                {
                    (this.heap[i], this.heap[parent]) = (this.heap[parent], this.heap[i]);
                    i = parent;
                }
                else break;
            }
        }

        public Node Pop()
        {
            Node top = this.heap[0];
            int last = this.heap.Count - 1;
            this.heap[0] = this.heap[last];
            this.heap.RemoveAt(last);
            int n = this.heap.Count;
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = 2 * i + 2, smallest = i;
                if (l < n && Less(this.heap[l], this.heap[smallest])) smallest = l;
                if (r < n && Less(this.heap[r], this.heap[smallest])) smallest = r;
                if (smallest == i) break;
                (this.heap[i], this.heap[smallest]) = (this.heap[smallest], this.heap[i]);
                i = smallest;
            }
            return top;
        }
    }
}
