using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WorldZones.WorldGen;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using RegionInfo = WorldZones.Runtime.RegionInfo;

namespace WorldZones.Cli
{
    /// <summary>
    /// Throwaway composite dumper for the region-render design review (2026-06-26).
    /// Emits ONE JSON blob carrying everything the offline Python renderer needs to compose
    /// Daniel's proposed layered map: biome-tinted region fill + seaward coast glow +
    /// terrestrial-only ink + centroid labels.
    ///
    /// Matches the SHIPPED overlay reality: IncludeInlandWater=false (lakes unowned → read as
    /// coast). Reuses the verified WorldZonesRuntime.Build façade — no new region logic here.
    ///
    /// Output JSON shape:
    ///   { seed, size, minIndex, zoneMeters,
    ///     depth:   [size*size] row-major DepthClass (0=Land,1=Shallow,2=Deep),
    ///     regionId:[size*size] row-major int region label (-1 = unassigned),
    ///     regions: [ {key,id,name,domBiome,centroidX,centroidZ,landZones,isCoastal} ],
    ///     seams:   [ {ax,az,bx,bz,terrain} ]   terrain: "LandLand"|"LandWater"|"WaterWater"|"LandVoid"|"WaterVoid"
    ///   }
    /// </summary>
    public static class CompositeDump
    {
        public static int Run(string seed, string outPath)
        {
            Console.WriteLine($"=== Composite dump — seed '{seed}' (IncludeInlandWater=false, shipped reality) ===");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            var world = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,            // shipped plugin default
                UseFeatureAwareBorders = true,         // the watershed borders we ship
                ComputeRegionInfo = true,              // need DominantBiome + centroids + names
                Namer = new MultiSchemaRegionNamer(),  // the default rich namer
            });

            ZoneGrid grid = world.Grid;
            int size = grid.Size;
            int min = grid.MinIndex;
            int[,] rid = world.RegionIdGrid;

            // id -> dominant-biome lookup for seam classification fallback (unused now, kept simple).
            Console.WriteLine($"size={size} minIndex={min} regions={world.Regions.Count}");

            var sb = new StringBuilder(8 * 1024 * 1024);
            sb.Append('{');
            sb.Append($"\"seed\":\"{Esc(seed)}\",");
            sb.Append($"\"size\":{size},");
            sb.Append($"\"minIndex\":{min},");
            sb.Append("\"zoneMeters\":64,");

