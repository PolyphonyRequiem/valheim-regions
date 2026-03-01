using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using WorldZones.Mod.RegionOverlay.Integration;
using WorldZones.Regions;

namespace WorldZones.Mod.RegionOverlay.Patches
{
    [HarmonyPatch]
    public static class MinimapUpdateBiomePatch
    {
        internal static ManualLogSource Log { get; set; }

        public static event Action<float, float, bool, bool, float, float> BiomeUpdated;

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

            global::Minimap minimap = global::Minimap.instance;
            var position = player.transform.position;
            bool fullMapVisible = minimap != null && global::Minimap.IsOpen();
            bool minimapVisible = minimap != null && !fullMapVisible;

            bool hasHoverPosition = TryGetHoverWorldPosition(minimap, out float hoverWorldX, out float hoverWorldZ);
            if (!hasHoverPosition)
            {
                hoverWorldX = float.NaN;
                hoverWorldZ = float.NaN;
            }

            Log?.LogDebug($"[MinimapUpdateBiomePatch] Postfix fired. Player pos: ({position.x:F0}, {position.z:F0}), minimapVisible={minimapVisible}, fullMapVisible={fullMapVisible}, hasHoverPosition={hasHoverPosition}");
            BiomeUpdated?.Invoke(position.x, position.z, minimapVisible, fullMapVisible, hoverWorldX, hoverWorldZ);
        }

        public static void OnAfterUpdateBiome(
            float playerWorldX,
            float playerWorldZ,
            bool minimapVisible,
            bool fullMapVisible,
            float hoverWorldX,
            float hoverWorldZ,
            IRegionLookupService lookupService,
            MinimapLabelController labelController)
        {
            if (lookupService == null || labelController == null)
            {
                return;
            }

            RegionLookupResult lookup = lookupService.ResolveCurrent(playerWorldX, playerWorldZ);
            labelController.UpdateCurrentRegionLabel(minimapVisible, lookup);

            RegionLookupResult hoverLookup = null;
            if (fullMapVisible && !float.IsNaN(hoverWorldX) && !float.IsNaN(hoverWorldZ))
            {
                hoverLookup = lookupService.ResolveCurrent(hoverWorldX, hoverWorldZ);
            }

            labelController.UpdateHoverRegionLabel(fullMapVisible, hoverLookup);
        }

        private static bool TryGetHoverWorldPosition(global::Minimap minimap, out float worldX, out float worldZ)
        {
            worldX = float.NaN;
            worldZ = float.NaN;

            if (minimap == null || !global::Minimap.IsOpen() || minimap.m_mapImageLarge == null)
            {
                return false;
            }

            if (!IsHoverExplored(minimap))
            {
                return false;
            }

            Vector3 screenPoint = Input.mousePresent
                ? Input.mousePosition
                : new Vector3(Screen.width / 2f, Screen.height / 2f, 0f);

            var rectTransform = minimap.m_mapImageLarge.transform as RectTransform;
            if (rectTransform == null)
            {
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, null, out Vector2 localPoint))
            {
                return false;
            }

            Vector2 normalized = Rect.PointToNormalized(rectTransform.rect, localPoint);
            if (normalized.x < 0f || normalized.x > 1f || normalized.y < 0f || normalized.y > 1f)
            {
                return false;
            }

            Rect uvRect = minimap.m_mapImageLarge.uvRect;
            float mapX = uvRect.xMin + normalized.x * uvRect.width;
            float mapY = uvRect.yMin + normalized.y * uvRect.height;

            int halfTexture = minimap.m_textureSize / 2;
            float sampleX = mapX * minimap.m_textureSize;
            float sampleY = mapY * minimap.m_textureSize;
            sampleX = (sampleX - halfTexture) * minimap.m_pixelSize;
            sampleY = (sampleY - halfTexture) * minimap.m_pixelSize;

            worldX = sampleX;
            worldZ = sampleY;
            return true;
        }

        private static bool IsHoverExplored(global::Minimap minimap)
        {
            if (minimap == null || minimap.m_biomeNameLarge == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(minimap.m_biomeNameLarge.text);
        }
    }
}
