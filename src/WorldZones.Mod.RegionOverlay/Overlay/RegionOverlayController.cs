using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;
using WorldZones.Runtime.Geometry;
using WorldZones.Unity;

namespace WorldZones.Mod.RegionOverlay.Overlay
{
    /// <summary>
    /// Tier-3 live draw of the region overlay onto Valheim's LARGE world map. Consumes the Tier-2
    /// helpers (<see cref="MapUvProjector"/>, <see cref="RegionUiVertexFiller"/> via
    /// <see cref="RegionInkGraphic"/>, <see cref="RegionTextureBaker"/>) and the live <c>Minimap</c>
    /// (<c>m_pinRootLarge</c>, <c>m_mapImageLarge</c>, <c>m_pixelSize</c>, <c>m_textureSize</c>,
    /// <c>uvRect</c>). Borders-only is the resting default; fog-gated via the reflected
    /// <see cref="FogRevealGate"/>.
    ///
    /// <para>Plain class driven by the plugin's existing Update pump (like
    /// <c>MinimapLabelController</c>) — NOT a self-hosting MonoBehaviour, so it cannot fall into the
    /// "host SetActive(false) kills its own Update" trap (the pump lives on the always-active plugin
    /// GameObject). Visibility toggles a <c>_content</c> CHILD, never the mount root. Mount success is
    /// logged explicitly — "patch applied" is never trusted as proof the overlay drew.</para>
    ///
    /// <para>Scoped to the north-up LARGE map (rotation 0). The small rotating minimap is a separate
    /// scenario (the labels handle it independently). See docs/design/region-render-seam.md (step 3).</para>
    /// </summary>
    public sealed class RegionOverlayController
    {
        private readonly ManualLogSource? logger;
        private readonly FogRevealGate fog;

        // ── TWEAK-ME starting dials (reversible; Daniel tunes in-world / via future config) ──────────
        /// <summary>Border stroke width in UI pixels (constant on screen, zoom-independent).</summary>
        public float StrokeWidthPx { get; set; } = 2f;
        /// <summary>Border ink colour (near-black, slightly translucent so terrain reads under thin lines).</summary>
        public Color32 InkColor { get; set; } = new Color32(18, 18, 18, 235);
        /// <summary>Fill alpha for the translucent <see cref="RegionOverlayStyle.BordersTint"/> style.</summary>
        public byte TintAlpha { get; set; } = 90;
        /// <summary>Fill alpha for the opaque <see cref="RegionOverlayStyle.Parchment"/> style.</summary>
        public byte ParchmentAlpha { get; set; } = 235;
        /// <summary>Seconds between fog-aware fill re-bakes while a fill style is active (fog advances as you explore).</summary>
        public float FillRebakeIntervalSeconds { get; set; } = 3f;

        // ── Mount state ─────────────────────────────────────────────────────────────────────────────
        private Minimap? boundMinimap;
        private GameObject? root;       // stable host under m_pinRootLarge — never SetActive(false)
        private GameObject? content;    // toggled child holding the graphics
        private GameObject? discClip;   // circular uGUI Mask clipping fill+ink to the inscribed disc
        private RegionInkGraphic? ink;
        private RawImage? fill;
        private RawImage? halo;          // soft coast-fade layer (below ink, beside fill)
        private bool mountLogged;

        // ── TWEAK-ME coast-halo dials (reversible; Daniel tunes in-world) ────────────────────────────
        /// <summary>Coast-halo colour (RGB) + peak alpha (A). Warm gold by default; A scales the whole fade.</summary>
        public Color32 HaloColor { get; set; } = new Color32(235, 180, 95, 200);

        // ── TWEAK-ME Atlas dials (reversible; validated offline on Niflheim — region-atlas-render.md) ─
        /// <summary>Atlas biome-fill alpha — low so the hillshaded terrain reads THROUGH the tint (0.28 → ~71).</summary>
        public byte AtlasFillAlpha { get; set; } = 71;
        /// <summary>Atlas coast-glow peak alpha (the glow uses each region's biome colour; A scales it).</summary>
        public byte AtlasGlowAlpha { get; set; } = 242;
        /// <summary>Atlas ink colour — near-black, only stroked on interior land↔land seams.</summary>
        public Color32 AtlasInkColor { get; set; } = new Color32(15, 12, 10, 235);

