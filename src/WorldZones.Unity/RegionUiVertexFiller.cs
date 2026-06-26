using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WorldZones.Runtime.Geometry;

namespace WorldZones.Unity
{
    /// <summary>
    /// The border-INK primitive (mesh: crisp, restyles live, vector-sharp at any zoom). Consumes the
    /// deduplicated Tier-1 <see cref="BorderSegment"/> seam set (or the contour-hugging
    /// <see cref="RefinedBorder"/> polylines), projects each world endpoint to map UV with
    /// <see cref="MapUvProjector"/>, and emits quad strokes into a uGUI <see cref="VertexHelper"/> for
    /// a <see cref="MaskableGraphic"/> mounted under <c>m_pinRootLarge</c> (the pin layer above the
    /// vanilla map RawImage).
    ///
    /// <para>UnityEngine-ONLY — no <c>Minimap</c> / <c>assembly_valheim</c> symbol here (AC-T2-INK-4).
    /// The two seams a consumer wires in are the projection FRAME (the whole-map world window, e.g.
    /// <see cref="ValheimMapConventions.FullMapFrame"/>) and the live displayed
    /// <paramref name="uvRect"/> (vanilla's <c>m_mapImageLarge.uvRect</c> — how vanilla pans/zooms the
    /// map texture). A world point's screen position is where its full-map UV falls inside that
    /// displayed window, mapped onto <see cref="uiRect"/>. Stroke width is in UI pixels, so it is
    /// constant on screen regardless of map zoom (AC-T2-INK-3). See docs/design/region-render-seam.md.</para>
    /// </summary>
    public sealed class RegionUiVertexFiller
    {
        private readonly MapFrame frame;
        private readonly Rect uiRect;
        private readonly float halfStrokePx;
        private readonly Color32 ink;

        /// <param name="frame">The whole-map projection frame (the world window the map texture spans;
        ///   pass <see cref="ValheimMapConventions.FullMapFrame"/> for the vanilla M map).</param>
        /// <param name="uiRect">The target graphic's local rect (its <c>rectTransform.rect</c>) — quad
        ///   positions are emitted in this local space.</param>
        /// <param name="strokeWidthPx">Border stroke width in UI pixels (constant on screen).</param>
        /// <param name="ink">Stroke colour.</param>
        public RegionUiVertexFiller(MapFrame frame, Rect uiRect, float strokeWidthPx, Color32 ink)
        {
            this.frame = frame;
            this.uiRect = uiRect;
            this.halfStrokePx = Mathf.Max(0.01f, strokeWidthPx) * 0.5f;
            this.ink = ink;
        }

        /// <summary>
        /// Stroke the deduplicated seams (the borders-only style). Each <see cref="BorderSegment"/>
        /// emits exactly one quad (two triangles); N segments → N quads, no double-stroke (the seam set
        /// is already deduplicated in Tier-1 — AC-T2-INK-1). A segment fully outside the displayed
        /// <paramref name="uvRect"/> window is skipped, not drawn off the rect (AC-T2-INK-2).
        /// </summary>
        /// <param name="vh">The vertex helper to fill (cleared/managed by the caller).</param>
        /// <param name="segments">The Tier-1 deduplicated seam set.</param>
        /// <param name="uvRect">The displayed sub-rect of the map texture (vanilla
        ///   <c>m_mapImageLarge.uvRect</c>) — i.e. the currently-visible window after pan/zoom.</param>
        public void FillSegments(VertexHelper vh, IReadOnlyList<BorderSegment> segments, Rect uvRect)
        {
            if (vh == null || segments == null) return;
            for (int i = 0; i < segments.Count; i++)
            {
                BorderSegment seg = segments[i];
                Vector2 a = ToUi(seg.A.X, seg.A.Z, uvRect, out int outA);
                Vector2 b = ToUi(seg.B.X, seg.B.Z, uvRect, out int outB);
                // Cohen–Sutherland trivial reject: both endpoints share an outside half-plane → off-screen.
                if ((outA & outB) != 0) continue;
                EmitQuad(vh, a, b);
            }
        }

