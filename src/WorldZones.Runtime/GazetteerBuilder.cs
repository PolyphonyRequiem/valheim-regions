using System;
using System.Collections.Generic;
using System.Linq;
using WorldZones.Regions;
using WorldZones.WorldGen;
using Vector2i = WorldZones.Regions.Vector2i;

namespace WorldZones.Runtime
{
    /// <summary>
    /// Turns the raw proto-region topology (region-id grid + <see cref="ProtoRegion"/> list) into the
    /// rich <see cref="RegionInfo"/> model by sampling biome + height per zone from an
    /// <see cref="IWorldSampler"/> and aggregating per region.
    ///
    /// <para>
    /// This is the per-zone aggregation that used to live as private static code inside the CLI's
    /// gazetteer exporter (<c>Gazetteer.Export</c>). It is lifted here verbatim in behaviour so the
    /// in-process model a consumer mod gets is byte-for-byte the same data the gazetteer JSON carries
    /// — the CLI can now become a thin serializer over this instead of owning a fourth copy of the
    /// aggregation.
    /// </para>
    /// </summary>
    public static class GazetteerBuilder
    {
        private static readonly (int dx, int dy)[] N4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private sealed class Agg
        {
            public readonly ProtoRegion Region;
            public int LandZones;
            public double SumX, SumZ, SumH;
            public int MinZx = int.MaxValue, MinZy = int.MaxValue, MaxZx = int.MinValue, MaxZy = int.MinValue;
            public float MinH = float.MaxValue, MaxH = float.MinValue;
            public float PeakX, PeakZ;
            public bool Coastal;
            public readonly Dictionary<BiomeType, int> Biome = new Dictionary<BiomeType, int>();
            public readonly HashSet<int> NeighborIds = new HashSet<int>();
            public Agg(ProtoRegion r) { this.Region = r; }
        }

        /// <summary>
        /// Aggregates the proto-region result into rich <see cref="RegionInfo"/> records.
        /// Returned regions are ordered by durable <c>RegionKey</c> (ordinal), and only regions with
        /// at least one land zone are included. Names are NOT set here — that is the namer's job.
        /// </summary>
        public static List<RegionInfo> Build(
            IWorldSampler sampler,
            ZoneGrid grid,
            ProtoRegionResult result,
            int[,] regionIdGrid)
        {
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (regionIdGrid == null) throw new ArgumentNullException(nameof(regionIdGrid));

            int size = grid.Size, min = grid.MinIndex;

            var agg = new Dictionary<int, Agg>();
            foreach (var r in result.Regions) agg[r.Id] = new Agg(r);

            for (int gy = 0; gy < size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int id = regionIdGrid[gy, gx];
                if (id < 0 || !agg.TryGetValue(id, out var a)) continue;

                int zx = gx + min, zy = gy + min;
                float wx = zx * (float)ZoneGrid.ZoneSize, wz = zy * (float)ZoneGrid.ZoneSize;
                BiomeType biome = sampler.GetBiome(wx, wz);
                float h = sampler.GetHeight(wx, wz);

                a.LandZones++;
                a.SumX += wx; a.SumZ += wz; a.SumH += h;
                if (zx < a.MinZx) a.MinZx = zx;
                if (zy < a.MinZy) a.MinZy = zy;
                if (zx > a.MaxZx) a.MaxZx = zx;
                if (zy > a.MaxZy) a.MaxZy = zy;
                if (h < a.MinH) a.MinH = h;
                if (h > a.MaxH) { a.MaxH = h; a.PeakX = wx; a.PeakZ = wz; }
                a.Biome.TryGetValue(biome, out int bc);
                a.Biome[biome] = bc + 1;

                foreach (var (dx, dy) in N4)
                {
                    int ngx = gx + dx, ngy = gy + dy;
                    if (ngx < 0 || ngx >= size || ngy < 0 || ngy >= size) continue;
                    int nid = regionIdGrid[ngy, ngx];
                    if (nid >= 0 && nid != id && agg.ContainsKey(nid)) a.NeighborIds.Add(nid);
                    var ndepth = grid[ngx + min, ngy + min];
                    if (ndepth != DepthClass.Land) a.Coastal = true;
                }
            }

            // id → RegionKey, for resolving neighbour arrays to durable keys
            var idToKey = result.Regions.ToDictionary(r => r.Id, r => r.RegionKey);

            var infos = new List<RegionInfo>();
            foreach (var r in result.Regions)
            {
                var a = agg[r.Id];
                int land = a.LandZones;
                if (land <= 0) continue; // skip regions with no land footprint (matches gazetteer)

                double cx = a.SumX / land, cz = a.SumZ / land, meanH = a.SumH / land;
                double areaKm2 = (double)r.TotalAreaZones * ZoneGrid.ZoneSize * ZoneGrid.ZoneSize / 1_000_000.0;

                // dominant non-ocean biome
                BiomeType dom = BiomeType.None;
                int domC = -1;
                foreach (var kv in a.Biome)
                    if (kv.Key != BiomeType.Ocean && kv.Value > domC) { domC = kv.Value; dom = kv.Key; }

                // biome composition (fraction of land zones), descending, ocean excluded
                var comp = a.Biome.Where(kv => kv.Key != BiomeType.Ocean)
                    .OrderByDescending(kv => kv.Value)
                    .ToDictionary(kv => kv.Key, kv => (float)((double)kv.Value / land));

                var neighborKeys = a.NeighborIds
                    .Select(nid => idToKey[nid])
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                infos.Add(new RegionInfo
                {
                    RegionKey = r.RegionKey,
                    Name = null, // set by the namer
                    TransientId = r.Id,
                    IdentityCoord = r.IdentityCoord,
                    SeedZone = r.Seed,
                    CentroidX = (float)cx,
                    CentroidZ = (float)cz,
                    MinZoneX = a.MinZx, MinZoneZ = a.MinZy, MaxZoneX = a.MaxZx, MaxZoneZ = a.MaxZy,
                    AreaZones = r.TotalAreaZones,
                    LandZones = r.LandAreaZones,
                    InlandWaterZones = r.InlandWaterAreaZones,
                    AreaKm2 = areaKm2,
                    IsCoastal = a.Coastal,
                    DominantBiome = dom,
                    BiomeComposition = comp,
                    MinElevation = a.MinH,
                    MeanElevation = (float)meanH,
                    MaxElevation = a.MaxH,
                    HighestPeakX = a.PeakX,
                    HighestPeakZ = a.PeakZ,
                    NeighborKeys = neighborKeys,
                });
            }

            infos.Sort((x, y) => string.CompareOrdinal(x.RegionKey, y.RegionKey));
            return infos;
        }
    }
}
