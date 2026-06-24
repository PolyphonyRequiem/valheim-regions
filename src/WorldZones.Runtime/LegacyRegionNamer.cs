using System;
using System.Collections.Generic;
using WorldZones.Regions;

namespace WorldZones.Runtime
{
    /// <summary>
    /// The original region namer: the deterministic 500-name catalog
    /// (<see cref="RegionGuidNameService"/>) keyed off worldId + durable RegionKey. This is the name
    /// the shipped overlay plugin and the CLI gazetteer used before the multi-schema namer existed.
    ///
    /// <para>
    /// It exists so a caller can route through <see cref="WorldZonesRuntime.Build"/> while keeping the
    /// exact legacy naming — i.e. so consolidating the bootstrap is a behavior-preserving refactor, not
    /// a behavior change. Adopting the richer <see cref="MultiSchemaRegionNamer"/> in a given consumer
    /// is then a separate, visible one-line swap (<c>options.Namer = new MultiSchemaRegionNamer()</c>),
    /// not something a refactor smuggles in.
    /// </para>
    /// </summary>
    public sealed class LegacyRegionNamer : IRegionNamer
    {
        /// <inheritdoc />
        public void NameAll(string worldId, IReadOnlyList<RegionInfo> regions)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("worldId must not be null or empty", nameof(worldId));
            if (regions == null) throw new ArgumentNullException(nameof(regions));

            foreach (var r in regions)
            {
                r.Name = RegionGuidNameService.CreateDeterministicName(worldId, r.RegionKey);
            }
        }
    }
}
