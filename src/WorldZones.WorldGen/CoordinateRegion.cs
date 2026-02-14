namespace WorldZones.WorldGen
{
    /// <summary>
    /// Defines a rectangular region of world coordinates for batch queries or export.
    /// Immutable value type.
    /// </summary>
    public readonly struct CoordinateRegion
    {
        /// <summary>Minimum X coordinate (western boundary).</summary>
        public float MinX { get; init; }
        
        /// <summary>Minimum Z coordinate (southern boundary).</summary>
        public float MinZ { get; init; }
        
        /// <summary>Maximum X coordinate (eastern boundary).</summary>
        public float MaxX { get; init; }
        
        /// <summary>Maximum Z coordinate (northern boundary).</summary>
        public float MaxZ { get; init; }
        
        /// <summary>
        /// Initializes a new coordinate region.
        /// </summary>
        public CoordinateRegion(float minX, float minZ, float maxX, float maxZ)
        {
            this.MinX = minX;
            this.MinZ = minZ;
            this.MaxX = maxX;
            this.MaxZ = maxZ;
        }
        
        /// <summary>Gets the width of the region (X-axis span).</summary>
        public float Width => this.MaxX - this.MinX;
        
        /// <summary>Gets the height of the region (Z-axis span).</summary>
        public float Height => this.MaxZ - this.MinZ;
        
        /// <summary>
        /// Validates that the region has positive dimensions.
        /// </summary>
        /// <returns>True if MaxX > MinX and MaxZ > MinZ, otherwise false.</returns>
        public bool IsValid() => this.MaxX > this.MinX && this.MaxZ > this.MinZ;
    }
}
