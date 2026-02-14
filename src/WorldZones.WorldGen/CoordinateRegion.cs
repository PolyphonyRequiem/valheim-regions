namespace WorldZones.WorldGen;

/// <summary>
/// Defines a rectangular region of world coordinates for batch queries or export.
/// Immutable value type using C# 9 record struct.
/// </summary>
/// <param name="MinX">Minimum X coordinate (western boundary).</param>
/// <param name="MinZ">Minimum Z coordinate (southern boundary).</param>
/// <param name="MaxX">Maximum X coordinate (eastern boundary).</param>
/// <param name="MaxZ">Maximum Z coordinate (northern boundary).</param>
public readonly record struct CoordinateRegion(float MinX, float MinZ, float MaxX, float MaxZ)
{
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
