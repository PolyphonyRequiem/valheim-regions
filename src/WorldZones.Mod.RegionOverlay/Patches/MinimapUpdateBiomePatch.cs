using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using WorldZones.Mod.RegionOverlay.Integration;
using WorldZones.Regions;

namespace WorldZones.Mod.RegionOverlay.Patches
{
    [HarmonyPatch]
    public static class MinimapUpdateBiomePatch
    {
        internal static ManualLogSource Log { get; set; }

        public static event Action<float, float, bool> BiomeUpdated;

        public static MethodBase TargetMethod()
        {
            Type minimapType = AccessTools.TypeByName("Minimap");
            Type playerType = AccessTools.TypeByName("Player");
            return AccessTools.Method(minimapType, "UpdateBiome", new[] { playerType });
        }

        public static void Postfix(global::Player player)
        {
            if (player == null)
            {
                return;
            }

            var position = player.transform.position;
            bool minimapVisible = global::Minimap.instance != null && !global::Minimap.IsOpen();
            Log?.LogDebug($"[MinimapUpdateBiomePatch] Postfix fired. Player pos: ({position.x:F0}, {position.z:F0}), minimapVisible={minimapVisible}");
            BiomeUpdated?.Invoke(position.x, position.z, minimapVisible);
        }

        public static void OnAfterUpdateBiome(
            float playerWorldX,
            float playerWorldZ,
            bool minimapVisible,
            IRegionLookupService lookupService,
            MinimapLabelController labelController)
        {
            if (lookupService == null || labelController == null)
            {
                return;
            }

            RegionLookupResult lookup = lookupService.ResolveCurrent(playerWorldX, playerWorldZ);
            labelController.UpdateCurrentRegionLabel(minimapVisible, lookup);
        }
    }
}
