using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldZones.Regions
{
    /// <summary>
    /// Configuration for archipelago candidate detection.
    /// </summary>
    public class ArchipelagoDetectorOptions
    {
        /// <summary>
        /// Minimum number of distinct land components a shelf must contain
        /// to be considered an archipelago candidate. Default: 3.
        /// </summary>
        public int MinLandComponents { get; set; } = 3;

        /// <summary>
        /// Maximum share (0–1) of total land area that a single land component
        /// may occupy for the shelf to qualify as an archipelago candidate.
        /// If any single island exceeds this fraction, the shelf is dominated
        /// by one landmass and is not an archipelago. Default: 0.6 (60%).
        /// </summary>
        public float MaxDominantShare { get; set; } = 0.6f;
    }

    /// <summary>
    /// Detects archipelago candidates by examining shelf components.
    /// A shelf is flagged when it contains many small land components
    /// with no single dominant landmass.
    /// </summary>
    public static class ArchipelagoDetector
    {
        /// <summary>
        /// Scans shelf components and returns those that qualify as
        /// archipelago candidates based on the configured thresholds.
        /// </summary>
        /// <param name="shelfComponents">Shelf components from <see cref="ComponentLabeler.LabelShelf"/>.</param>
        /// <param name="landComponents">Land components from <see cref="ComponentLabeler.LabelLand"/>, indexed by ID.</param>
        /// <param name="options">Detection thresholds. Uses defaults when null.</param>
        /// <returns>List of archipelago candidates, sorted by descending total land zone count.</returns>
        public static List<ArchipelagoCandidate> Detect(
            List<ShelfComponent> shelfComponents,
            List<LandComponent> landComponents,
            ArchipelagoDetectorOptions options = null)
        {
            if (shelfComponents == null)
                throw new ArgumentNullException(nameof(shelfComponents));
            if (landComponents == null)
                throw new ArgumentNullException(nameof(landComponents));

            var opts = options ?? new ArchipelagoDetectorOptions();

            // Build lookup from land component ID → zone count
            var landZoneCounts = new Dictionary<int, int>();
            foreach (var lc in landComponents)
                landZoneCounts[lc.Id] = lc.Zones.Count;

            var candidates = new List<ArchipelagoCandidate>();
            int nextId = 0;

            foreach (var shelf in shelfComponents)
            {
                var landIds = shelf.ContainedLandComponentIds;

                // Must have at least N distinct land components
                if (landIds.Count < opts.MinLandComponents)
                    continue;

                // Compute total land area and find the dominant component
                int totalLandZones = 0;
                int maxSingleZones = 0;

                foreach (var landId in landIds)
                {
                    if (landZoneCounts.TryGetValue(landId, out int count))
                    {
                        totalLandZones += count;
                        if (count > maxSingleZones)
                            maxSingleZones = count;
                    }
                }

                if (totalLandZones == 0)
                    continue;

                float dominantShare = (float)maxSingleZones / totalLandZones;

                // Dominant landmass exceeds threshold → not an archipelago
                if (dominantShare > opts.MaxDominantShare)
                    continue;

                candidates.Add(new ArchipelagoCandidate(
                    nextId,
                    shelf.Id,
                    new List<int>(landIds),
                    totalLandZones,
                    dominantShare));

                nextId++;
            }

            // Sort by descending total land area
            candidates.Sort((a, b) => b.TotalLandZoneCount.CompareTo(a.TotalLandZoneCount));

            return candidates;
        }
    }
}