        // ── Cached per-world geometry (built once per world, NOT per frame — AC-T3-DRAW-1) ───────────
        private RegionBoundaryGraph? graph;
        private IReadOnlyList<RefinedBorder>? arcs;   // v2: refined contour-hugging coast ∪ biome-seam arcs
        private int[,]? regionIdGrid;
        private int gridMinIndex;
        private IReadOnlyList<Color32>? palette;        // colourblind lightness ramp (BordersTint/Parchment)
        private IReadOnlyList<Color32>? biomePalette;   // Atlas: per-label biome wash colour
        private CoastHaloField? haloField;            // cached per-world; null until SetHaloField

        // ── Fill bake cache ─────────────────────────────────────────────────────────────────────────
        private readonly RegionTextureBaker baker = new RegionTextureBaker();
        private Texture2D? fillTexture;
        private float lastFillBakeTime = float.NegativeInfinity;
        private bool bakedFillWasBiome;   // which palette the cached fill was baked with (re-bake on switch)

        // ── Halo bake cache (re-baked only when the mode changes, NOT per frame) ─────────────────────
        private readonly CoastHaloBaker haloBaker = new CoastHaloBaker();
        private Texture2D? haloTexture;
        private CoastHaloMode bakedHaloMode = CoastHaloMode.Off;

        // ── v2 reusable refined-arc fog-gate buffers ─────────────────────────────────────────────────
        // The visible fragments handed to the ink each frame, plus a pool of fragment polylines (and a
        // parallel pool of RefinedBorder wrappers, each permanently wrapping its pool list) so the
        // per-frame fog walk allocates ~nothing on the hot path. SAME cross-frame-reuse contract the
        // old visibleSegments path already shipped: SetBorders stores these references and uGUI consumes
        // them in the SAME frame's canvas rebuild (before the next Render mutates them).
        private readonly List<RefinedBorder> visibleArcs = new List<RefinedBorder>(512);
        private readonly List<List<WzVec2>> fragmentPool = new List<List<WzVec2>>(512);
        private readonly List<RefinedBorder> borderWrapperPool = new List<RefinedBorder>(512);
        private int fragmentPoolUsed;

        public RegionOverlayController(ManualLogSource? logger)
        {
            this.logger = logger;
            this.fog = new FogRevealGate(logger);
        }

        /// <summary>True if the fog gate resolved; when false the overlay disables itself (fail-closed).</summary>
        public bool FogAvailable => this.fog.Available;

        /// <summary>
        /// Cache the per-world geometry. Call ONCE per world load (the boundary graph + the refined
        /// contour-hugging arcs the ink strokes + the grid the fill bakes from). The palette is sized
        /// to the max label so every region has a fill colour.
        /// </summary>
        public void SetWorld(RegionBoundaryGraph boundaryGraph, IReadOnlyList<RefinedBorder> refinedArcs,
                             int[,] regionIdGrid, int minIndex)
        {
            this.graph = boundaryGraph;
            this.arcs = refinedArcs;
            this.regionIdGrid = regionIdGrid;
            this.gridMinIndex = minIndex;

            int maxLabel = -1;
            int h = regionIdGrid.GetLength(0), w = regionIdGrid.GetLength(1);
            for (int gy = 0; gy < h; gy++)
                for (int gx = 0; gx < w; gx++)
                    if (regionIdGrid[gy, gx] > maxLabel) maxLabel = regionIdGrid[gy, gx];

            this.palette = RegionPalette.BuildLightnessRamp(maxLabel + 1);
            this.InvalidateFill();

            int arcCount = refinedArcs?.Count ?? 0;
            this.logger?.LogInfo(
                $"RegionOverlay: world geometry cached — {boundaryGraph.Segments.Count} seams, " +
                $"{arcCount} refined arcs, grid {w}x{h}, {maxLabel + 1} region labels.");
        }

        /// <summary>True once a world's geometry has been cached (and the fog gate is usable).</summary>
        public bool HasWorld => this.graph != null && this.regionIdGrid != null && this.fog.Available;

