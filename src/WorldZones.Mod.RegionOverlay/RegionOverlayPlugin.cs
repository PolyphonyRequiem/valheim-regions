using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using WorldZones.Mod.RegionOverlay.Integration;
using WorldZones.Mod.RegionOverlay.Patches;
using WorldZones.Mod.RegionOverlay.Persistence;
using WorldZones.Regions;
using WorldZones.Runtime;
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
                // gazetteer aggregation + naming, so this stays behaviour-identical to the old inline
                // path (height-only classification, no biome sampling) — and the GetBiome resolver
                // below is never invoked at this flag setting.
                //
                // worldIdentity MUST stay the numeric seed string (world.m_seed.ToString()): discovery
                // persistence file paths are keyed on it. Do not "fix" it to the seed NAME here — that
                // would orphan every player's saved discovery state.
                var sampler = new ValheimWorldSampler(
                    worldIdentity,
                    (wx, wz) => gameWorldGenerator.GetHeight(wx, wz),
                    // Raw cast is valid: the port's BiomeType mirrors Valheim's Heightmap.Biome bits
                    // EXACTLY (Meadows=1..Mistlands=512 — see VegetationCatalogue.cs). Never invoked at
                    // ComputeRegionInfo=false; if a consumer flips that flag, prefer a name-mapped bridge
                    // so a future 1.0 enum renumber fails loudly instead of silently mis-tagging biomes.
                    (wx, wz) => (BiomeType)(int)gameWorldGenerator.GetBiome(wx, wz));

                RegionWorld regionWorld = WorldZonesRuntime.Build(sampler, new RegionBuildOptions
                {
                    TargetZonesPerRegion = DefaultTargetZonesPerRegion,
                    SeedRng = seedRng,
                    ComputeRegionInfo = false,    // point-query-only: no aggregation, no naming, no biome
                });

                this.regionLookupService = regionWorld.Lookup;
                this.lastWorldSeedName = string.IsNullOrWhiteSpace(seedName)
                    ? worldIdentity
                    : seedName;
                this.regionDataReady = true;

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