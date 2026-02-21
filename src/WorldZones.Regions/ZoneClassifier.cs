using System;
using WorldZones.WorldGen;

namespace WorldZones.Regions
{
    /// <summary>
    /// Configuration thresholds for zone depth classification.
    /// </summary>
    public class ZoneClassifierOptions
    {
        /// <summary>
        /// Base-height value at sea level. Zones at or above this are Land.
        /// Valheim default ≈ 0.05.
        /// </summary>
        public float SeaLevelBaseHeight { get; set; } = 0.05f;

        /// <summary>
        /// How far below sea level counts as Shallow (rather than Deep),
        /// in base-height units (not meters).
        /// A zone with baseHeight &gt;= SeaLevelBaseHeight - ShallowDepthBelowSea is Shallow.
        /// </summary>
        public float ShallowDepthBelowSea { get; set; } = 0.02f;
    }

    /// <summary>
    /// Classifies every zone in a <see cref="ZoneGrid"/> as Land, Shallow, or Deep
    /// by sampling <see cref="WorldGenerator.GetBaseHeight"/> at each zone center.
    /// </summary>
    public static class ZoneClassifier
    {
        /// <summary>
        /// Fills <paramref name="grid"/> with <see cref="DepthClass"/> values
        /// by sampling <paramref name="heightSampler"/> at each zone center.
        /// Deterministic for a given sampler.
        /// </summary>
        /// <param name="grid">The zone grid to populate.</param>
        /// <param name="heightSampler">Returns base height given (worldX, worldZ).</param>
        /// <param name="options">Classification thresholds. Uses defaults when null.</param>
        public static void Classify(ZoneGrid grid, Func<float, float, float> heightSampler, ZoneClassifierOptions options = null)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (heightSampler == null)
                throw new ArgumentNullException(nameof(heightSampler));

            var opts = options ?? new ZoneClassifierOptions();
            float seaLevel = opts.SeaLevelBaseHeight;
            float shallowThreshold = seaLevel - opts.ShallowDepthBelowSea;

            foreach (var coord in grid.AllCoords())
            {
                var center = ZoneGrid.ZoneCenter(coord);
                float baseHeight = heightSampler(center.worldX, center.worldZ);

                DepthClass depth;
                if (baseHeight >= seaLevel)
                    depth = DepthClass.Land;
                else if (baseHeight >= shallowThreshold)
                    depth = DepthClass.Shallow;
                else
                    depth = DepthClass.Deep;

                grid[coord] = depth;
            }
        }

        /// <summary>
        /// Fills <paramref name="grid"/> with <see cref="DepthClass"/> values
        /// by sampling the world generator at each zone center.
        /// Deterministic for a given seed.
        /// </summary>
        /// <param name="grid">The zone grid to populate.</param>
        /// <param name="worldGen">World generator initialised with the desired seed.</param>
        /// <param name="options">Classification thresholds. Uses defaults when null.</param>
        public static void Classify(ZoneGrid grid, WorldGenerator worldGen, ZoneClassifierOptions options = null)
        {
            if (worldGen == null)
                throw new ArgumentNullException(nameof(worldGen));

            Classify(grid, (wx, wz) => worldGen.GetBaseHeight(wx, wz), options);
        }
    }
}
