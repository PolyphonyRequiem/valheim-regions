using System;

namespace WorldZones.Regions
{
    /// <summary>
    /// Configuration thresholds for zone depth classification.
    /// </summary>
    public class ZoneClassifierOptions
    {
        /// <summary>
        /// Height value at the water surface, in the units returned by the sampler.
        /// For base-height samplers this is ~0.05; for biome-height (world units) it is 30f.
        /// Zones at or above this are Land.
        /// </summary>
        public float WaterLevel { get; set; } = 30f;

        /// <summary>
        /// How far below <see cref="WaterLevel"/> a zone can be and still count as
        /// Shallow (rather than Deep), in the same units as <see cref="WaterLevel"/>.
        /// A zone with height &gt;= WaterLevel - ShallowDepth is Shallow.
        /// Default 0 means there is no shallow band when using height thresholds alone.
        /// </summary>
        public float ShallowDepth { get; set; } = 0f;
    }

    /// <summary>
    /// Classifies every zone in a <see cref="ZoneGrid"/> as Land, Shallow, or Deep
    /// based on terrain height (depth below sea level).
    /// No biome types are used — classification is purely depth-based.
    /// </summary>
    public static class ZoneClassifier
    {
        /// <summary>Water surface height in world units (metres).</summary>
        public const float DefaultWaterLevel = 30f;

        /// <summary>
        /// Maximum water depth (metres below <see cref="DefaultWaterLevel"/>) that
        /// still counts as Shallow (continental shelf). Zones deeper than this are Deep.
        /// depthMeters = max(0, SeaLevelY - terrainY);
        /// Shallow when 0 &lt; depthMeters &lt;= ShelfMaxDepthMeters, Deep when depthMeters &gt; ShelfMaxDepthMeters.
        /// </summary>
        public const float DefaultShelfMaxDepth = 10f;

        /// <summary>
        /// Fills <paramref name="grid"/> with <see cref="DepthClass"/> values
        /// by sampling <paramref name="heightSampler"/> at each zone center.
        /// Useful for unit testing with synthetic height functions.
        /// </summary>
        /// <param name="grid">The zone grid to populate.</param>
        /// <param name="heightSampler">Returns a height value given (worldX, worldZ).</param>
        /// <param name="options">Classification thresholds. Uses defaults when null.</param>
        public static void Classify(ZoneGrid grid, Func<float, float, float> heightSampler, ZoneClassifierOptions options = null)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (heightSampler == null)
                throw new ArgumentNullException(nameof(heightSampler));

            var opts = options ?? new ZoneClassifierOptions();
            float waterLevel = opts.WaterLevel;
            float shallowThreshold = waterLevel - opts.ShallowDepth;

            foreach (var coord in grid.AllCoords())
            {
                var center = ZoneGrid.ZoneCenter(coord);
                float height = heightSampler(center.worldX, center.worldZ);

                DepthClass depth;
                if (height >= waterLevel)
                    depth = DepthClass.Land;
                else if (height >= shallowThreshold)
                    depth = DepthClass.Shallow;
                else
                    depth = DepthClass.Deep;

                grid[coord] = depth;
            }
        }

        /// <summary>
        /// Fills <paramref name="grid"/> with <see cref="DepthClass"/> values
        /// using depth-based classification:
        /// terrainY ≥ SeaLevel → Land;
        /// terrainY ≥ SeaLevel − ShelfMaxDepth → Shallow;
        /// otherwise → Deep.
        /// Biome type is NOT used for classification — depth only.
        /// </summary>
        /// <param name="grid">The zone grid to populate.</param>
        /// <param name="provider">World data provider.</param>
        public static void Classify(ZoneGrid grid, IWorldDataProvider provider)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            float waterLevel = provider.WaterLevel;
            float shelfThreshold = waterLevel - DefaultShelfMaxDepth;

            foreach (var coord in grid.AllCoords())
            {
                var center = ZoneGrid.ZoneCenter(coord);
                float wx = center.worldX;
                float wz = center.worldZ;

                float terrainY = provider.GetTerrainHeight(wx, wz);

                DepthClass depth;
                if (terrainY >= waterLevel)
                {
                    depth = DepthClass.Land;
                }
                else if (terrainY >= shelfThreshold)
                {
                    depth = DepthClass.Shallow;
                }
                else
                {
                    depth = DepthClass.Deep;
                }

                grid[coord] = depth;
            }
        }
    }
}
