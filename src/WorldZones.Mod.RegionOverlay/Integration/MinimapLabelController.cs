using TMPro;
using UnityEngine;
using WorldZones.Regions;

namespace WorldZones.Mod.RegionOverlay.Integration
{
    public sealed class MinimapLabelController
    {
        private Minimap boundMinimap;
        private TMP_Text regionLabelSmall;

        public bool IsVisible { get; private set; }

        public string CurrentRegionNameText { get; private set; } = string.Empty;

        public void UpdateCurrentRegionLabel(bool minimapVisible, RegionLookupResult lookupResult)
        {
            if (!this.EnsureSmallLabel())
            {
                this.IsVisible = false;
                this.CurrentRegionNameText = string.Empty;
                return;
            }

            if (!minimapVisible)
            {
                this.IsVisible = false;
                this.CurrentRegionNameText = string.Empty;
                this.regionLabelSmall.gameObject.SetActive(false);
                return;
            }

            if (lookupResult == null || !lookupResult.HasRegion || string.IsNullOrWhiteSpace(lookupResult.RegionName))
            {
                this.IsVisible = false;
                this.CurrentRegionNameText = string.Empty;
                this.regionLabelSmall.gameObject.SetActive(false);
                return;
            }

            this.IsVisible = true;
            this.CurrentRegionNameText = lookupResult.RegionName;
            this.regionLabelSmall.gameObject.SetActive(true);
            this.regionLabelSmall.text = this.CurrentRegionNameText;
        }

        private bool EnsureSmallLabel()
        {
            Minimap minimap = Minimap.instance;
            if (minimap == null || minimap.m_smallRoot == null || minimap.m_biomeNameSmall == null)
            {
                this.regionLabelSmall = null;
                this.boundMinimap = null;
                return false;
            }

            if (this.regionLabelSmall != null && this.boundMinimap == minimap)
            {
                return true;
            }

            TMP_Text source = minimap.m_biomeNameSmall;
            var labelObject = Object.Instantiate(source.gameObject, source.transform.parent);
            labelObject.name = "WZ_RegionNameSmall";

            TMP_Text label = labelObject.GetComponent<TMP_Text>();
            if (label == null)
            {
                Object.Destroy(labelObject);
                return false;
            }

            label.text = string.Empty;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = true;
            label.fontSizeMax = source.enableAutoSizing ? source.fontSizeMax : source.fontSize;
            label.fontSizeMin = Mathf.Max(10f, source.enableAutoSizing ? source.fontSizeMin : source.fontSize * 0.55f);
            label.alignment = TextAlignmentOptions.BottomLeft;
            label.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform sourceRect = source.rectTransform;
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0f, 0f);
            labelRect.anchoredPosition = new Vector2(8f, 6f);
            float height = Mathf.Max(22f, sourceRect.rect.height * 1.1f);
            labelRect.sizeDelta = new Vector2(-16f, height);

            label.gameObject.SetActive(false);

            this.boundMinimap = minimap;
            this.regionLabelSmall = label;
            return true;
        }
    }
}