        /// <summary>
        /// Supply the Atlas biome-fill palette: one <see cref="Color32"/> per grid label
        /// (<c>RegionInfo.TransientId</c>), at full alpha (the controller scales it by
        /// <see cref="AtlasFillAlpha"/> at draw). Null leaves Atlas falling back to the lightness ramp.
        /// Invalidates any baked fill so the next Atlas render re-bakes with biome colours.
        /// </summary>
        public void SetBiomePalette(IReadOnlyList<Color32>? labelToBiomeColor)
        {
            this.biomePalette = labelToBiomeColor;
            this.InvalidateFill();
        }

        /// <summary>
        /// Cache the per-world coast-halo field (the signed-distance-to-shoreline field the soft fade
        /// bakes from). Built once per world load alongside <see cref="SetWorld"/>; null clears it (the
        /// halo then renders nothing). Invalidates any baked halo texture so the next render re-bakes.
        /// </summary>
        public void SetHaloField(CoastHaloField? field)
        {
            this.haloField = field;
            this.InvalidateHalo();
        }

        /// <summary>
        /// The per-update render. Mounts on first call (logs success), then draws the current
        /// <paramref name="style"/> (F8 ink/fill dial) AND the current <paramref name="haloMode"/> (F7
        /// coast-fade dial) onto the large map IF it is open; otherwise hides the content. Safe to call
        /// every plugin tick — the boundary graph is NOT rebuilt here (it is cached). The halo is an
        /// INDEPENDENT layer: it shows whenever <paramref name="haloMode"/> ≠ Off and a world is loaded,
        /// even when <paramref name="style"/> is Vanilla (the two dials are orthogonal).
        /// </summary>
        public void Render(RegionOverlayStyle style, CoastHaloMode haloMode)
        {
            Minimap? minimap = Minimap.instance;
            if (minimap == null || minimap.m_pinRootLarge == null || minimap.m_mapImageLarge == null)
            {
                HideContent();
                return;
            }

            // (Re)mount if needed (first time, or the minimap was rebuilt on world reload).
            if (this.boundMinimap != minimap || this.root == null)
            {
                if (!Mount(minimap)) { HideContent(); return; }
            }

            // Nothing draws unless the large map is open with world data + a working fog gate. Both dials
            // being at their resting "nothing" position (Vanilla + Off) also hides everything.
            bool largeMapOpen = Minimap.IsOpen();
            bool anythingToDraw = style != RegionOverlayStyle.Vanilla || haloMode != CoastHaloMode.Off;
            if (!largeMapOpen || !HasWorld || !anythingToDraw)
            {
                HideContent();
                return;
            }

            ShowContent();

            MapFrame fullFrame = ValheimMapConventions.FullMapFrame(minimap.m_pixelSize, minimap.m_textureSize);
            Rect vanillaUvRect = minimap.m_mapImageLarge.uvRect;

            // ── Coast halo (soft fade, F7 dial OR implied-on by Atlas), below the fill + ink ─────────
            CoastHaloMode effectiveHalo = style.ImpliesHalo() && haloMode == CoastHaloMode.Off
                ? CoastHaloMode.Seaward   // Atlas implies the seaward glow even when F7 is Off
                : haloMode;
            if (effectiveHalo != CoastHaloMode.Off && this.haloField != null)
            {
                EnsureHaloTexture(effectiveHalo);
                this.halo!.uvRect = this.haloBaker.WorldAlignedUvRect(DisplayedFrame(fullFrame, vanillaUvRect));
                this.halo.gameObject.SetActive(this.haloTexture != null);
            }
            else
            {
                this.halo!.gameObject.SetActive(false);
            }

            // ── Ink (refined contour-hugging arcs), fog-gated per SUB-SEGMENT ─────────────────────────
            if (style.DrawsInk())
            {
                BuildVisibleArcs(minimap, style.TerrestrialInkOnly());
                Color32 inkColor = style == RegionOverlayStyle.Atlas ? this.AtlasInkColor : this.InkColor;
                this.ink!.SetBorders(this.visibleArcs, fullFrame, vanillaUvRect, this.StrokeWidthPx, inkColor);
                this.ink.gameObject.SetActive(true);
            }
            else
            {
                this.ink!.gameObject.SetActive(false);
            }

            // ── Fill (baked texture), fog-gated per texel (pre-masked grid) ──────────────────────────
            if (style.DrawsFill())
            {
                bool biome = style.UsesBiomeFill();
                EnsureFillTexture(minimap, biome);
                byte alpha = style == RegionOverlayStyle.Atlas ? this.AtlasFillAlpha
                    : style == RegionOverlayStyle.Parchment ? this.ParchmentAlpha : this.TintAlpha;
                this.fill!.color = new Color32(255, 255, 255, alpha);
                this.fill.uvRect = this.baker.WorldAlignedUvRect(DisplayedFrame(fullFrame, vanillaUvRect));
                this.fill.gameObject.SetActive(this.fillTexture != null);
            }
            else
            {
                this.fill!.gameObject.SetActive(false);
            }
        }