        /// <summary>
        /// Stroke refined contour-hugging arcs (same ink, richer line — hugs the real coast/biome edge
        /// instead of the 64 m staircase). Each <see cref="RefinedBorder.Polyline"/> emits one quad per
        /// sub-segment; off-screen sub-segments are trivially rejected.
        /// </summary>
        public void FillPolylines(VertexHelper vh, IReadOnlyList<RefinedBorder> borders, Rect uvRect)
        {
            if (vh == null || borders == null) return;
            for (int i = 0; i < borders.Count; i++)
            {
                IReadOnlyList<WzVec2> poly = borders[i].Polyline;
                if (poly == null || poly.Count < 2) continue;

                Vector2 prev = ToUi(poly[0].X, poly[0].Z, uvRect, out int prevOut);
                for (int j = 1; j < poly.Count; j++)
                {
                    Vector2 cur = ToUi(poly[j].X, poly[j].Z, uvRect, out int curOut);
                    if ((prevOut & curOut) == 0) EmitQuad(vh, prev, cur);
                    prev = cur;
                    prevOut = curOut;
                }
            }
        }

        /// <summary>
        /// World (X,Z) → local UI point inside <see cref="uiRect"/>, plus a Cohen–Sutherland outcode
        /// for the point relative to the displayed [0,1] window. The point is placed by where its
        /// full-map UV falls inside <paramref name="uvRect"/> (the displayed sub-window), so it tracks
        /// vanilla's pan/zoom exactly.
        /// </summary>
        private Vector2 ToUi(double worldX, double worldZ, Rect uvRect, out int outcode)
        {
            // Full-map UV (over the whole texture the frame spans).
            Vector2 fullUv = MapUvProjector.Project(new Vector2((float)worldX, (float)worldZ), this.frame);

            // Where does that fall within the currently-displayed window (uvRect)? → normalised screen UV.
            float w = Mathf.Approximately(uvRect.width, 0f) ? 1f : uvRect.width;
            float h = Mathf.Approximately(uvRect.height, 0f) ? 1f : uvRect.height;
            float sx = (fullUv.x - uvRect.xMin) / w;
            float sy = (fullUv.y - uvRect.yMin) / h;

            outcode = 0;
            if (sx < 0f) outcode |= 1; else if (sx > 1f) outcode |= 2;
            if (sy < 0f) outcode |= 4; else if (sy > 1f) outcode |= 8;

            // Map the screen UV onto the graphic's local rect.
            return new Vector2(
                this.uiRect.xMin + sx * this.uiRect.width,
                this.uiRect.yMin + sy * this.uiRect.height);
        }

        /// <summary>Emit a screen-constant-width stroked quad (two triangles) along p0→p1.</summary>
        private void EmitQuad(VertexHelper vh, Vector2 p0, Vector2 p1)
        {
            Vector2 dir = p1 - p0;
            float len = dir.magnitude;
            // Degenerate (both endpoints coincide after projection): draw a tiny square so a zero-length
            // seam still shows a dot rather than vanishing.
            Vector2 n = len < 1e-4f ? new Vector2(0f, 1f) : new Vector2(-dir.y / len, dir.x / len);
            Vector2 off = n * this.halfStrokePx;

            int baseIdx = vh.currentVertCount;
            AddVert(vh, p0 + off);
            AddVert(vh, p0 - off);
            AddVert(vh, p1 - off);
            AddVert(vh, p1 + off);
            vh.AddTriangle(baseIdx + 0, baseIdx + 1, baseIdx + 2);
            vh.AddTriangle(baseIdx + 2, baseIdx + 3, baseIdx + 0);
        }

        private void AddVert(VertexHelper vh, Vector2 p)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = new Vector3(p.x, p.y, 0f);
            v.color = this.ink;
            // uv0 zero: the consumer's graphic uses a solid/white material (the flat-colour ink case),
            // so the sampled texel is constant and the vertex colour is the visible stroke colour.
            v.uv0 = Vector2.zero;
            vh.AddVert(v);
        }
    }
}
