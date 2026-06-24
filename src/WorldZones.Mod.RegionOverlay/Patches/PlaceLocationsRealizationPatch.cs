using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace WorldZones.Mod.RegionOverlay.Patches
{
    /// <summary>
    /// Fires the LIVE REALIZATION signal: a Postfix on <c>ZoneSystem.PlaceLocations(Vector2i zoneID, …)</c>,
    /// which the game calls when a zone first loads and realizes its registered location (decomp 97674 —
    /// it sets <c>m_placed = true</c> and spawns the prefab). After it runs, we read back the now-placed
    /// <c>LocationInstance</c> for that zone and raise <see cref="LocationRealized"/> with the realized
    /// prefab + world position. The plugin forwards that to
    /// <see cref="WorldZones.Runtime.LiveLocationOverlay.NotifyRealized"/>, which updates the gazetteer's
    /// placement status and pushes <c>OnLocationRealized</c> / <c>OnUniqueResolved</c>.
    ///
    /// <para>
    /// Why patch <c>PlaceLocations</c> and not <c>SpawnLocation</c>: <c>PlaceLocations</c> is the
    /// per-zone realization entry point keyed on <c>zoneID</c>, so the post-state read is a clean dictionary
    /// lookup. It is private, so the target is resolved by reflection (<see cref="AccessTools"/>), matching
    /// the existing <c>PlayerUpdateBiomePatch</c> style.
    /// </para>
    ///
    /// <para>The signal is best-effort + idempotent downstream: the game can re-run PlaceLocations for a
    /// zone, but <c>LiveLocationOverlay.NotifyRealized</c> is a no-op on an already-realized site.</para>
    /// </summary>
    [HarmonyPatch]
    public static class PlaceLocationsRealizationPatch
    {
        /// <summary>Raised after a zone's location is realized: (prefabName, worldX, worldZ).</summary>
        public static event Action<string, float, float>? LocationRealized;

        public static MethodBase TargetMethod()
        {
            Type zoneSystemType = AccessTools.TypeByName("ZoneSystem");
            // private void PlaceLocations(Vector2i zoneID, Vector3 zoneCenterPos, Transform parent,
            //                             Heightmap hmap, List<ClearArea> clearAreas, SpawnMode mode,
            //                             List<GameObject> spawnedObjects)
            return AccessTools.Method(zoneSystemType, "PlaceLocations");
        }

        public static void Postfix(object __instance, object zoneID)
        {
            if (__instance == null) return;
            try
            {
                // Read m_locationInstances[zoneID] post-call; if present + placed, it just realized.
                var instances = AccessTools.Field(__instance.GetType(), "m_locationInstances")
                    ?.GetValue(__instance) as IDictionary;
                if (instances == null) return;
                if (!instances.Contains(zoneID)) return;

                object inst = instances[zoneID];                       // ZoneSystem.LocationInstance (struct)
                if (inst == null) return;

                bool placed = (bool)(AccessTools.Field(inst.GetType(), "m_placed")?.GetValue(inst) ?? false);
                if (!placed) return;

                object location = AccessTools.Field(inst.GetType(), "m_location")?.GetValue(inst);
                object posObj = AccessTools.Field(inst.GetType(), "m_position")?.GetValue(inst);
                if (location == null || posObj == null) return;

                string prefabName = ResolvePrefabName(location);
                if (string.IsNullOrEmpty(prefabName)) return;

                // posObj is a UnityEngine.Vector3 — pull x,z via reflection to keep this file game-type-light.
                float x = (float)(AccessTools.Field(posObj.GetType(), "x")?.GetValue(posObj) ?? 0f);
                float z = (float)(AccessTools.Field(posObj.GetType(), "z")?.GetValue(posObj) ?? 0f);

                LocationRealized?.Invoke(prefabName, x, z);
            }
            catch
            {
                // Never let a realization-signal hiccup break worldgen. Best-effort overlay.
            }
        }

        /// <summary>ZoneLocation exposes m_prefabName (set in SetupLocations) and m_prefab (SoftReference
        /// with a Name). Prefer m_prefabName; fall back to m_prefab.Name.</summary>
        private static string ResolvePrefabName(object location)
        {
            var nameField = AccessTools.Field(location.GetType(), "m_prefabName");
            if (nameField?.GetValue(location) is string s && !string.IsNullOrEmpty(s)) return s;

            object prefab = AccessTools.Field(location.GetType(), "m_prefab")?.GetValue(location);
            if (prefab != null)
            {
                var nameProp = AccessTools.Property(prefab.GetType(), "Name");
                if (nameProp?.GetValue(prefab) is string ps) return ps;
            }
            return null;
        }
    }
}