        /// <summary>Back-compat overload: render the F8 style with the coast halo off.</summary>
        public void Render(RegionOverlayStyle style) => Render(style, CoastHaloMode.Off);

        /// <summary>
        /// Fog-gate the cached refined arcs at SUB-SEGMENT granularity (AC-V2-A-LINE-2). A refined arc
        /// is a long chained polyline whose vertices span explored AND unexplored fog; gating the WHOLE
        /// arc by either endpoint would reveal unexplored interior (spoiling the world). So we walk each
        /// arc per vertex-pair, applying the SAME per-pair <c>IsExplored</c> test the old per-seam path
        /// ran (a sub-segment shows if either of its local endpoints touches explored fog), and emit the
        /// contiguous visible runs as fragment polylines. Net ≤ the old per-seam cost (AC-V2-A-LINE-3):
        /// the refined arcs carry far fewer vertices than 12,541 raw seams × 2 endpoints.
        ///
        /// <para>Pooled: both the fragment point-lists and their <see cref="RefinedBorder"/> wrappers are
        /// reused frame-to-frame (a wrapper permanently wraps its pool list; clearing + refilling the
        /// list mutates what the wrapper exposes), so the steady-state hot path allocates nothing. The
        /// produced <see cref="visibleArcs"/> are consumed by uGUI within the SAME frame — identical
        /// cross-frame-reuse contract to the prior <see cref="visibleSegments"/> path.</para>
        /// </summary>
        private void BuildVisibleArcs(Minimap minimap, bool terrestrialOnly)
        {
            this.visibleArcs.Clear();
            this.fragmentPoolUsed = 0;
            if (this.arcs == null) return;

            for (int a = 0; a < this.arcs.Count; a++)
            {
                // Atlas: ink ONLY interior land↔land seams. A coastline arc (region-vs-void) carries
                // KeyB == null; the seaward glow draws those, so the ink skips them. Biome-seam arcs
                // (two real regions) keep both keys → kept. This is the terrestrial-vs-coastal axis.
                if (terrestrialOnly && this.arcs[a].KeyB == null) continue;

                IReadOnlyList<WzVec2> poly = this.arcs[a].Polyline;
                if (poly == null || poly.Count < 2) continue;

                List<WzVec2>? frag = null;
                bool prevExplored = this.fog.IsExplored(minimap, poly[0].X, poly[0].Z);
                for (int j = 1; j < poly.Count; j++)
                {
                    bool curExplored = this.fog.IsExplored(minimap, poly[j].X, poly[j].Z);
                    // Sub-segment (poly[j-1] → poly[j]) shows if EITHER local endpoint is explored —
                    // the per-pair gate identical to the old per-seam IsExplored(A) || IsExplored(B).
                    if (prevExplored || curExplored)
                    {
                        if (frag == null)
                        {
                            frag = RentFragment();
                            frag.Add(poly[j - 1]);
                        }
                        frag.Add(poly[j]);
                    }
                    else if (frag != null)
                    {
                        FlushFragment(frag);
                        frag = null;
                    }
                    prevExplored = curExplored;
                }
                if (frag != null) FlushFragment(frag);
            }
        }

