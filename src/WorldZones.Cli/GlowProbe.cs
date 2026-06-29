using System;
using System.Collections.Generic;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Cli
{
    /// <summary>
    /// THROWAWAY diagnostic (2026-06-28) for the "Atlas sea glow is all ONE colour, not per-region
    /// biome tint" bug. Reproduces the LIVE plugin halo path headlessly — same RegionBuildOptions, same
    /// regionIdAt closure (Math.Round(wx/zone)-ridMin), same CoastHaloField.Build(bandMeters,
    /// depthFadeMeters, regionIdAt), same BuildBiomePalettes(DominantBiome→Glow) — then reports:
    ///   (a) how many DISTINCT region ids the field attributes to water texels (NearestRegionIdAt),
    ///   (b) how many DISTINCT glow COLOURS those map to via the glow palette,
    ///   (c) the fill-palette spread for comparison (the half that VISIBLY works in-game).
    /// If (a) collapses to ~1 → attribution bug. If (a) is rich but (b)→1 → palette/index bug. If both
    /// are rich here but flat in-game → the bug is in the Unity BakeBiome/SetBiomePalette wiring, not the
    /// pure path. Pure ground truth, no GPU, no walk.
    /// </summary>
    public static class GlowProbe
    {
        public static int Run(string seed)
        {
            Console.WriteLine($"=== Glow probe — seed '{seed}' (live plugin halo path, headless) ===");

            var worldGen = new WorldGenerator(seed);
            var sampler = new PortWorldSampler(worldGen, seed);
            RegionWorld regionWorld = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
            {
                IncludeInlandWater = false,
                UseFeatureAwareBorders = true,
                ComputeRegionInfo = true,
                Namer = new MultiSchemaRegionNamer(),
            });

            int regionCount = regionWorld.Regions.Count;
            Console.WriteLine($"regions={regionCount}");

            // ── Build the live regionIdAt closure verbatim (RegionOverlayPlugin.cs ~L455-465) ──
            int[,] ridGrid = regionWorld.RegionIdGrid;
            int ridMin = regionWorld.Grid.MinIndex;
            int ridH = ridGrid.GetLength(0), ridW = ridGrid.GetLength(1);
            const double zone = 64.0, halfZone = 32.0;
            Func<double, double, int> regionIdAt = (wx, wz) =>
            {
                int gx = (int)Math.Round(wx / zone) - ridMin;
                int gy = (int)Math.Round(wz / zone) - ridMin;
                if (gx < 0 || gy < 0 || gx >= ridW || gy >= ridH) return -1;
                return ridGrid[gy, gx];
            };

            // ── Build the halo field exactly as the plugin does (16 m cell, sea-level height) ──
            const double haloCell = 16.0;
            int gh = ridGrid.GetLength(0), gw = ridGrid.GetLength(1);
            double haloOriginX = ridMin * zone - halfZone;
            double haloOriginZ = ridMin * zone - halfZone;
            int haloW = (int)(gw * zone / haloCell);
            int haloH = (int)(gh * zone / haloCell);
            var haloHeight = new HeightScalarField(sampler, CoastHaloField.SeaLevel);
            CoastHaloField haloFld = CoastHaloField.Build(
                haloHeight, haloOriginX, haloOriginZ, haloCell, haloW, haloH,
                bandMeters: CoastHaloField.DefaultBandMeters, depthFadeMeters: 14.0,
                regionIdAt: regionIdAt);

            // ── (a) distinct region ids attributed to water texels, plus how many water texels got tagged ──
            var distinctRids = new HashSet<int>();
            long taggedWater = 0, totalWater = 0;
            for (int gy = 0; gy < haloH; gy++)
                for (int gx = 0; gx < haloW; gx++)
                {
                    int rid = haloFld.NearestRegionIdAt(gy, gx);
                    if (rid >= 0) { distinctRids.Add(rid); taggedWater++; }
                    // crude water count: a texel that is NOT land at sea level
                    if (haloHeight.Sample(haloOriginX + (gx + 0.5) * haloCell,
                                          haloOriginZ + (gy + 0.5) * haloCell) < CoastHaloField.SeaLevel)
                        totalWater++;
                }
            Console.WriteLine($"(a) NearestRegionIdAt: {distinctRids.Count} distinct region ids on water; "
                            + $"{taggedWater} texels tagged of ~{totalWater} water texels "
                            + $"({(totalWater > 0 ? 100.0 * taggedWater / totalWater : 0):F1}% of water reached by the band BFS)");

            // ── Build BOTH palettes exactly as BuildBiomePalettes does (fill wash + glow sat-floored) ──
            int maxLabel = -1;
            foreach (RegionInfo r in regionWorld.Regions) if (r.TransientId > maxLabel) maxLabel = r.TransientId;
            var fillPal = new (byte r, byte g, byte b)[maxLabel + 1];
            var glowPal = new (byte r, byte g, byte b)[maxLabel + 1];
            for (int i = 0; i <= maxLabel; i++) { fillPal[i] = (150, 150, 150); glowPal[i] = (150, 150, 150); }
            foreach (RegionInfo r in regionWorld.Regions)
            {
                if (r.TransientId < 0 || r.TransientId > maxLabel) continue;
                fillPal[r.TransientId] = BiomeRenderPalette.Wash(r.DominantBiome);
                glowPal[r.TransientId] = BiomeRenderPalette.Glow(r.DominantBiome);
            }

            // ── (b) distinct glow colours ACTUALLY used by the attributed water texels ──
            var usedGlowColours = new HashSet<(byte, byte, byte)>();
            foreach (int rid in distinctRids)
                if (rid >= 0 && rid <= maxLabel) usedGlowColours.Add(glowPal[rid]);
            Console.WriteLine($"(b) distinct GLOW colours those ids map to: {usedGlowColours.Count}");

            // ── (b2) THE FALLBACK-GOLD QUESTION: of texels that actually GLOW (alpha>0), how many get a
            // real per-region biome colour vs fall back to the gold HaloColor (235,180,95) because their
            // nearest coast is unincorporated (rid<0)? This is the "yellow glow on non-region stuff" bug. ──
            long glowTexels = 0, biomeColoured = 0, fallbackGold = 0;
            for (int gy = 1; gy < haloH - 1; gy++)
                for (int gx = 1; gx < haloW - 1; gx++)
                {
                    double alpha = haloFld.Alpha(CoastHaloMode.Seaward, gy, gx);
                    if (alpha <= 0) continue;           // not glowing
                    glowTexels++;
                    int rid = haloFld.NearestRegionIdAt(gy, gx);
                    if (rid >= 0 && rid <= maxLabel) biomeColoured++;
                    else fallbackGold++;
                }
            Console.WriteLine($"(b2) GLOWING texels (Seaward, alpha>0): {glowTexels}; "
                            + $"biome-coloured={biomeColoured} ({(glowTexels>0?100.0*biomeColoured/glowTexels:0):F1}%), "
                            + $"FALLBACK-GOLD={fallbackGold} ({(glowTexels>0?100.0*fallbackGold/glowTexels:0):F1}%) "
                            + $"← gold = coast whose nearest land is UNINCORPORATED (rid<0)");

            // ── (c) fill-palette spread for comparison (the visibly-working half) ──
            var distinctFill = new HashSet<(byte, byte, byte)>();
            var distinctGlowAll = new HashSet<(byte, byte, byte)>();
            var biomeSpread = new HashSet<BiomeType>();
            foreach (RegionInfo r in regionWorld.Regions)
            {
                distinctFill.Add(BiomeRenderPalette.Wash(r.DominantBiome));
                distinctGlowAll.Add(BiomeRenderPalette.Glow(r.DominantBiome));
                biomeSpread.Add(r.DominantBiome);
            }
            Console.WriteLine($"(c) across ALL {regionCount} regions: {biomeSpread.Count} distinct DominantBiomes, "
                            + $"{distinctFill.Count} distinct FILL colours, {distinctGlowAll.Count} distinct GLOW colours");

            // ── Verdict heuristic ──
            Console.WriteLine();
            if (distinctRids.Count <= 1)
                Console.WriteLine("VERDICT: ATTRIBUTION collapse — NearestRegionIdAt returns ~one id. Bug is in the regionIdAt closure or the BFS seeding, NOT the palette.");
            else if (usedGlowColours.Count <= 1)
                Console.WriteLine("VERDICT: PALETTE/INDEX collapse — many region ids attributed but they map to ~one glow colour. Bug is in glow palette build or indexing.");
            else
                Console.WriteLine("VERDICT: pure path is RICH (many ids → many colours). The flat in-game glow is in the Unity wiring (SetBiomePalette/BakeBiome/glowPalette delivery), not this pure path.");

            return 0;
        }
    }
}
