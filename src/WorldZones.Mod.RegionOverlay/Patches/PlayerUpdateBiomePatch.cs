using System;
using System.Reflection;
using HarmonyLib;

namespace WorldZones.Mod.RegionOverlay.Patches
{
    [HarmonyPatch]
    public static class PlayerUpdateBiomePatch
    {
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

            BiomeUpdated?.Invoke(position.x, position.z, playerId, playerName);
        }
    }
}
