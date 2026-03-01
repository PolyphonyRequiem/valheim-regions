using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace WorldZones.Mod.RegionOverlay.Patches
{
    [HarmonyPatch]
    public static class PlayerUpdateBiomePatch
    {
        internal static ManualLogSource Log { get; set; }

        public static event Action<float, float, long, string> BiomeUpdated;

        public static MethodBase TargetMethod()
        {
            Type playerType = AccessTools.TypeByName("Player");
            return AccessTools.Method(playerType, "UpdateBiome", new[] { typeof(float) });
        }

        public static void Postfix(global::Player __instance)
        {
            if (__instance == null)
            {
                return;
            }

            var position = __instance.transform.position;
            long playerId = __instance.GetPlayerID();
            string playerName = __instance.GetPlayerName() ?? string.Empty;

            Log?.LogDebug($"[PlayerUpdateBiomePatch] Postfix fired. Player pos=({position.x:F0},{position.z:F0}) id={playerId} name='{playerName}'");
            BiomeUpdated?.Invoke(position.x, position.z, playerId, playerName);
        }
    }
}
