using System;
using System.Collections.Generic;

namespace WorldZones.Regions
{
    /// <summary>
    /// A 2D grid of 64×64m zones covering the Valheim world (±10,000m radius).
    /// Stores a <see cref="DepthClass"/> per zone.
    /// </summary>
    public class ZoneGrid
    {
        /// <summary>Zone edge length in world units (meters), matching ZoneSystem.c_ZoneSize.</summary>
        public const int ZoneSize = 64;

        /// <summary>World radius in meters.</summary>
        public const float WorldRadius = 10000f;

        /// <summary>Minimum zone coordinate on each axis (inclusive).</summary>
        public readonly int MinIndex;

        /// <summary>Maximum zone coordinate on each axis (inclusive).</summary>
        public readonly int MaxIndex;

        /// <summary>Number of zones along each axis.</summary>
        public readonly int Size;

        private readonly DepthClass[] _cells;

        /// <summary>
        /// Creates a ZoneGrid covering ±<paramref name="worldRadiusMeters"/> at 64m zone resolution.
        /// </summary>
        public ZoneGrid(float worldRadiusMeters)
        {
            MinIndex = WorldToZone(-worldRadiusMeters);
            MaxIndex = WorldToZone(worldRadiusMeters);
            Size = MaxIndex - MinIndex + 1;
            _cells = new DepthClass[Size * Size];
        }

        /// <summary>
        /// Creates a ZoneGrid covering ±10,000m (default Valheim world radius).
        /// </summary>
        public ZoneGrid() : this(WorldRadius) { }

        /// <summary>
        /// Converts a world-space coordinate to a zone index,
        /// matching Valheim ZoneSystem: Floor((world + 32) / 64).
        /// </summary>
        public static int WorldToZone(float world)
        {
            return (int)Math.Floor((world + ZoneSize / 2.0) / ZoneSize);
        }

        /// <summary>
        /// Returns the world-space center of the given zone coordinate.
        /// </summary>
        public static (float worldX, float worldZ) ZoneCenter(Vector2i coord)
        {
            return (coord.x * (float)ZoneSize, coord.y * (float)ZoneSize);
        }

        /// <summary>
        /// Converts a world-space position to a zone coordinate.
        /// </summary>
        public static Vector2i WorldToZoneCoord(float worldX, float worldZ)
        {
            return new Vector2i(WorldToZone(worldX), WorldToZone(worldZ));
        }

        /// <summary>
        /// Returns true if the zone coordinate falls within the grid bounds.
        /// </summary>
        public bool InBounds(Vector2i coord)
        {
            return coord.x >= MinIndex && coord.x <= MaxIndex
                && coord.y >= MinIndex && coord.y <= MaxIndex;
        }

        /// <summary>
        /// Gets or sets the <see cref="DepthClass"/> for the zone at the given coordinate.
        /// </summary>
        public DepthClass this[Vector2i coord]
        {
            get => _cells[FlatIndex(coord)];
            set => _cells[FlatIndex(coord)] = value;
        }

        /// <summary>
        /// Gets or sets the <see cref="DepthClass"/> for the zone at (x, y).
        /// </summary>
        public DepthClass this[int x, int y]
        {
            get => _cells[FlatIndex(x, y)];
            set => _cells[FlatIndex(x, y)] = value;
        }



        private int FlatIndex(Vector2i coord)
        {
            return FlatIndex(coord.x, coord.y);
        }

        private int FlatIndex(int x, int y)
        {
            if (x < MinIndex || x > MaxIndex)
                throw new ArgumentOutOfRangeException(nameof(x),
                    $"Zone x={x} is outside grid bounds [{MinIndex}..{MaxIndex}]");
            if (y < MinIndex || y > MaxIndex)
                throw new ArgumentOutOfRangeException(nameof(y),
                    $"Zone y={y} is outside grid bounds [{MinIndex}..{MaxIndex}]");
            return (y - MinIndex) * Size + (x - MinIndex);
        }

        /// <summary>
        /// Yields all zone coordinates in deterministic order (y then x, both ascending).
        /// </summary>
        public IEnumerable<Vector2i> AllCoords()
        {
            for (int y = MinIndex; y <= MaxIndex; y++)
                for (int x = MinIndex; x <= MaxIndex; x++)
                    yield return new Vector2i(x, y);
        }
    }
}
