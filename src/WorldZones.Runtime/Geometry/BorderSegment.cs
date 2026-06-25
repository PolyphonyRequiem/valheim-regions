namespace WorldZones.Runtime.Geometry
{
    /// <summary>
    /// One undirected boundary segment on the zone-edge lattice — the seam between two adjacent
    /// zones whose region assignment differs. THE locked render primitive (Daniel, 2026-06-24): a
    /// seam is owned by the PAIR of regions it divides and is emitted exactly ONCE, so a consumer
    /// strokes each border a single time with no double-draw / z-fight, and "who is on each side" is
    /// intrinsic to the segment (what a territorial consumer later needs).
    ///
    /// <para>Endpoints are world-space metres on the <c>64·n+32</c> zone-corner lattice. Keys are the
    /// durable <see cref="WorldZones.Regions.RegionKey"/> strings (e.g. "r.-3.7"), canonicalised so
    /// <see cref="KeyA"/> ≤ <see cref="KeyB"/> ordinally and a region-vs-void (coast / world-edge)
    /// seam carries the region in <see cref="KeyA"/> and <c>null</c> in <see cref="KeyB"/>. So "all
    /// segments touching region R" = those where <c>KeyA == R || KeyB == R</c>. See
    /// docs/design/region-render-seam.md.</para>
    /// </summary>
    public readonly struct BorderSegment
    {
        /// <summary>First endpoint (world metres).</summary>
        public readonly WzVec2 A;

        /// <summary>Second endpoint (world metres).</summary>
        public readonly WzVec2 B;

        /// <summary>Durable key of one bounding region (the ordinally-lesser of the two). Never null.</summary>
        public readonly string KeyA;

        /// <summary>
        /// Durable key of the other bounding region, or <c>null</c> when the far side is
        /// unassigned — ocean, unassigned land, or off the world edge (i.e. this is region
        /// <see cref="KeyA"/>'s outer coastline).
        /// </summary>
        public readonly string KeyB;

        public BorderSegment(WzVec2 a, WzVec2 b, string keyA, string keyB)
        {
            this.A = a;
            this.B = b;
            this.KeyA = keyA;
            this.KeyB = keyB;
        }

        /// <summary>True if the far side is unassigned (ocean / world edge) — a region's outer coast.</summary>
        public bool IsCoastline => this.KeyB == null;

        /// <summary>Segment length in world metres (always 64 — one zone edge — but computed, not assumed).</summary>
        public double Length
        {
            get
            {
                double dx = this.B.X - this.A.X;
                double dz = this.B.Z - this.A.Z;
                return System.Math.Sqrt(dx * dx + dz * dz);
            }
        }
    }
}
