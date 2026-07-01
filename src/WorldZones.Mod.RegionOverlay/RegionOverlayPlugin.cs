using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using WorldZones.Mod.RegionOverlay.Integration;
using WorldZones.Mod.RegionOverlay.Overlay;
using WorldZones.Mod.RegionOverlay.Patches;
using WorldZones.Mod.RegionOverlay.Persistence;
using WorldZones.Regions;
using WorldZones.Runtime;
using WorldZones.Runtime.Geometry;
using WorldZones.WorldGen;

namespace WorldZones.Mod.RegionOverlay
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RegionOverlayPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "worldzones.regionoverlay";
        public const string PluginName = "WorldZones Region Overlay";
        public const string PluginVersion = "0.1.0";

        private const float UpdateIntervalSeconds = 0.5f;
        private const int DefaultTargetZonesPerRegion = 200;

        private MinimapLabelController? minimapLabelController;
        private IRegionLookupService? regionLookupService;
        private float updateTimer;
        private bool regionDataReady;
        private string? lastWorldSeedName;
        private DiscoveryStore? discoveryStore;
        private Harmony? harmony;

        // ── Tier-3 region overlay (borders-only live draw onto the large map) ───────────────────────
        // The style dial (resting default = Borders), the controller that draws it, and the hotkey
        // that cycles it. The controller mounts under Minimap.m_pinRootLarge and fog-gates via a
        // reflected Minimap.IsExplored. See docs/design/region-render-seam.md (step 3).
        private RegionOverlayController? overlayController;
        // Resting default = Atlas (overlay ON at load). Decided 2026-06-28: Atlas is THE mode; F8 now
        // toggles Atlas ⇄ Vanilla(off). Was Borders (a now-cut line mode) — leaving it Borders would have
        // shown a dead, zoom-buggy mode on load until the first F8. Flip this to Vanilla if you'd rather
        // the overlay start OFF.
        private RegionOverlayStyle overlayStyle = RegionOverlayStyle.Atlas;
        private const KeyCode OverlayCycleKey = KeyCode.F8;
        // Coast-halo dial (F7) — independent of the F8 style. Resting default = Off (opt-in soft fade).
        private CoastHaloMode haloMode = CoastHaloMode.Off;
        private const KeyCode HaloCycleKey = KeyCode.F7;
        // Glow-intensity dial (F6) — scales the F7 gold halo + Atlas glow so Daniel can A/B "kinda faint"
        // in-world. Walks named stops Full→Strong→Medium→Faint→Whisper→(wrap). Resting = Full (ship default).
        private const KeyCode GlowIntensityCycleKey = KeyCode.F6;
        private int glowIntensityStop;   // index into GlowIntensityStops; 0 = Full (1.0)
        // Named intensity stops (label + multiplier). Full=1.0 is the current ship value; the lower stops
        // bracket the "make it faint" hypothesis. Tune freely — pure reversible render dial.
        private static readonly (string Label, float Mul)[] GlowIntensityStops =
        {
            ("Full", 1.00f),
            ("Strong", 0.70f),
            ("Medium", 0.45f),
            ("Faint", 0.25f),
            ("Whisper", 0.12f),
        };
        private bool overlayWorldCached;

        /// <summary>
        /// FORK B toggle (docs/design/spike-004-shared-seam-primitive.md): when true, the map fill is
        /// reassembled from ONE shared seam per region-pair (the same curve the ink draws), so fill and ink
        /// agree by construction. false ⇒ the shipped independent-refine fill (byte-identical). Flip to true
        /// + redeploy to ship; gated on Daniel's in-world walk. static readonly (not const) so the off path
        /// carries no unreachable-code warning.
        /// </summary>
        private static readonly bool UseSharedSeamFill = true;
        // Live realization overlay — non-null only when a location-bearing gazetteer has been built
        // (a consumer that opts into RegionBuildOptions.LocationSource). Until then the realization
        // signal is received but has nothing to update, so it is a no-op. See location-gazetteer-api.md.
        private WorldZones.Runtime.LiveLocationOverlay? locationOverlay;

        private void Awake()
        {
            this.harmony = new Harmony(PluginGuid);
            this.harmony.PatchAll(typeof(RegionOverlayPlugin).Assembly);
            MinimapUpdateBiomePatch.BiomeUpdated += this.OnMinimapBiomeUpdated;
            PlayerUpdateBiomePatch.BiomeUpdated += this.OnPlayerBiomeUpdated;
            // The realization Postfix on ZoneSystem.PlaceLocations pushes (prefab, x, z) here as zones load.
            PlaceLocationsRealizationPatch.LocationRealized += this.OnLocationRealized;

            this.minimapLabelController = new MinimapLabelController();
            this.regionLookupService = NullRegionLookupService.Instance;
            this.discoveryStore = new DiscoveryStore(this.Logger);
            this.overlayController = new RegionOverlayController(this.Logger);
            if (!this.overlayController.FogAvailable)
            {
                this.Logger.LogWarning(
                    "RegionOverlay: fog gate (Minimap.IsExplored) did not resolve — the borders overlay will " +
                    "stay disabled rather than draw the whole unfogged map.");
            }
            this.Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        /// <summary>
        /// Forwards a live realization signal (from <see cref="PlaceLocationsRealizationPatch"/>) into the
        /// gazetteer overlay, which updates placement status and raises OnLocationRealized /
        /// OnUniqueResolved. No-op until a location-bearing gazetteer is built (the current minimap-label
        /// path is point-query-only and builds none) — the hook is wired now so enabling the live
        /// gazetteer is a one-line overlay assignment, not new plumbing.
        /// </summary>
        private void OnLocationRealized(string prefabName, float worldX, float worldZ)
        {
            this.locationOverlay?.NotifyRealized(prefabName, worldX, worldZ);
        }

        private void OnMinimapBiomeUpdated(float playerWorldX, float playerWorldZ, bool minimapVisible, bool fullMapVisible, float hoverWorldX, float hoverWorldZ)
        {
            if (this.regionLookupService == null || this.minimapLabelController == null)
            {
                return;
            }

            MinimapUpdateBiomePatch.OnAfterUpdateBiome(
                playerWorldX,
                playerWorldZ,
                minimapVisible,
                fullMapVisible,
                hoverWorldX,
                hoverWorldZ,
                this.regionLookupService,
                this.minimapLabelController);
        }

        private void OnPlayerBiomeUpdated(float playerWorldX, float playerWorldZ, long playerId, string playerName)
        {
            if (!this.regionDataReady || this.regionLookupService == null || this.discoveryStore == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(this.lastWorldSeedName))
            {
                return;
            }

            string worldId = this.lastWorldSeedName!;

            RegionLookupResult lookup = this.regionLookupService.ResolveCurrent(playerWorldX, playerWorldZ);
            if (lookup == null || !lookup.HasRegion || string.IsNullOrWhiteSpace(lookup.RegionName))
            {
                return;
            }

            string playerKey = this.ResolvePlayerIdentity(playerId, playerName);
            if (string.IsNullOrWhiteSpace(playerKey))
            {
                return;
            }

            bool isFirstDiscovery = this.discoveryStore.CheckAndRecordDiscovery(
                worldId,
                playerKey,
                lookup.RegionName,
                lookup.RegionId,
                lookup.RegionKey);

            if (!isFirstDiscovery)
            {
                return;
            }

            this.ShowDiscoveryBanner(lookup.RegionName);
            this.Logger.LogInfo(
                $"Region discovered: world='{this.lastWorldSeedName}' player='{playerKey}' regionId={lookup.RegionId?.ToString() ?? "-"} regionName={lookup.RegionName}");
        }

        private void Update()
        {
            // Hotkey + overlay render run EVERY frame (not on the 0.5s throttle): a throttled key poll
            // drops presses, and the overlay must track pan/zoom smoothly. Cheap when the map is closed
            // (the controller early-returns) — the per-seam fog filter only runs while the large map is open.
            this.PumpRegionOverlay();

            this.updateTimer += Time.deltaTime;
            if (this.updateTimer < UpdateIntervalSeconds)
            {
                return;
            }

            this.updateTimer = 0f;

            if (!this.regionDataReady)
            {
                this.TryInitializeRegionData();
                return;
            }

            // If the world changed (e.g. player returned to menu and loaded a different world), reset
            if (global::WorldGenerator.instance == null)
            {
                this.ResetRegionData();
                return;
            }

            if (IsMenuWorldGenerator(global::WorldGenerator.instance))
            {
                this.ResetRegionData();
                return;
            }

            bool minimapVisible = Minimap.instance != null && !Minimap.IsOpen();
            var player = Player.m_localPlayer;
            if (player == null)
            {
                this.minimapLabelController?.UpdateCurrentRegionLabel(false, null);
                this.minimapLabelController?.UpdateHoverRegionLabel(false, null);
                return;
            }

            var pos = player.transform.position;
            RegionLookupResult? lookup = this.regionLookupService?.ResolveCurrent(pos.x, pos.z);
            this.minimapLabelController?.UpdateCurrentRegionLabel(minimapVisible, lookup);
        }

        private void TryInitializeRegionData()
        {
            var gameWorldGenerator = global::WorldGenerator.instance;
            if (gameWorldGenerator == null)
            {
                return;
            }

            if (IsMenuWorldGenerator(gameWorldGenerator))
            {
                return;
            }

            World world = ZNet.World;
            if (world == null || world.m_menu)
            {
                return;
            }

            string seedName = world.m_seedName;
            int seed = world.m_seed;
            string worldIdentity = seed.ToString();
            int seedRng = seed;

            this.Logger.LogInfo($"World detected: '{seedName}' (seed {seed}). Generating region data...");

            try
            {
                var sw = Stopwatch.StartNew();

                // Route through the shared runtime façade (the bootstrap that used to be inlined here
                // now lives in WorldZonesRuntime.Build, deduped with the CLI + gazetteer). The minimap
                // label plugin is a POINT-QUERY consumer: ComputeRegionInfo=false skips the rich
                // gazetteer aggregation + naming. But feature-aware borders (below) DO sample biome +
                // river per land zone ONCE during the cost-field build at world load — independent of
                // ComputeRegionInfo — so the in-game tessellation matches the headless gazetteer.
                //
                // worldIdentity MUST stay the numeric seed string (world.m_seed.ToString()): discovery
                // persistence file paths are keyed on it. Do not "fix" it to the seed NAME here — that
                // would orphan every player's saved discovery state.
                var sampler = new ValheimWorldSampler(
                    worldIdentity,
                    (wx, wz) => gameWorldGenerator.GetHeight(wx, wz),
                    // Raw cast is valid: the port's BiomeType mirrors Valheim's Heightmap.Biome bits
                    // EXACTLY (Meadows=1..Mistlands=512 — see VegetationCatalogue.cs). Used by the
                    // feature-aware cost-field build below; if a future 1.0 enum renumber lands, prefer a
                    // name-mapped bridge so it fails loudly instead of silently mis-tagging biomes.
                    (wx, wz) => (BiomeType)(int)gameWorldGenerator.GetBiome(wx, wz),
                    // River seam — forwards to the game's pregenerated rivers so in-game borders use
                    // rivers as a wall, matching the headless gazetteer (which gets rivers via the port).
                    // WorldGenerator.GetRiverWeight is PRIVATE in vanilla (decomp-verified), so go via a
                    // cached reflected handle rather than a direct call (which would not compile).
                    BuildRiverResolver(gameWorldGenerator));

                RegionWorld regionWorld = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
                {
                    TargetZonesPerRegion = DefaultTargetZonesPerRegion,
                    SeedRng = seedRng,
                    // ComputeRegionInfo=true: run the gazetteer aggregation + MultiSchemaRegionNamer so
                    // the lookup service returns RICH multi-schema region names (threaded RegionKey→Name)
                    // on the minimap + large-map hover labels, instead of the legacy flat catalogue. This
                    // was deliberately OFF for point-query speed; it adds the GazetteerBuilder aggregation
                    // + biome-sampler pass at world load. Daniel accepts the cost for the public demo
                    // (locked 2026-06-25). The added load time is measured + reported in review.
                    ComputeRegionInfo = true,
                    // Feature-aware borders ON: borders fall on biome edges / shores / rivers (watershed
                    // Dijkstra), matching the gazetteer dataset so the map a player sees == the dataset.
                    // See docs/design/region-borders.md. This is what Daniel asked for: the new
                    // boundaries used in BOTH generation and what players walk.
                    UseFeatureAwareBorders = true,
                });

                this.regionLookupService = regionWorld.Lookup;
                this.lastWorldSeedName = string.IsNullOrWhiteSpace(seedName)
                    ? worldIdentity
                    : seedName;
                this.regionDataReady = true;

                // Tier-3 overlay: build + cache the renderable boundary geometry ONCE per world load
                // (AC-T3-DRAW-1 — not per frame). Source the id→key map from ProtoResult.Regions
                // (always populated regardless of ComputeRegionInfo; ProtoRegion.Id is the grid label,
                // ProtoRegion.RegionKey the durable key — the SAME mapping GazetteerBuilder uses), and
                // call the Tier-1 extractor directly. ProtoResult is the stable source here, so this
                // is unaffected by the ComputeRegionInfo flag (now true for rich naming). The live
                // `sampler` is passed through so Layer-0 can refine the arcs off the SAME field source
                // the headless gazetteer uses (AC-V2-L0-3).
                this.CacheOverlayGeometry(regionWorld, sampler);

                sw.Stop();
                ProtoRegionResult protoResult = regionWorld.ProtoResult;
                this.Logger.LogInfo(
                    $"Region data ready: {protoResult.RegionCount} regions, land={protoResult.LandZoneCount}, unassigned={protoResult.UnassignedLandCount}, seededComponents={protoResult.SeededComponentCount}, minorIslets={protoResult.MinorIsletCount} in {sw.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                this.Logger.LogError($"Failed to generate region data: {ex}");
            }
        }

        private void ResetRegionData()
        {
            if (this.regionDataReady)
            {
                this.Logger.LogInfo($"World unloaded (was '{this.lastWorldSeedName}'). Clearing region data.");
            }

            this.regionDataReady = false;
            this.regionLookupService = NullRegionLookupService.Instance;
            this.lastWorldSeedName = null;

            // Tier-3 overlay: tear down the mount + drop the cached geometry so the next world rebuilds it.
            this.overlayController?.Unmount();
            this.overlayWorldCached = false;
        }

        /// <summary>
        /// Per-frame overlay pump: poll the cycle hotkey + render the current style. Cheap when the
        /// large map is closed (the controller early-returns without touching the fog gate). The hotkey
        /// advances the style enum (AC-T3-DRAW-2); the render selects ink/fill per the locked table.
        /// </summary>
        private void PumpRegionOverlay()
        {
            if (this.overlayController == null) return;

            // Cycle hotkey — every frame (GetKeyDown is edge-triggered). Suppressed while a text field
            // has focus so it doesn't fire mid-typing (console / rename / chat).
            if (Input.GetKeyDown(OverlayCycleKey) && !IsTextInputActive())
            {
                this.overlayStyle = this.overlayStyle.Next();
                this.Logger.LogInfo($"RegionOverlay: style → {this.overlayStyle} (hotkey {OverlayCycleKey}).");
            }

            // Coast-halo hotkey (F7) — independent dial: Off → Seaward → Inland → Off. Same focus guard.
            if (Input.GetKeyDown(HaloCycleKey) && !IsTextInputActive())
            {
                this.haloMode = NextHaloMode(this.haloMode);
                this.Logger.LogInfo($"RegionOverlay: coast halo → {this.haloMode} (hotkey {HaloCycleKey}).");
            }

            // Glow-intensity hotkey (F6) — walks the named stops and pushes the multiplier to the controller,
            // which re-bakes the halo at the new peak alpha. Lets Daniel A/B "kinda faint" live. Same guard.
            if (Input.GetKeyDown(GlowIntensityCycleKey) && !IsTextInputActive())
            {
                this.glowIntensityStop = (this.glowIntensityStop + 1) % GlowIntensityStops.Length;
                var stop = GlowIntensityStops[this.glowIntensityStop];
                this.overlayController.GlowIntensity = stop.Mul;
                this.Logger.LogInfo(
                    $"RegionOverlay: glow intensity → {stop.Label} ({stop.Mul:0.00}×) (hotkey {GlowIntensityCycleKey}).");
            }

            // Render only once a world's geometry is cached; otherwise keep the overlay hidden.
            if (this.regionDataReady && this.overlayWorldCached)
            {
                this.overlayController.Render(this.overlayStyle, this.haloMode);
            }
            else
            {
                this.overlayController.Render(RegionOverlayStyle.Vanilla, CoastHaloMode.Off); // hides content, keeps host alive
            }
        }

        /// <summary>Advance the coast-halo dial: Off → Seaward → Inland → Off.</summary>
        private static CoastHaloMode NextHaloMode(CoastHaloMode m) =>
            m == CoastHaloMode.Off ? CoastHaloMode.Seaward
            : m == CoastHaloMode.Seaward ? CoastHaloMode.Inland
            : CoastHaloMode.Off;

        /// <summary>True if a uGUI/text field currently has keyboard focus (so the hotkey shouldn't fire).</summary>
        private static bool IsTextInputActive()
        {
            try
            {
                if (global::Console.instance != null && global::Console.IsVisible()) return true;
                if (global::Chat.instance != null && global::Chat.instance.HasFocus()) return true;
                if (global::TextInput.IsVisible()) return true;
            }
            catch
            {
                // Defensive: any of these singletons can be absent in some scenes — never block the hotkey on a throw.
            }
            return false;
        }

        /// <summary>
        /// Build the renderable boundary geometry for the loaded world and hand it (plus the refined
        /// contour-hugging arcs + the region-id grid) to the overlay controller, ONCE per world load.
        /// The id→key map comes from <c>ProtoResult.Regions</c> (always populated, independent of
        /// <c>ComputeRegionInfo</c>; <c>ProtoRegion.Id</c> is the grid label, <c>ProtoRegion.RegionKey</c>
        /// the durable key) so the extracted seams carry real durable keys regardless of the naming flag.
        /// Mirrors <c>RegionWorld.BuildBoundaryGraph</c>'s logic against the populated proto set.
        ///
        /// <para>Layer 0 (v2): the same graph is refined into smooth coast ∪ biome-seam arcs via the
        /// SAME two refiners + default isos the headless <c>gazetteer --boundaries</c> path uses
        /// (<c>Gazetteer.WriteBoundaries</c>) — so the in-world arcs equal the shipped
        /// <c>{seed}_boundaries.json</c> dataset (AC-V2-L0-3). Built once here, never per frame
        /// (AC-V2-L0-1). The fields read the live <paramref name="sampler"/> directly.</para>
        /// </summary>
        private void CacheOverlayGeometry(RegionWorld regionWorld, IWorldSampler sampler)
        {
            if (this.overlayController == null) return;
            if (!this.overlayController.FogAvailable)
            {
                // Fail-closed: without the fog mask we must not draw, so skip caching entirely.
                this.overlayWorldCached = false;
                return;
            }

            try
            {
                var idToKey = new Dictionary<int, string>();
                foreach (ProtoRegion r in regionWorld.ProtoResult.Regions)
                {
                    if (!idToKey.ContainsKey(r.Id)) idToKey[r.Id] = r.RegionKey;
                }

                RegionBoundaryGraph graph = RegionBoundaryExtractor.Extract(
                    regionWorld.RegionIdGrid, regionWorld.Grid.MinIndex, idToKey);

                // Layer 0: refine the SAME graph into smooth arcs (coast ∪ biome-seam) off the live
                // sampler — byte-for-byte the headless gazetteer path (Gazetteer.WriteBoundaries:294-297:
                // same two refiners, same fields, same 25 m default coast iso). One flat List<RefinedBorder>.
                var heightField = new HeightScalarField(sampler);   // default CoastIso = 25 m
                var biomeField = new BiomeCategoryField(sampler);
                var arcs = new List<RefinedBorder>();
                arcs.AddRange(RegionBoundaryRefiner.RefineCoastlinesSmoothed(graph, heightField));
                arcs.AddRange(RegionBoundaryRefiner.RefineBiomeSeams(graph, biomeField));

                // ── Main-only strip (render-time, 2026-06-28): keep each region's LARGEST contiguous land
                // component, drop detached chunks → unowned. Visual-only: the engine's RegionIdGrid is
                // untouched (gazetteer/names/count unchanged); we hand the overlay a stripped COPY so fill +
                // glow stop teleporting across water. Engine-strip is a separate domain task. Safety-checked
                // on Niflheim: 0 regions collapse to empty, 164 preserved, worst loses 81% but survives.
                int[,] mainGrid = StripToMainComponent(regionWorld.RegionIdGrid);
                this.overlayController.SetWorld(graph, arcs, mainGrid, regionWorld.Grid.MinIndex);

                // Coast-halo field (F7 soft fade) — built once over the SAME world window the region grid
                // covers, at a 16 m cell for a smooth fade (4× finer than the 64 m zone lattice). Reads the
                // live sampler's height via the same HeightScalarField the coast line uses, but at SEA LEVEL
                // (30 m) so the fade's shoreline is the true waterline. true-ocean-only is handled inside
                // CoastHaloField (flood from the window edge). Pure + static per world → bake once here.
                const double haloCell = 16.0;
                const double zone = 64.0, halfZone = 32.0;
                int gh = regionWorld.RegionIdGrid.GetLength(0), gw = regionWorld.RegionIdGrid.GetLength(1);
                double haloOriginX = regionWorld.Grid.MinIndex * zone - halfZone;
                double haloOriginZ = regionWorld.Grid.MinIndex * zone - halfZone;
                int haloW = (int)(gw * zone / haloCell);
                int haloH = (int)(gh * zone / haloCell);
                var haloHeight = new HeightScalarField(sampler, CoastHaloField.SeaLevel);
                // Atlas per-region glow needs the halo field to know WHICH region each coast belongs to.
                // Supply a world(x,z) → grid-label sampler over the MAIN-ONLY grid (matches the fill, so the
                // glow attributes only to a region's contiguous body — orphan-island coasts read unowned).
                int[,] ridGrid = mainGrid;
                int ridMin = regionWorld.Grid.MinIndex;
                int ridH = ridGrid.GetLength(0), ridW = ridGrid.GetLength(1);
                System.Func<double, double, int> regionIdAt = (wx, wz) =>
                {
                    // world metre → zone index → grid index (round to nearest zone centre).
                    int gx = (int)System.Math.Round(wx / zone) - ridMin;
                    int gy = (int)System.Math.Round(wz / zone) - ridMin;
                    if (gx < 0 || gy < 0 || gx >= ridW || gy >= ridH) return -1;
                    return ridGrid[gy, gx];
                };
                CoastHaloField haloFld = CoastHaloField.Build(
                    haloHeight, haloOriginX, haloOriginZ, haloCell, haloW, haloH,
                    // Band 96 m + depth-gate 14 m: the validated Atlas glow that hugs the coast and dies
                    // over deep water (no open-sea haze). docs/design/region-atlas-render.md. The F7
                    // gold halo on the other styles also picks up the tighter band — an improvement.
                    bandMeters: CoastHaloField.DefaultBandMeters, depthFadeMeters: 14.0,
                    regionIdAt: regionIdAt,
                    // C-cost apron: terrain-shaped extent — sprawls over shallow archipelago, retracts at
                    // deep drop-offs (deepWeight 8 m = depth that doubles per-step cost). Decided 2026-06-28.
                    costFloodDeepWeight: 8.0,
                    // Phase-2 partition (2026-06-29, Daniel option A): includeLakes makes ALL water fade —
                    // enclosed lakes / interior pockets get a fade instead of a blank hole ("makes lakes
                    // look more interesting"). The swamp floor + isSwamp make the fade's LAND test match
                    // RegionFillMaskBaker's exactly, so a rescued-swamp texel the fill paints is land here
                    // too (the fade leaves it alone) — fill XOR fade, no double-layer, no gap.
                    includeLakes: true,
                    swampLandFloor: 27.5,   // matches RegionBuildOptions.SwampLandFloorMeters (A/B-locked 2026-06-30: 28.5 holed bog interiors, 27.5 = clean coastal trim)
                    isSwamp: (wx, wz) => sampler.GetBiome((float)wx, (float)wz) == WorldZones.WorldGen.BiomeType.Swamp);
                this.overlayController.SetHaloField(haloFld);

                // ── Phase-2 FINE FILL MASK (2026-06-29): the terrestrial fill follows the AUTHORITATIVE
                // REFINED RING, not the 64 m zone grid. Daniel's swamp walk showed the raster fill edge was
                // a 64 m zone-membership staircase (72% zone-limited) that stops short of the real coast.
                // The fix: build the refined ring boundary (coast edges snapped to the 30 m waterline iso,
                // land-seam edges to the biome flip, smoothed-last with the watertight guards) and rasterize
                // it (point-in-ring, holes subtracted) into the SAME fine int[,] the height-clip mask used —
                // so the controller / BakeFine / fog-gate / fill↔fade partition are all UNCHANGED. The fill
                // edge becomes the smooth contour instead of the zone staircase. Built once per world.
                const double fineTexel = 16.0;
                int fineSub = (int)(zone / fineTexel);   // 4
                var ringCoastField = new HeightScalarField(sampler, HeightScalarField.SeaLevel); // iso = 30 m waterline
                var ringSeamField = new BiomeCategoryField(sampler);
                var ringKeyToLabel = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.Ordinal);
                foreach (RegionInfo r in regionWorld.Regions) ringKeyToLabel[r.RegionKey] = r.TransientId;
                int ringRidW = mainGrid.GetLength(1), ringRidH = mainGrid.GetLength(0);
                int ringMin = regionWorld.Grid.MinIndex;
                RegionRingRefiner.RegionIdAt ringRidAt = (wx, wz) =>
                {
                    int zx = (int)System.Math.Round(wx / zone) - ringMin;
                    int zy = (int)System.Math.Round(wz / zone) - ringMin;
                    return (zx < 0 || zy < 0 || zx >= ringRidW || zy >= ringRidH) ? -1 : mainGrid[zy, zx];
                };
                // Refine the rings off the MAIN-ONLY graph (same grid the fill+glow attribute to) so the
                // fill footprint matches the rest of the overlay. Stripped orphan chunks stay unincorporated.
                var mainIdToKey = new System.Collections.Generic.Dictionary<int, string>();
                foreach (RegionInfo r in regionWorld.Regions) if (!mainIdToKey.ContainsKey(r.TransientId)) mainIdToKey[r.TransientId] = r.RegionKey;
                RegionBoundaryGraph mainGraph = RegionBoundaryExtractor.Extract(mainGrid, ringMin, mainIdToKey);
                RefinedRegionBoundary ringBoundary = RefinedRegionBoundary.Build(
                    mainGraph, ringKeyToLabel, ringRidAt, ringCoastField, ringSeamField);

                // ── FORK B (2026-07-01): the SHARED-SEAM fill. Off by default (flip to true + redeploy to
                // ship, same gate as every other border lever). When on, the fill ring is reassembled from
                // ONE shared seam per region-pair — the SAME curve the ink draws — so fill and ink agree by
                // construction (kills the ~16 m weave) instead of being three independent refinements. Any
                // region that fails to reassemble falls back to the independent-refine ringBoundary above, so
                // the fill is never holed. false ⇒ byte-identical to the shipped independent-refine path.
                // See docs/design/spike-004-shared-seam-primitive.md.
                // (static readonly, not const, so flipping it needs no code edit here and the off path
                //  compiles without an unreachable-code warning.)
                if (UseSharedSeamFill)
                {
                    var seamSet = SharedSeamSet.Build(mainGraph, ringCoastField, ringSeamField);
                    ringBoundary = SharedSeamBoundary.ToRefinedRegionBoundary(seamSet, mainGraph, fallback: ringBoundary);
                    this.Logger.LogInfo($"RegionOverlay: fork-B shared-seam fill ON — {seamSet.Seams.Count} shared seams.");
                }

                var ringFillBaker = new RegionRingFillBaker(ringBoundary, ringKeyToLabel);
                int[,] fineFillMask = ringFillBaker.Bake(ringRidH, ringRidW, ringMin, fineSub);
                this.overlayController.SetFineFillMask(fineFillMask, fineTexel);
                this.Logger.LogInfo($"RegionOverlay: ring fill baked — {ringBoundary.Rings.Count} refined rings "
                             + $"(rolledToRaw={ringBoundary.RolledBackToRawCount}, skippedSmall={ringBoundary.SkippedSmallCount}).");

                // Atlas biome palettes (fill wash + sat-floored coast glow), one colour per grid label
                // (RegionInfo.TransientId) from each region's DominantBiome (ComputeRegionInfo=true
                // guarantees it). Built once per world; the controller scales alpha at draw.
                var (fillPalette, glowPalette) = BuildBiomePalettes(regionWorld);
                this.overlayController.SetBiomePalette(fillPalette, glowPalette);

                this.overlayWorldCached = true;
            }
            catch (Exception ex)
            {
                this.Logger.LogError($"RegionOverlay: failed to cache boundary geometry: {ex}");
                this.overlayWorldCached = false;
            }
        }

        /// <summary>
        /// Render-time main-only strip: return a COPY of the region-id grid where each region keeps only
        /// its LARGEST 8-connected land component; detached chunks become −1 (unowned). The engine grid is
        /// never mutated — this is a visual contiguity pass so fill + glow don't teleport across water to
        /// island fragments that share an id. Decided 2026-06-28; engine-strip is a separate domain task.
        /// </summary>
        private static int[,] StripToMainComponent(int[,] grid)
        {
            int h = grid.GetLength(0), w = grid.GetLength(1);
            var compId = new int[h, w]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) compId[y, x] = -1;
            var seen = new bool[h, w];
            var biggest = new System.Collections.Generic.Dictionary<int, (int sz, int id)>();
            int cid = 0;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int lab = grid[y, x]; if (lab < 0 || seen[y, x]) continue;
                var st = new System.Collections.Generic.Stack<(int, int)>(); st.Push((y, x)); seen[y, x] = true; int n = 0;
                while (st.Count > 0) { var (cy, cx) = st.Pop(); n++; compId[cy, cx] = cid;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int ny = cy + dy, nx = cx + dx;
                        if (ny < 0 || nx < 0 || ny >= h || nx >= w || seen[ny, nx] || grid[ny, nx] != lab) continue; seen[ny, nx] = true; st.Push((ny, nx)); } }
                if (!biggest.TryGetValue(lab, out var b) || n > b.sz) biggest[lab] = (n, cid);
                cid++;
            }
            var outg = new int[h, w];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int lab = grid[y, x]; outg[y, x] = (lab >= 0 && biggest[lab].id == compId[y, x]) ? lab : -1; }
            return outg;
        }

        /// <summary>
        /// Build the Atlas biome palettes — both indexed by grid label (<c>RegionInfo.TransientId</c>):
        /// <list type="bullet">
        ///   <item><b>fill</b>: each region's <c>DominantBiome</c> wash colour
        ///     (<see cref="BiomeRenderPalette.Wash"/>) for the low-alpha territory tint.</item>
        ///   <item><b>glow</b>: the sat-floored coast-glow colour (<see cref="BiomeRenderPalette.Glow"/>)
        ///     so muted biomes still read at the shore.</item>
        /// </list>
        /// Sized to max label + 1 so the Tier-2 bakers index them directly. Labels with no rich region
        /// get a neutral grey (shouldn't happen with ComputeRegionInfo=true, but never index OOB).
        /// </summary>
        private static (List<UnityEngine.Color32> fill, List<UnityEngine.Color32> glow) BuildBiomePalettes(
            RegionWorld regionWorld)
        {
            int maxLabel = -1;
            foreach (RegionInfo r in regionWorld.Regions)
                if (r.TransientId > maxLabel) maxLabel = r.TransientId;

            var fill = new List<UnityEngine.Color32>(maxLabel + 1);
            var glow = new List<UnityEngine.Color32>(maxLabel + 1);
            for (int i = 0; i <= maxLabel; i++)
            {
                fill.Add(new UnityEngine.Color32(150, 150, 150, 255));
                glow.Add(new UnityEngine.Color32(150, 150, 150, 255));
            }

            foreach (RegionInfo r in regionWorld.Regions)
            {
                if (r.TransientId < 0 || r.TransientId > maxLabel) continue;
                var (fr, fg, fb) = BiomeRenderPalette.Wash(r.DominantBiome);
                var (gr, gg, gb) = BiomeRenderPalette.Glow(r.DominantBiome);
                fill[r.TransientId] = new UnityEngine.Color32(fr, fg, fb, 255);
                glow[r.TransientId] = new UnityEngine.Color32(gr, gg, gb, 255);
            }
            return (fill, glow);
        }

        /// <summary>
        /// Build a river resolver over the LIVE game's private <c>WorldGenerator.GetRiverWeight</c> via a
        /// cached reflected handle (it is private in vanilla — decomp-verified). Returns null if the
        /// method can't be found (e.g. a future 1.0 signature change), so feature-aware borders degrade
        /// to biome/shore only in-game rather than crashing — and the gazetteer (which has rivers via the
        /// port) would then differ; we log a warning so that mismatch is visible, not silent.
        /// </summary>
        private ValheimWorldSampler.RiverResolver BuildRiverResolver(global::WorldGenerator gameWorldGenerator)
        {
            var method = AccessTools.Method(typeof(global::WorldGenerator), "GetRiverWeight",
                new[] { typeof(float), typeof(float), typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });
            if (method == null)
            {
                this.Logger.LogWarning(
                    "WorldGenerator.GetRiverWeight not found via reflection — in-game borders will use " +
                    "biome/shore only (no river seam). This will DIFFER from the gazetteer dataset, which " +
                    "has rivers. Likely a Valheim version signature change.");
                return null;
            }

            return (float wx, float wz, out float weight, out float width) =>
            {
                var args = new object[] { wx, wz, 0f, 0f };
                method.Invoke(gameWorldGenerator, args);
                weight = (float)args[2];
                width = (float)args[3];
            };
        }

        private static bool IsMenuWorldGenerator(global::WorldGenerator worldGenerator)
        {
            if (worldGenerator == null)
            {
                return false;
            }

            try
            {
                var worldField = worldGenerator.GetType().GetField("m_world", BindingFlags.Instance | BindingFlags.NonPublic);
                object? world = worldField?.GetValue(worldGenerator);
                if (world == null)
                {
                    return false;
                }

                var menuField = world.GetType().GetField("m_menu", BindingFlags.Instance | BindingFlags.Public);
                object? isMenuValue = menuField?.GetValue(world);
                return isMenuValue is bool isMenu && isMenu;
            }
            catch
            {
                return false;
            }
        }

        private string ResolvePlayerIdentity(long playerId, string playerName)
        {
            if (playerId > 0)
            {
                return playerId.ToString();
            }

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                return playerName.Trim();
            }

            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null)
            {
                return string.Empty;
            }

            long localPlayerId = localPlayer.GetPlayerID();
            if (localPlayerId > 0)
            {
                return localPlayerId.ToString();
            }

            string fallbackName = localPlayer.GetPlayerName();
            return string.IsNullOrWhiteSpace(fallbackName) ? string.Empty : fallbackName.Trim();
        }

        private void ShowDiscoveryBanner(string regionName)
        {
            if (string.IsNullOrWhiteSpace(regionName))
            {
                return;
            }

            if (MessageHud.instance == null)
            {
                return;
            }

            MessageHud.instance.ShowBiomeFoundMsg($"Discovered: {regionName}", playStinger: true);
        }

        private void OnDestroy()
        {
            MinimapUpdateBiomePatch.BiomeUpdated -= this.OnMinimapBiomeUpdated;
            PlayerUpdateBiomePatch.BiomeUpdated -= this.OnPlayerBiomeUpdated;
            this.overlayController?.Unmount();
            this.harmony?.UnpatchSelf();
        }

        private sealed class NullRegionLookupService : IRegionLookupService
        {
            public static readonly NullRegionLookupService Instance = new NullRegionLookupService();

            public RegionLookupResult ResolveCurrent(float worldX, float worldZ)
            {
                return new RegionLookupResult
                {
                    HasRegion = false,
                    RegionId = null,
                    RegionName = string.Empty,
                    ResolutionReason = RegionResolutionReason.DataUnavailable
                };
            }
        }
    }
}