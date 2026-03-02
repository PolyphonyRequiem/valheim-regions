using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Regions
{
    /// <summary>
    /// Attributes connected inland-water bodies to adjacent regions deterministically.
    /// </summary>
    public static class InlandWaterAttributor
    {
        private static readonly (int dx, int dy)[] Neighbors = { (1, 0), (-1, 0), (0, -1), (0, 1) };

        /// <summary>
        /// Applies inland-water attribution to the provided region ownership grid.
        /// </summary>
        /// <param name="grid">Zone grid used for bounds and coordinate mapping.</param>
        /// <param name="regionIdGrid">Mutable ownership grid; updated in-place for attributed inland-water zones.</param>
        /// <param name="connectivityGrid">Per-zone connectivity kinds produced by the categorizer.</param>
        /// <param name="inlandBodies">Connected inland-water bodies to attribute.</param>
        /// <returns>Attribution summary counts and changed-region tracking.</returns>
        /// <exception cref="ArgumentNullException">Thrown when any input argument is null.</exception>
        public static InlandWaterAttributionResult Attribute(
            ZoneGrid grid,
            int[,] regionIdGrid,
            WaterConnectivityKind[,] connectivityGrid,
            List<InlandWaterBody> inlandBodies)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (regionIdGrid == null) throw new ArgumentNullException(nameof(regionIdGrid));
            if (connectivityGrid == null) throw new ArgumentNullException(nameof(connectivityGrid));
            if (inlandBodies == null) throw new ArgumentNullException(nameof(inlandBodies));

            int min = grid.MinIndex;
            int max = grid.MaxIndex;

            var result = new InlandWaterAttributionResult();

            foreach (var body in inlandBodies)
            {
                var borderVotes = new Dictionary<int, int>();
                body.AdjacentRegionIds.Clear();

                foreach (var zone in body.Zones)
                {
                    foreach (var (dx, dy) in Neighbors)
                    {
                        int nx = zone.x + dx;
                        int ny = zone.y + dy;
                        if (nx < min || nx > max || ny < min || ny > max)
                        {
                            continue;
                        }

                        int regionId = regionIdGrid[ny - min, nx - min];
                        if (regionId < 0)
                        {
                            continue;
                        }

                        body.AdjacentRegionIds.Add(regionId);
                        if (!borderVotes.ContainsKey(regionId))
                        {
                            borderVotes[regionId] = 0;
                        }

                        borderVotes[regionId]++;
                    }
                }

                int winnerRegionId = ChooseWinner(borderVotes);
                if (winnerRegionId < 0)
                {
                    result.UnassignedWaterBodyCount++;
                    result.UnassignedInlandWaterZoneCount += body.ZoneCount;
                    continue;
                }

                foreach (var zone in body.Zones)
                {
                    if (connectivityGrid[zone.y - min, zone.x - min] != WaterConnectivityKind.InlandWater)
                    {
                        continue;
                    }

                    regionIdGrid[zone.y - min, zone.x - min] = winnerRegionId;
                }

                result.AttributedWaterBodyCount++;
                result.AttributedWaterZoneCount += body.ZoneCount;
                result.ChangedRegionIds.Add(winnerRegionId);
            }

            return result;
        }

        private static int ChooseWinner(Dictionary<int, int> borderVotes)
        {
            int winnerRegionId = -1;
            int winnerBorderCount = -1;

            foreach (int regionId in borderVotes.Keys.OrderBy(id => id))
            {
                int voteCount = borderVotes[regionId];
                if (voteCount > winnerBorderCount)
                {
                    winnerBorderCount = voteCount;
                    winnerRegionId = regionId;
                    continue;
                }

                if (voteCount == winnerBorderCount && regionId < winnerRegionId)
                {
                    winnerRegionId = regionId;
                }
            }

            return winnerRegionId;
        }
    }
}
