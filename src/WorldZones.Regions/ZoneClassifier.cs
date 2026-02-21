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
    /// Classifies every zone in a <see cref="ZoneGrid"/> as Land, Shallow, or Deep.
    /// The <see cref="WorldGenerator"/> overload mirrors the BiomeMapExporter logic:
    /// Ocean biome → Deep, non-ocean with GetBiomeHeight &lt; 30 → Shallow, else → Land.
    /// </summary>
    public static class ZoneClassifier
    {
        /// <summary>Water surface height in world units (metres).</summary>
        public const float DefaultWaterLevel = 30f;

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
        /// using the same logic as BiomeMapExporter:
        /// Ocean biome → Deep; non-ocean with GetBiomeHeight &lt; 30 → Shallow; else → Land.
        /// </summary>
        /// <param name="grid">The zone grid to populate.</param>
        /// <param name="worldGen">World generator initialised with the desired seed.</param>
        public static void Classify(ZoneGrid grid, WorldGenerator worldGen)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            if (worldGen == null)
                throw new ArgumentNullException(nameof(worldGen));

            foreach (var coord in grid.AllCoords())
            {
                var center = ZoneGrid.ZoneCenter(coord);
                float wx = center.worldX;
                float wz = center.worldZ;

                var biome = worldGen.GetBiome(wx, wz);

                DepthClass depth;
                if (biome == BiomeType.Ocean)
                {
                    depth = DepthClass.Deep;
                }
                else if (worldGen.GetBiomeHeight(biome, wx, wz) < DefaultWaterLevel)
                {
                    depth = DepthClass.Shallow;
                }
                else
                {
                    depth = DepthClass.Land;
                }

                grid[coord] = depth;
            }
        }
    }
}