            // ── depth grid (row-major [gy, gx], gy from 0..size-1 == zone min..max) ──
            sb.Append("\"depth\":[");
            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    int zx = gx + min, zy = gy + min;
                    DepthClass d = grid[zx, zy];
                    int dv = d == DepthClass.Land ? 0 : d == DepthClass.Shallow ? 1 : 2;
                    if (gy != 0 || gx != 0) sb.Append(',');
                    sb.Append(dv);
                }
            }
            sb.Append("],");

            // ── region-id grid (row-major [gy, gx]) ──
            sb.Append("\"regionId\":[");
            for (int gy = 0; gy < size; gy++)
            {
                for (int gx = 0; gx < size; gx++)
                {
                    if (gy != 0 || gx != 0) sb.Append(',');
                    sb.Append(rid[gy, gx].ToString(CultureInfo.InvariantCulture));
                }
            }
            sb.Append("],");

            // ── per-region facts ──
            sb.Append("\"regions\":[");
            bool first = true;
            foreach (RegionInfo r in world.Regions)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('{');
                sb.Append($"\"key\":\"{Esc(r.RegionKey)}\",");
                sb.Append($"\"id\":{r.TransientId},");
                sb.Append($"\"name\":\"{Esc(r.Name ?? "")}\",");
                sb.Append($"\"domBiome\":\"{r.DominantBiome}\",");
                sb.Append($"\"centroidX\":{Num(r.CentroidX)},");
                sb.Append($"\"centroidZ\":{Num(r.CentroidZ)},");
                sb.Append($"\"landZones\":{r.LandZones},");
                sb.Append($"\"isCoastal\":{(r.IsCoastal ? "true" : "false")}");
                sb.Append('}');
            }
            sb.Append("],");

            // ── classified seams: walk the zone-edge lattice ourselves so we can tag terrain ──
            // For each interior/edge lattice seam where region differs, classify by the DepthClass
            // of the two zones it divides. This is the EdgeTerrain axis Daniel asked about.
            sb.Append("\"seams\":[");
            bool firstSeam = true;
            // vertical edges: between zone (gx-1) and (gx), spanning corner (gx,gy)-(gx,gy+1)
            for (int gx = 0; gx <= size; gx++)
            for (int gy = 0; gy < size; gy++)
            {
                int lid = RidAt(rid, size, gx - 1, gy);
                int rid2 = RidAt(rid, size, gx, gy);
                if (lid == rid2) continue;
                int ld = DepthAt(grid, min, size, gx - 1, gy);
                int rd = DepthAt(grid, min, size, gx, gy);
                AppendSeam(sb, ref firstSeam, min, gx, gy, gx, gy + 1, lid, rid2, ld, rd);
            }
            // horizontal edges: between zone (gy-1) and (gy), spanning corner (gx,gy)-(gx+1,gy)
            for (int gy = 0; gy <= size; gy++)
            for (int gx = 0; gx < size; gx++)
            {
                int bid = RidAt(rid, size, gx, gy - 1);
                int aid = RidAt(rid, size, gx, gy);
                if (bid == aid) continue;
                int bd = DepthAt(grid, min, size, gx, gy - 1);
                int ad = DepthAt(grid, min, size, gx, gy);
                AppendSeam(sb, ref firstSeam, min, gx, gy, gx + 1, gy, bid, aid, bd, ad);
            }
            sb.Append(']');

            sb.Append('}');

            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length / 1024} KB)");
            return 0;
        }

        // region id at grid coord, -1 if out of bounds (== unassigned/void)
        private static int RidAt(int[,] rid, int size, int gx, int gy)
        {
            if (gx < 0 || gy < 0 || gx >= size || gy >= size) return -1;
            return rid[gy, gx];
        }

        // depth at grid coord; out of bounds = Deep (ocean/void), matches the world rim
        private static int DepthAt(ZoneGrid grid, int min, int size, int gx, int gy)
        {
            if (gx < 0 || gy < 0 || gx >= size || gy >= size) return 2; // Deep
            DepthClass d = grid[gx + min, gy + min];
            return d == DepthClass.Land ? 0 : d == DepthClass.Shallow ? 1 : 2;
        }

        // corner (gx,gy) in world metres: the 64*n+32 zone-corner lattice (minIndex*64 - 32 origin)
        private static double CornerX(int min, int g) => (g + min) * 64.0 - 32.0;

        private static void AppendSeam(StringBuilder sb, ref bool first, int min,
            int agx, int agy, int bgx, int bgy, int id1, int id2, int d1, int d2)
        {
            // terrain classification on the two divided zones' depth
            // land=0, shallow=1, deep=2. "water" = shallow OR deep.
            bool w1 = d1 != 0, w2 = d2 != 0;
            bool void1 = id1 < 0, void2 = id2 < 0;
            string terrain;
            if (!w1 && !w2) terrain = (void1 || void2) ? "LandVoid" : "LandLand";
            else if (w1 && w2) terrain = (void1 || void2) ? "WaterVoid" : "WaterWater";
            else terrain = "LandWater"; // one land, one water

            double ax = CornerX(min, agx), az = CornerX(min, agy);
            double bx = CornerX(min, bgx), bz = CornerX(min, bgy);

            if (!first) sb.Append(',');
            first = false;
            sb.Append('{');
            sb.Append($"\"ax\":{Num(ax)},\"az\":{Num(az)},\"bx\":{Num(bx)},\"bz\":{Num(bz)},");
            sb.Append($"\"a\":{id1},\"b\":{id2},");
            sb.Append($"\"terrain\":\"{terrain}\"");
            sb.Append('}');
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>
        /// Emit a fine-resolution terrain basemap (real GetHeight + GetBiome per pixel) as raw binary
        /// so the offline renderer can hillshade it like the in-game map and composite region layers
        /// on top. Layout (little-endian):
        ///   int32 size, int32 step(m), int32 range(m)
        ///   then size*size records, row-major, py=0 is NORTH (+range): { int16 heightM, uint8 biomeIdx }
        /// biomeIdx: 0 Ocean,1 Meadows,2 BlackForest,3 Swamp,4 Mountain,5 Plains,6 Mistlands,7 AshLands,8 DeepNorth
        /// </summary>
        public static int Basemap(string seed, string outPath, int step)
        {
            int range = 10000;
            int size = (range * 2 / step) + 1;
            Console.WriteLine($"=== Basemap — seed '{seed}' step={step}m size={size}x{size} ===");
            var wg = new WorldGenerator(seed);

            using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            bw.Write(size); bw.Write(step); bw.Write(range);

            for (int py = 0; py < size; py++)
            {
                float wz = range - py * step;   // north at top
                for (int px = 0; px < size; px++)
                {
                    float wx = -range + px * step;
                    BiomeType b = wg.GetBiome(wx, wz);
                    float h = wg.GetBiomeHeight(b, wx, wz);
                    bw.Write((short)Math.Max(-32000, Math.Min(32000, (int)Math.Round(h))));
                    bw.Write((byte)BiomeIdx(b));
                }
                if (py % 500 == 0) Console.WriteLine($"  {py * 100 / size}%");
            }
            Console.WriteLine($"Wrote {outPath} ({new FileInfo(outPath).Length / 1024 / 1024} MB)");
            return 0;
        }

        private static int BiomeIdx(BiomeType b) => b switch
        {
            BiomeType.Ocean => 0,
            BiomeType.Meadows => 1,
            BiomeType.BlackForest => 2,
            BiomeType.Swamp => 3,
            BiomeType.Mountain => 4,
            BiomeType.Plains => 5,
            BiomeType.Mistlands => 6,
            BiomeType.AshLands => 7,
            BiomeType.DeepNorth => 8,
            _ => 0,
        };
    }
}
