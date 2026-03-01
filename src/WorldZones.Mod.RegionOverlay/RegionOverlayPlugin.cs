using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using WorldZones.Mod.RegionOverlay.Integration;
using WorldZones.Mod.RegionOverlay.Patches;
using WorldZones.Regions;

namespace WorldZones.Mod.RegionOverlay
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class RegionOverlayPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "worldzones.regionoverlay";
        public const string PluginName = "WorldZones Region Overlay";
        public const string PluginVersion = "0.1.0";

        private const float UpdateIntervalSeconds = 0.5f;
        private const float DiagnosticLogIntervalSeconds = 5f;
        private const int DefaultTargetZonesPerRegion = 200;
        private const int DefaultMinComponentZonesForProto = 12;

        private MinimapLabelController minimapLabelController;
        private IRegionLookupService regionLookupService;
        private float updateTimer;
        private bool regionDataReady;
        private string lastWorldSeedName;
        private string lastLookupDebugText;
        private Vector2i? lastLoggedZone;
        private float diagnosticLogTimer;
        private ZoneGrid generatedGrid;
        private int[,] generatedRegionIdGrid;
        private int[,] generatedLandLabelGrid;
        private Dictionary<int, int> landComponentSizesById;
        private Harmony harmony;

        private void Awake()
        {
            this.harmony = new Harmony(PluginGuid);
            this.harmony.PatchAll(typeof(RegionOverlayPlugin).Assembly);
            MinimapUpdateBiomePatch.Log = this.Logger;
            MinimapUpdateBiomePatch.BiomeUpdated += this.OnMinimapBiomeUpdated;

            this.minimapLabelController = new MinimapLabelController();
            this.regionLookupService = NullRegionLookupService.Instance;
            this.lastLookupDebugText = string.Empty;
            this.lastLoggedZone = null;
            this.diagnosticLogTimer = 0f;
            this.Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void OnMinimapBiomeUpdated(float playerWorldX, float playerWorldZ, bool minimapVisible)
        {
            MinimapUpdateBiomePatch.OnAfterUpdateBiome(
                playerWorldX,
                playerWorldZ,
                minimapVisible,
                this.regionLookupService,
                this.minimapLabelController);
        }

        private void Update()
        {
            this.updateTimer += Time.deltaTime;
            this.diagnosticLogTimer += Time.deltaTime;
            if (this.updateTimer < UpdateIntervalSeconds)
            {
                return;
            }

            this.updateTimer = 0f;
            bool emitDiagnostics = this.diagnosticLogTimer >= DiagnosticLogIntervalSeconds;
            if (emitDiagnostics)
            {
                this.diagnosticLogTimer = 0f;
            }

            if (!this.regionDataReady)
            {
                this.TryInitializeRegionData();

                if (emitDiagnostics)
                {
                    bool wgReady = global::WorldGenerator.instance != null;
                    this.Logger.LogInfo(
                        $"Diag tick: waiting regionDataReady={this.regionDataReady} wgReady={wgReady} worldReady={ZNet.World != null} playerReady={Player.m_localPlayer != null} minimapReady={Minimap.instance != null}");
                }

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
                this.minimapLabelController.UpdateCurrentRegionLabel(false, null);

                if (emitDiagnostics)
                {
                    this.Logger.LogInfo(
                        $"Diag tick: no-player regionDataReady={this.regionDataReady} world='{this.lastWorldSeedName}' minimapReady={Minimap.instance != null}");
                }

                return;
            }

            var wg = global::WorldGenerator.instance;
            var pos = player.transform.position;
            var zone = ZoneGrid.WorldToZoneCoord(pos.x, pos.z);
            RegionLookupResult lookup = this.regionLookupService.ResolveCurrent(pos.x, pos.z);
            this.minimapLabelController.UpdateCurrentRegionLabel(minimapVisible, lookup);

            if (lookup != null && lookup.HasRegion)
            {
                this.lastLookupDebugText = string.Empty;
            }
            else
            {
                this.lastLookupDebugText = $"{lookup?.ResolutionReason ?? RegionResolutionReason.DataUnavailable} z({zone.x},{zone.y})";
                if (!this.lastLoggedZone.HasValue || this.lastLoggedZone.Value.x != zone.x || this.lastLoggedZone.Value.y != zone.y)
                {
                    this.Logger.LogInfo($"Lookup unresolved at zone ({zone.x},{zone.y}): {lookup?.ResolutionReason ?? RegionResolutionReason.DataUnavailable}");
                    this.lastLoggedZone = zone;
                }
            }

            if (emitDiagnostics)
            {
                string biomeText = "unknown";
                string biomeHeightText = "n/a";
                string rawHeightText = "n/a";

                if (wg != null)
                {
                    var biome = wg.GetBiome(pos.x, pos.z);
                    float height = wg.GetHeight(pos.x, pos.z);
                    biomeText = biome.ToString();
                    biomeHeightText = height.ToString("F3");
                    rawHeightText = height.ToString("F3");
                }

                string reasonText = lookup == null ? "null" : lookup.ResolutionReason.ToString();
                string regionIdText = lookup?.RegionId?.ToString() ?? "-";
                string regionNameText = string.IsNullOrWhiteSpace(lookup?.RegionName) ? "-" : lookup.RegionName;
                string zoneCellText = this.GetZoneCellDebug(zone);

                this.Logger.LogInfo(
                    $"Diag tick: world='{this.lastWorldSeedName}' pos=({pos.x:F1},{pos.z:F1}) zone=({zone.x},{zone.y}) biome={biomeText} hBiome={biomeHeightText} hRaw={rawHeightText} minimapVisible={minimapVisible} hasRegion={lookup?.HasRegion ?? false} reason={reasonText} regionId={regionIdText} regionName={regionNameText} {zoneCellText}");
            }
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
            int seedRng = GetStableHashCode(seedName);

            if (string.IsNullOrEmpty(seedName))
            {
                return;
            }

            this.Logger.LogInfo($"World detected: '{seedName}' (seed {seed}, seedRng {seedRng}). Generating region data...");
            this.LogWorldGeneratorOffsets(gameWorldGenerator);

            try
            {
                var sw = Stopwatch.StartNew();

                var provider = new ValheimWorldDataProvider(
                    seedName,
                    (wx, wz) => gameWorldGenerator.GetHeight(wx, wz));

                ZoneGrid grid = ProtoRegionGenerator.CreateClassifiedGrid(provider);
                List<LandComponent> landComponents = ComponentLabeler.LabelLand(grid, out int[,] landLabelGrid);

                ProtoRegionResult protoResult = ProtoRegionGenerator.GenerateLand(
                    grid,
                    landComponents,
                    DefaultTargetZonesPerRegion,
                    seedRng,
                    out int[,] regionIdGrid,
                    out _);

                var regionIds = new HashSet<int>();
                int rows = regionIdGrid.GetLength(0);
                int cols = regionIdGrid.GetLength(1);
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        int assignedId = regionIdGrid[y, x];
                        if (assignedId >= 0)
                        {
                            regionIds.Add(assignedId);
                        }
                    }
                }

                this.regionLookupService = new RegionLookupService(grid, regionIdGrid, seedName, regionIds);
                this.generatedGrid = grid;
                this.generatedRegionIdGrid = regionIdGrid;
                this.generatedLandLabelGrid = landLabelGrid;
                this.landComponentSizesById = new Dictionary<int, int>(landComponents.Count);
                foreach (var component in landComponents)
                {
                    this.landComponentSizesById[component.Id] = component.Zones.Count;
                }
                this.lastWorldSeedName = seedName;
                this.regionDataReady = true;
                this.lastLookupDebugText = string.Empty;
                this.lastLoggedZone = null;

                this.LogDebugZoneSnapshot(grid, regionIdGrid, landLabelGrid, provider, 2, 5);
                this.LogDebugZoneSnapshot(grid, regionIdGrid, landLabelGrid, provider, 2, 6);
                this.LogDebugZoneSnapshot(grid, regionIdGrid, landLabelGrid, provider, 3, 6);
                this.DumpRuntimeDebugMaps(seedName, grid, regionIdGrid);

                sw.Stop();
                this.Logger.LogInfo(
                    $"Region data ready: {protoResult.RegionCount} regions, land={protoResult.LandZoneCount}, unassigned={protoResult.UnassignedLandCount}, seededComponents={protoResult.SeededComponentCount}, minorIslets={protoResult.MinorIsletCount} in {sw.ElapsedMilliseconds}ms.");
            }
            catch (Exception ex)
            {
                this.Logger.LogError($"Failed to generate region data: {ex}");
            }
        }

        private static int GetStableHashCode(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return 0;
            }

            int hash = 5381;
            int hash2 = hash;

            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash = ((hash << 5) + hash) ^ str[i];
                if (i == str.Length - 1 || str[i + 1] == '\0')
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash + (hash2 * 1566083941);
        }

        private void ReclassifyZonesWithSubsampling(ZoneGrid grid, IWorldDataProvider provider)
        {
            float waterLevel = provider.WaterLevel;
            const float sampleOffset = ZoneGrid.ZoneSize * 0.25f;

            foreach (var coord in grid.AllCoords())
            {
                if (grid[coord] == DepthClass.Land)
                {
                    continue;
                }

                var center = ZoneGrid.ZoneCenter(coord);
                int landSamples = 0;

                if (provider.GetTerrainHeight(center.worldX, center.worldZ) >= waterLevel) landSamples++;
                if (provider.GetTerrainHeight(center.worldX - sampleOffset, center.worldZ - sampleOffset) >= waterLevel) landSamples++;
                if (provider.GetTerrainHeight(center.worldX - sampleOffset, center.worldZ + sampleOffset) >= waterLevel) landSamples++;
                if (provider.GetTerrainHeight(center.worldX + sampleOffset, center.worldZ - sampleOffset) >= waterLevel) landSamples++;
                if (provider.GetTerrainHeight(center.worldX + sampleOffset, center.worldZ + sampleOffset) >= waterLevel) landSamples++;

                if (landSamples >= 3)
                {
                    grid[coord] = DepthClass.Land;
                }
                else if (landSamples > 0)
                {
                    grid[coord] = DepthClass.Shallow;
                }
            }
        }

        private void ExpandRegionsIntoAdjacentShallowZones(ZoneGrid grid, int[,] regionIdGrid)
        {
            int size = grid.Size;
            int min = grid.MinIndex;
            var original = (int[,])regionIdGrid.Clone();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (original[y, x] >= 0)
                    {
                        continue;
                    }

                    int zoneX = x + min;
                    int zoneY = y + min;
                    var coord = new Vector2i(zoneX, zoneY);
                    if (grid[coord] != DepthClass.Shallow)
                    {
                        continue;
                    }

                    int chosen = this.ChooseAdjacentAssignedRegionId(original, x, y, size);
                    if (chosen >= 0)
                    {
                        regionIdGrid[y, x] = chosen;
                    }
                }
            }
        }

        private int ChooseAdjacentAssignedRegionId(int[,] original, int x, int y, int size)
        {
            int left = x > 0 ? original[y, x - 1] : -1;
            int right = x < size - 1 ? original[y, x + 1] : -1;
            int down = y > 0 ? original[y - 1, x] : -1;
            int up = y < size - 1 ? original[y + 1, x] : -1;

            if (left >= 0)
            {
                return left;
            }

            if (right >= 0)
            {
                return right;
            }

            if (down >= 0)
            {
                return down;
            }

            if (up >= 0)
            {
                return up;
            }

            return -1;
        }

        private void LogWorldGeneratorOffsets(global::WorldGenerator wg)
        {
            try
            {
                var t = wg.GetType();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                float o0 = (float)t.GetField("m_offset0", flags).GetValue(wg);
                float o1 = (float)t.GetField("m_offset1", flags).GetValue(wg);
                float o2 = (float)t.GetField("m_offset2", flags).GetValue(wg);
                float o3 = (float)t.GetField("m_offset3", flags).GetValue(wg);
                float o4 = (float)t.GetField("m_offset4", flags).GetValue(wg);
                int version = (int)t.GetField("m_version", flags).GetValue(wg);
                var worldField = t.GetField("m_world", flags);
                bool isMenu = false;
                if (worldField != null)
                {
                    var world = worldField.GetValue(wg);
                    var menuField = world.GetType().GetField("m_menu", BindingFlags.Instance | BindingFlags.Public);
                    if (menuField != null)
                    {
                        isMenu = (bool)menuField.GetValue(world);
                    }
                }
                this.Logger.LogInfo($"WorldGenerator offsets: o0={o0} o1={o1} o2={o2} o3={o3} o4={o4} version={version} isMenu={isMenu}");
            }
            catch (Exception ex)
            {
                this.Logger.LogWarning($"Could not read WorldGenerator offsets: {ex.Message}");
            }
        }

        private void LogDebugZoneSnapshot(ZoneGrid grid, int[,] regionIdGrid, int[,] landLabelGrid, IWorldDataProvider provider, int zoneX, int zoneY)
        {
            var coord = new Vector2i(zoneX, zoneY);
            if (!grid.InBounds(coord))
            {
                this.Logger.LogInfo($"Debug zone snapshot z({zoneX},{zoneY}): out-of-bounds");
                return;
            }

            int gx = zoneX - grid.MinIndex;
            int gy = zoneY - grid.MinIndex;

            var center = ZoneGrid.ZoneCenter(coord);
            float waterLevel = provider.WaterLevel;
            float centerH = provider.GetTerrainHeight(center.worldX, center.worldZ);
            float offset = ZoneGrid.ZoneSize * 0.25f;
            float hNW = provider.GetTerrainHeight(center.worldX - offset, center.worldZ + offset);
            float hNE = provider.GetTerrainHeight(center.worldX + offset, center.worldZ + offset);
            float hSW = provider.GetTerrainHeight(center.worldX - offset, center.worldZ - offset);
            float hSE = provider.GetTerrainHeight(center.worldX + offset, center.worldZ - offset);

            int regionId = regionIdGrid[gy, gx];
            int label = landLabelGrid[gy, gx];
            DepthClass depth = grid[coord];

            this.Logger.LogInfo(
                $"Debug zone snapshot z({zoneX},{zoneY}): depth={depth} regionId={regionId} landLabel={label} waterLevel={waterLevel:F2} hCenter={centerH:F3} hNW={hNW:F3} hNE={hNE:F3} hSW={hSW:F3} hSE={hSE:F3}");
        }

        private void DumpRuntimeDebugMaps(string seedName, ZoneGrid grid, int[,] regionIdGrid)
        {
            try
            {
                string debugDir = Path.Combine(Paths.PluginPath, "WorldZones", "debug");
                Directory.CreateDirectory(debugDir);

                int size = grid.Size;
                byte[] regionRgb = new byte[size * size * 3];
                byte[] depthRgb = new byte[size * size * 3];

                for (int i = 0; i < regionRgb.Length; i += 3)
                {
                    regionRgb[i] = 20;
                    regionRgb[i + 1] = 20;
                    regionRgb[i + 2] = 30;
                }

                for (int gy = 0; gy < size; gy++)
                {
                    for (int gx = 0; gx < size; gx++)
                    {
                        int zx = gx + grid.MinIndex;
                        int zy = gy + grid.MinIndex;
                        int py = size - 1 - gy;
                        int offset = (py * size + gx) * 3;

                        var depth = grid[zx, zy];
                        if (depth == DepthClass.Land)
                        {
                            depthRgb[offset] = 90;
                            depthRgb[offset + 1] = 140;
                            depthRgb[offset + 2] = 90;
                        }
                        else if (depth == DepthClass.Shallow)
                        {
                            depthRgb[offset] = 90;
                            depthRgb[offset + 1] = 110;
                            depthRgb[offset + 2] = 180;
                        }
                        else
                        {
                            depthRgb[offset] = 20;
                            depthRgb[offset + 1] = 20;
                            depthRgb[offset + 2] = 40;
                        }

                        int rid = regionIdGrid[gy, gx];
                        if (rid >= 0)
                        {
                            var c = ComponentColor(rid);
                            regionRgb[offset] = c.r;
                            regionRgb[offset + 1] = c.g;
                            regionRgb[offset + 2] = c.b;
                        }
                    }
                }

                DrawZoneMarker(regionRgb, size, size, grid, 0, 0, new RgbColor(255, 255, 255));
                DrawZoneMarker(depthRgb, size, size, grid, 0, 0, new RgbColor(255, 255, 255));
                DrawZonePixel(regionRgb, size, size, grid, 2, 5, new RgbColor(160, 160, 160));
                DrawZonePixel(regionRgb, size, size, grid, 2, 6, new RgbColor(160, 160, 160));
                DrawZonePixel(regionRgb, size, size, grid, 3, 6, new RgbColor(160, 160, 160));
                DrawZonePixel(depthRgb, size, size, grid, 2, 5, new RgbColor(160, 160, 160));
                DrawZonePixel(depthRgb, size, size, grid, 2, 6, new RgbColor(160, 160, 160));
                DrawZonePixel(depthRgb, size, size, grid, 3, 6, new RgbColor(160, 160, 160));

                string regionsPath = Path.Combine(debugDir, $"{seedName}_runtime_proto_regions.png");
                string depthPath = Path.Combine(debugDir, $"{seedName}_runtime_depth_classes.png");
                RuntimePngWriter.Write(regionsPath, size, size, regionRgb);
                RuntimePngWriter.Write(depthPath, size, size, depthRgb);

                this.Logger.LogInfo($"Runtime debug maps written: '{regionsPath}', '{depthPath}'");
            }
            catch (Exception ex)
            {
                this.Logger.LogWarning($"Failed to write runtime debug maps: {ex.Message}");
            }
        }

        private static void DrawZoneMarker(byte[] rgbData, int width, int height, ZoneGrid grid, int zoneX, int zoneY, RgbColor color)
        {
            int gx = zoneX - grid.MinIndex;
            int gy = zoneY - grid.MinIndex;
            if (gx < 0 || gx >= width || gy < 0 || gy >= height)
            {
                return;
            }

            int py = height - 1 - gy;
            DrawCross(rgbData, width, height, gx, py, 3, color);
        }

        private static void DrawZonePixel(byte[] rgbData, int width, int height, ZoneGrid grid, int zoneX, int zoneY, RgbColor color)
        {
            int gx = zoneX - grid.MinIndex;
            int gy = zoneY - grid.MinIndex;
            if (gx < 0 || gx >= width || gy < 0 || gy >= height)
            {
                return;
            }

            int py = height - 1 - gy;
            SetPixel(rgbData, width, height, gx, py, color);
        }

        private static void DrawCross(byte[] rgbData, int width, int height, int x, int y, int armLength, RgbColor color)
        {
            for (int d = -armLength; d <= armLength; d++)
            {
                SetPixel(rgbData, width, height, x + d, y, color);
                SetPixel(rgbData, width, height, x, y + d, color);
            }
        }

        private static void SetPixel(byte[] rgbData, int width, int height, int x, int y, RgbColor color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            int offset = (y * width + x) * 3;
            rgbData[offset] = color.r;
            rgbData[offset + 1] = color.g;
            rgbData[offset + 2] = color.b;
        }

        private static RgbColor ComponentColor(int id)
        {
            double hue = (id * 0.618033988749895) % 1.0;
            return HslToRgb(hue, 0.7, 0.55);
        }

        private static RgbColor HslToRgb(double h, double s, double l)
        {
            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h * 6.0) % 2.0 - 1.0));
            double m = l - c / 2.0;

            double r;
            double g;
            double b;
            int sector = (int)(h * 6.0) % 6;
            switch (sector)
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }

            return new RgbColor((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private readonly struct RgbColor
        {
            public readonly byte r;
            public readonly byte g;
            public readonly byte b;

            public RgbColor(byte r, byte g, byte b)
            {
                this.r = r;
                this.g = g;
                this.b = b;
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
            this.generatedGrid = null;
            this.generatedRegionIdGrid = null;
            this.generatedLandLabelGrid = null;
            this.landComponentSizesById = null;
            this.lastWorldSeedName = null;
            this.lastLookupDebugText = string.Empty;
            this.lastLoggedZone = null;
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
                object world = worldField?.GetValue(worldGenerator);
                if (world == null)
                {
                    return false;
                }

                var menuField = world.GetType().GetField("m_menu", BindingFlags.Instance | BindingFlags.Public);
                object isMenuValue = menuField?.GetValue(world);
                return isMenuValue is bool isMenu && isMenu;
            }
            catch
            {
                return false;
            }
        }

        private string GetZoneCellDebug(Vector2i zone)
        {
            if (this.generatedGrid == null || this.generatedRegionIdGrid == null)
            {
                return "gridCell=n/a";
            }

            if (!this.generatedGrid.InBounds(zone))
            {
                return "gridCell=out-of-bounds";
            }

            int centerId = this.generatedRegionIdGrid[zone.y - this.generatedGrid.MinIndex, zone.x - this.generatedGrid.MinIndex];
            int leftId = this.TryGetRegionIdAtZone(zone.x - 1, zone.y);
            int rightId = this.TryGetRegionIdAtZone(zone.x + 1, zone.y);
            int upId = this.TryGetRegionIdAtZone(zone.x, zone.y + 1);
            int downId = this.TryGetRegionIdAtZone(zone.x, zone.y - 1);

            DepthClass depthClass = this.generatedGrid[zone];
            int landLabel = this.TryGetLandLabelAtZone(zone.x, zone.y);
            int componentSize = this.TryGetLandComponentSize(landLabel);
            bool seededEligible = componentSize >= DefaultMinComponentZonesForProto;

            return $"gridCell={centerId} depth={depthClass} landLabel={landLabel} componentSize={componentSize} seededEligible={seededEligible} neighbors(L={leftId},R={rightId},U={upId},D={downId})";
        }

        private int TryGetLandLabelAtZone(int zoneX, int zoneY)
        {
            if (this.generatedGrid == null || this.generatedLandLabelGrid == null)
            {
                return int.MinValue;
            }

            var coord = new Vector2i(zoneX, zoneY);
            if (!this.generatedGrid.InBounds(coord))
            {
                return int.MinValue;
            }

            return this.generatedLandLabelGrid[zoneY - this.generatedGrid.MinIndex, zoneX - this.generatedGrid.MinIndex];
        }

        private int TryGetLandComponentSize(int landLabel)
        {
            if (landLabel < 0 || this.landComponentSizesById == null)
            {
                return -1;
            }

            if (this.landComponentSizesById.TryGetValue(landLabel, out int size))
            {
                return size;
            }

            return -1;
        }

        private int TryGetRegionIdAtZone(int zoneX, int zoneY)
        {
            if (this.generatedGrid == null || this.generatedRegionIdGrid == null)
            {
                return int.MinValue;
            }

            var coord = new Vector2i(zoneX, zoneY);
            if (!this.generatedGrid.InBounds(coord))
            {
                return int.MinValue;
            }

            return this.generatedRegionIdGrid[zoneY - this.generatedGrid.MinIndex, zoneX - this.generatedGrid.MinIndex];
        }

        private void OnDestroy()
        {
            MinimapUpdateBiomePatch.BiomeUpdated -= this.OnMinimapBiomeUpdated;
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