        /// <summary>Rent a cleared point-list from the pool (grows the pool 1:1 with its wrapper).</summary>
        private List<WzVec2> RentFragment()
        {
            if (this.fragmentPoolUsed >= this.fragmentPool.Count)
            {
                var fresh = new List<WzVec2>(64);
                this.fragmentPool.Add(fresh);
                // A wrapper that PERMANENTLY wraps this pool list — clearing/refilling the list updates
                // what RefinedBorder.Polyline exposes, so the wrapper is reusable every frame. Keys are
                // irrelevant to FillPolylines (it reads Polyline only), so null/false is fine.
                this.borderWrapperPool.Add(new RefinedBorder(fresh, null, null, false));
            }
            List<WzVec2> list = this.fragmentPool[this.fragmentPoolUsed];
            list.Clear();
            return list;
        }

        /// <summary>Commit the current fragment (≥2 points) as a visible arc via its pooled wrapper.</summary>
        private void FlushFragment(List<WzVec2> frag)
        {
            if (frag.Count >= 2)
            {
                this.visibleArcs.Add(this.borderWrapperPool[this.fragmentPoolUsed]);
                this.fragmentPoolUsed++;
            }
            // A <2-point fragment can't occur (we always seed with 2), but if it did we'd leak a pool
            // slot for this frame; harmless (reclaimed next frame's reset). Left explicit for clarity.
        }

        /// <summary>(Re)bake the fog-masked fill texture on a throttle (fog advances as you explore).
        /// <paramref name="biome"/> selects the Atlas biome palette vs the colourblind lightness ramp;
        /// switching palette forces an immediate re-bake (not just on the time throttle).</summary>
        private void EnsureFillTexture(Minimap minimap, bool biome)
        {
            if (this.regionIdGrid == null) return;
            IReadOnlyList<Color32>? activePalette = biome ? (this.biomePalette ?? this.palette) : this.palette;
            if (activePalette == null) return;

            bool paletteSwitched = this.bakedFillWasBiome != biome;
            bool stale = this.fillTexture == null
                         || paletteSwitched
                         || (Time.unscaledTime - this.lastFillBakeTime) > this.FillRebakeIntervalSeconds;
            if (!stale) return;

            // Pre-mask: unexplored cells → -1 (transparent), so the pure Tier-2 baker fog-gates the fill
            // for free without learning about fog. AC-T3-FOG-1.
            int h = this.regionIdGrid.GetLength(0), w = this.regionIdGrid.GetLength(1);
            var masked = new int[h, w];
            const double cell = 64.0; // ZoneGrid.ZoneSize — matches RegionTextureBaker / RegionBoundaryExtractor
            for (int gy = 0; gy < h; gy++)
            {
                double wz = (gy + this.gridMinIndex) * cell;
                for (int gx = 0; gx < w; gx++)
                {
                    int label = this.regionIdGrid[gy, gx];
                    if (label < 0) { masked[gy, gx] = -1; continue; }
                    double wx = (gx + this.gridMinIndex) * cell;
                    masked[gy, gx] = this.fog.IsExplored(minimap, wx, wz) ? label : -1;
                }
            }

            if (this.fillTexture != null) Object.Destroy(this.fillTexture);
            this.fillTexture = this.baker.Bake(masked, this.gridMinIndex, activePalette, new Color32(0, 0, 0, 0));
            this.fill!.texture = this.fillTexture;
            this.lastFillBakeTime = Time.unscaledTime;
            this.bakedFillWasBiome = biome;
        }

        private void InvalidateFill()
        {
            this.lastFillBakeTime = float.NegativeInfinity;
            if (this.fillTexture != null) { Object.Destroy(this.fillTexture); this.fillTexture = null; }
        }

