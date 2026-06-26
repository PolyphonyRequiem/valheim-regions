using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldZones.Runtime.Geometry;
using WorldZones.Unity;

namespace WorldZones.Mod.RegionOverlay.Overlay
{
    /// <summary>
    /// The uGUI ink-graphic host: a <see cref="MaskableGraphic"/> whose <see cref="OnPopulateMesh"/>
    /// delegates to the Tier-2 <see cref="RegionUiVertexFiller"/> to stroke the (already
    /// fog-filtered) region seams. Lives on a GameObject parented under <c>m_pinRootLarge</c>.
    ///
    /// <para>It carries NO sprite — the default UI material samples the white texture so the per-vertex
    /// ink colour is what shows (avoids the <c>Knob.psd</c> builtin-sprite load failure on Valheim's
    /// 0.221.x Unity build entirely). <c>raycastTarget</c> is off so it never eats map clicks. The data
    /// is pushed by <see cref="SetSegments"/>; that marks the mesh dirty so uGUI repaints on its own
    /// schedule (we don't rebuild every frame).</para>
    /// </summary>
    public sealed class RegionInkGraphic : MaskableGraphic
    {
        private MapFrame? _frame;
        private IReadOnlyList<BorderSegment>? _segments;
        private IReadOnlyList<RefinedBorder>? _borders;
        private Rect _uvRect = new Rect(0f, 0f, 1f, 1f);
        private float _strokeWidthPx = 2f;
        private Color32 _ink = new Color32(20, 20, 20, 235);

        // No sprite/texture: use the default white so vertex colour is the visible stroke.
        public override Texture mainTexture => s_WhiteTexture;

        protected override void Awake()
        {
            base.Awake();
            this.raycastTarget = false;
        }

        /// <summary>
        /// Push a new (fog-filtered) seam set + the live projection inputs, and mark the mesh dirty.
        /// Pass <paramref name="segments"/> = null/empty to clear.
        /// </summary>
        public void SetSegments(IReadOnlyList<BorderSegment>? segments, MapFrame frame, Rect uvRect,
                                float strokeWidthPx, Color32 ink)
        {
            this._segments = segments;
            this._borders = null;
            this._frame = frame;
            this._uvRect = uvRect;
            this._strokeWidthPx = strokeWidthPx;
            this._ink = ink;
            this.SetVerticesDirty();
        }

        /// <summary>
        /// Push a new (fog-filtered) refined-arc set + the live projection inputs, and mark the mesh
        /// dirty — the v2 contour-hugging line path. The caller has already fog-gated the arcs at
        /// SUB-SEGMENT granularity (each <see cref="RefinedBorder"/> here is a visible fragment), so
        /// <see cref="OnPopulateMesh"/> strokes them verbatim via <c>FillPolylines</c>. Pass
        /// <paramref name="borders"/> = null/empty to clear. Mutually exclusive with
        /// <see cref="SetSegments"/> — setting one clears the other.
        /// </summary>
        public void SetBorders(IReadOnlyList<RefinedBorder>? borders, MapFrame frame, Rect uvRect,
                               float strokeWidthPx, Color32 ink)
        {
            this._borders = borders;
            this._segments = null;
            this._frame = frame;
            this._uvRect = uvRect;
            this._strokeWidthPx = strokeWidthPx;
            this._ink = ink;
            this.SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (this._frame == null) return;

            var filler = new RegionUiVertexFiller(this._frame, GetPixelAdjustedRect(), this._strokeWidthPx, this._ink);

            // Prefer the refined-arc path (v2). Fall back to raw seams if only those were pushed
            // (e.g. a graph with no refined arcs — defensive; the live path always sets borders).
            if (this._borders != null && this._borders.Count > 0)
                filler.FillPolylines(vh, this._borders, this._uvRect);
            else if (this._segments != null && this._segments.Count > 0)
                filler.FillSegments(vh, this._segments, this._uvRect);
        }
    }
}