        /// <summary>
        /// (Re)bake the coast-halo texture when the mode changes. The halo field is static per world, so
        /// unlike the fill there is no time throttle — we only re-bake when <paramref name="mode"/>
        /// differs from the baked one. The bake is fog-agnostic here (the halo traces coastlines, and the
        /// field is pure); fog-gating the halo per-texel is a deferred v2 nicety tracked in the design doc.
        /// </summary>
        private void EnsureHaloTexture(CoastHaloMode mode)
        {
            if (this.haloField == null) return;
            if (this.haloTexture != null && this.bakedHaloMode == mode) return;

            if (this.haloTexture != null) Object.Destroy(this.haloTexture);
            this.haloTexture = this.haloBaker.Bake(this.haloField, mode, this.HaloColor);
            this.halo!.texture = this.haloTexture;
            this.bakedHaloMode = mode;
        }

        private void InvalidateHalo()
        {
            this.bakedHaloMode = CoastHaloMode.Off;
            if (this.haloTexture != null) { Object.Destroy(this.haloTexture); this.haloTexture = null; }
        }

        /// <summary>
        /// The MapFrame describing the world window vanilla currently DISPLAYS (after pan/zoom), from
        /// the full frame + the live <c>uvRect</c>. Used to align the fill texture's uvRect to vanilla's
        /// view so the fill registers under the ink + the terrain.
        /// </summary>
        private static MapFrame DisplayedFrame(MapFrame fullFrame, Rect uvRect)
        {
            double cx = (uvRect.center.x - 0.5) * fullFrame.SpanX + fullFrame.CenterX;
            double cz = (uvRect.center.y - 0.5) * fullFrame.SpanZ + fullFrame.CenterZ;
            double sx = uvRect.width * fullFrame.SpanX;
            double sz = uvRect.height * fullFrame.SpanZ;
            if (sx <= 0) sx = fullFrame.SpanX;
            if (sz <= 0) sz = fullFrame.SpanZ;
            return new MapFrame(cx, cz, sx, sz, 0.0);
        }

        // ── Mount / visibility ──────────────────────────────────────────────────────────────────────

        private bool Mount(Minimap minimap)
        {
            Unmount();

            this.root = new GameObject("WZ_RegionOverlay", typeof(RectTransform));
            var rootRt = (RectTransform)this.root.transform;
            rootRt.SetParent(minimap.m_pinRootLarge, worldPositionStays: false);
            StretchFull(rootRt);
            // Under the pins (drawn first = below) but over the map RawImage (a sibling layer beneath
            // the pin root) — so pins/labels stay on top of our fills.
            rootRt.SetAsFirstSibling();

            this.content = new GameObject("_content", typeof(RectTransform));
            var contentRt = (RectTransform)this.content.transform;
            contentRt.SetParent(rootRt, worldPositionStays: false);
            StretchFull(contentRt);

            // ── Disc clip (THE bleed fix) ─────────────────────────────────────────────────────────────
            // A single circular uGUI Mask between _content and the graphics clips BOTH the fill RawImage
            // and the ink graphic to the inscribed disc, so nothing spills into the black starfield
            // corners (AC-V2-A-CLIP-1). The mask root is stretch-full under m_pinRootLarge (co-extensive
            // with m_mapImageLarge.rect — the same registration the existing borders rely on), and
            // preserveAspect keeps the circle round + concentric with the vanilla disc (AC-V2-A-CLIP-2).
            // showMaskGraphic=false so the circle itself never paints — it only defines the stencil.
            var maskGo = new GameObject("WZ_DiscClip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            var maskRt = (RectTransform)maskGo.transform;
            maskRt.SetParent(contentRt, worldPositionStays: false);
            StretchFull(maskRt);
            var maskImage = maskGo.GetComponent<Image>();
            maskImage.sprite = GetCircleMaskSprite();
            maskImage.preserveAspect = true;       // round disc even if the map rect is non-square
            maskImage.raycastTarget = false;
            var mask = maskGo.GetComponent<Mask>();
            mask.showMaskGraphic = false;          // stencil only — the circle sprite never draws
            this.discClip = maskGo;

            // Halo (lowest), then fill, then ink — all inside the MASK (so all clipped to the disc).
            // Coast halo sits beneath the region fill + ink so the fade reads as a backdrop the borders
            // and tints draw over.
            var haloGo = new GameObject("WZ_CoastHalo", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var haloRt = (RectTransform)haloGo.transform;
            haloRt.SetParent(maskRt, worldPositionStays: false);
            StretchFull(haloRt);
            this.halo = haloGo.GetComponent<RawImage>();
            this.halo.raycastTarget = false;
            this.halo.color = new Color32(255, 255, 255, 255); // texture carries its own per-texel alpha
            haloGo.SetActive(false);

            // Fill (below) then ink (above) inside the MASK (so both are clipped to the disc).
            var fillGo = new GameObject("WZ_RegionFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(maskRt, worldPositionStays: false);
            StretchFull(fillRt);
            this.fill = fillGo.GetComponent<RawImage>();
            this.fill.raycastTarget = false;
            this.fill.color = new Color32(255, 255, 255, 0);
            fillGo.SetActive(false);

            var inkGo = new GameObject("WZ_RegionInk", typeof(RectTransform), typeof(CanvasRenderer));
            var inkRt = (RectTransform)inkGo.transform;
            inkRt.SetParent(maskRt, worldPositionStays: false);
            StretchFull(inkRt);
            this.ink = inkGo.AddComponent<RegionInkGraphic>();
            inkGo.SetActive(false);

            this.content.SetActive(false);
            this.boundMinimap = minimap;

            if (!this.mountLogged)
            {
                this.logger?.LogInfo(
                    "RegionOverlay: MOUNTED under Minimap.m_pinRootLarge (root 'WZ_RegionOverlay' + '_content' " +
                    "with WZ_RegionFill + WZ_RegionInk). Host stays active; visibility toggles _content. " +
                    $"FogGate.Available={this.fog.Available}.");
                this.mountLogged = true;
            }
            return true;
        }

        private void ShowContent() { if (this.content != null && !this.content.activeSelf) this.content.SetActive(true); }
        private void HideContent() { if (this.content != null && this.content.activeSelf) this.content.SetActive(false); }

        private static void StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        // ── Disc clip (THE bleed fix) ─────────────────────────────────────────────────────────────────
        /// <summary>
        /// The runtime-generated circular Mask sprite — painted ONCE (white inside an AA'd radius,
        /// transparent outside) and reused for every mount. Generated in code so there is NO asset
        /// bundle and NO builtin Unity sprite (sidesteps the <c>Knob.psd</c>-class builtin-sprite load
        /// failure on Valheim's 0.221.x Unity — AC-V2-A-CLIP-3), the same way <see cref="RegionInkGraphic"/>
        /// leans on <c>s_WhiteTexture</c> instead of a sprite. This is the JotunnLib <c>CircleMask</c>
        /// technique minus the bundle.
        /// </summary>
        private static Sprite? s_circleMaskSprite;

        /// <summary>Lazily build (and cache process-wide) the circular mask sprite.</summary>
        private static Sprite GetCircleMaskSprite()
        {
            if (s_circleMaskSprite != null) return s_circleMaskSprite;

            const int size = 512;                    // ample resolution for a clean disc edge on the large map
            const float r = size * 0.5f;             // inscribed radius = half the texture (fills the square)
            const float edge = 1.5f;                 // AA ramp width in texels

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                // Bilinear is fine here: this is the CLIP sprite, NOT the region fill (whose Point filter
                // guards the colourblind lightness palette). The clip never touches region colours.
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "WZ_CircleMaskTex",
            };

            var px = new Color32[size * size];
            float cx = r - 0.5f, cy = r - 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // alpha = 1 well inside the radius, ramps to 0 across the AA edge just inside r.
                    float a = Mathf.Clamp01((r - dist) / edge);
                    byte alpha = (byte)Mathf.RoundToInt(a * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            s_circleMaskSprite = Sprite.Create(
                tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit: 100f);
            s_circleMaskSprite.name = "WZ_CircleMask";
            return s_circleMaskSprite;
        }

        /// <summary>Tear down the mounted objects (world reload / shutdown).</summary>
        public void Unmount()
        {
            if (this.root != null) Object.Destroy(this.root);
            this.root = null;
            this.content = null;
            this.discClip = null;
            this.ink = null;
            this.fill = null;
            this.halo = null;
            this.boundMinimap = null;
            InvalidateFill();
            InvalidateHalo();
        }
    }
}
