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
        private RegionInkGraphic? ink;
        private RegionInkGraphic? inkSeam;   // second ink layer: interior seams in SeamInkColor (F7 two-colour)
        private RawImage? fill;
        private RawImage? halo;          // soft coast-fade layer (below ink, beside fill)
        private bool mountLogged;

        // ── TWEAK-ME coast-halo dials (reversible; Daniel tunes in-world) ────────────────────────────
        /// <summary>Coast-halo colour (RGB) + peak alpha (A). Warm gold by default; A scales the whole fade.</summary>
        public Color32 HaloColor { get; set; } = new Color32(235, 180, 95, 200);

        /// <summary>
        /// Global glow-intensity multiplier (F6 dial), applied to BOTH the F7 gold halo
        /// (<see cref="HaloColor"/>.a) and the Atlas per-region glow (<see cref="AtlasGlowAlpha"/>) at bake
        /// time. 1.0 = ship default ("Full"); lower stops walk the glow down toward faint. Lives here (not
        /// baked into the colours) so F6 composes with whatever F7/F8 are showing and is a pure reversible
        /// dial. Changing it invalidates the halo bake cache (alpha is baked into the texture).
        /// </summary>
        public float GlowIntensity
        {
            get => this.glowIntensity;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Approximately(v, this.glowIntensity)) return;
                this.glowIntensity = v;
                this.InvalidateHalo();   // alpha is baked in → force a re-bake at the new intensity
            }
        }
        private float glowIntensity = 1f;

        /// <summary>The F7 gold halo colour with its peak alpha scaled by the F6 intensity dial.</summary>
        private Color32 ScaledHaloColor()
        {
            byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(this.HaloColor.a * this.glowIntensity), 0, 255);
            return new Color32(this.HaloColor.r, this.HaloColor.g, this.HaloColor.b, a);
        }

        /// <summary>The Atlas per-region glow peak alpha scaled by the F6 intensity dial.</summary>
        private byte ScaledAtlasGlowAlpha()
            => (byte)Mathf.Clamp(Mathf.RoundToInt(this.AtlasGlowAlpha * this.glowIntensity), 0, 255);

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
        private IReadOnlyList<Color32>? biomePalette;   // Atlas: per-label biome wash colour (fill)
        private IReadOnlyList<Color32>? glowPalette;    // Atlas: per-label biome GLOW colour (sat-floored)
        private CoastHaloField? haloField;            // cached per-world; null until SetHaloField

        // ── Fill bake cache ─────────────────────────────────────────────────────────────────────────
        private readonly RegionTextureBaker baker = new RegionTextureBaker();
        private Texture2D? fillTexture;
        private float lastFillBakeTime = float.NegativeInfinity;
        private bool bakedFillWasBiome;   // which palette the cached fill was baked with (re-bake on switch)

        // ── Fine fill mask (phase 2, 2026-06-29): a sub-zone region-id raster whose land/water edge follows
        // the 30 m waterline (RegionFillMaskBaker), so the terrestrial fill stops AT the coast instead of
        // the 64 m zone staircase overhanging into water. Null = fall back to the coarse 64 m baker.
        private int[,]? fineFillMask;      // [fy, fx] region label, −1 = water/unassigned; already height-clipped
        private double fineTexelMeters;    // world m per fine texel (e.g. 16)
        private int fineSubdivisions;      // fine texels per zone edge (64 / texel) — for the per-zone fog gate

        // ── Halo bake cache (re-baked only when the mode changes, NOT per frame) ─────────────────────
        private readonly CoastHaloBaker haloBaker = new CoastHaloBaker();
        private Texture2D? haloTexture;
        private CoastHaloMode bakedHaloMode = CoastHaloMode.Off;
        private bool bakedHaloWasBiome;   // whether the cached halo was baked per-region (Atlas) vs single-colour
        private float bakedHaloIntensity = 1f;  // F6 intensity the cached halo was baked at (re-bake on change)

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

        // Second parallel set for the SEAM ink layer (F7 two-colour boundary draw). The coast arcs go through
        // the buffers above (fed to `ink`), the interior seams through these (fed to `inkSeam`), so the two
        // types stroke in distinct colours in the same frame. Same no-alloc cross-frame-reuse contract.
        private readonly List<RefinedBorder> visibleArcsSeam = new List<RefinedBorder>(512);
        private readonly List<List<WzVec2>> fragmentPoolSeam = new List<List<WzVec2>>(512);
        private readonly List<RefinedBorder> borderWrapperPoolSeam = new List<RefinedBorder>(512);
        private int fragmentPoolUsedSeam;

        // ── F7 boundary-draw dial (which boundaries + per-type colour) — set by the plugin before Render ──
        /// <summary>Which boundaries the ink strokes (F7). Default All = coast + seam both shown.</summary>
        public BoundaryDrawMode BoundaryMode { get; set; } = BoundaryDrawMode.All;
        /// <summary>Coast (region↔water) ink colour. Blue — colourblind-safe vs the seam amber. Tweak-me.</summary>
        public Color32 CoastInkColor { get; set; } = new Color32(40, 120, 255, 235);
        /// <summary>Interior seam (region↔region) ink colour. Amber — colourblind-safe vs the coast blue.</summary>
        public Color32 SeamInkColor { get; set; } = new Color32(255, 150, 40, 235);

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
        /// Supply the FINE fill mask (phase 2): a sub-zone region-id raster (from
        /// <c>WorldZones.Runtime.RegionFillMaskBaker</c>) whose land/water edge already follows the 30 m
        /// waterline, so the terrestrial fill stops at the real coast instead of the 64 m zone staircase.
        /// <paramref name="texelMeters"/> is the fine cell size (e.g. 16); it must divide 64 evenly.
        /// Pass null to revert to the coarse 64 m baker. Invalidates the cached fill.
        /// </summary>
        public void SetFineFillMask(int[,]? fineMask, double texelMeters)
        {
            this.fineFillMask = fineMask;
            this.fineTexelMeters = texelMeters;
            this.fineSubdivisions = texelMeters > 0 ? (int)System.Math.Round(64.0 / texelMeters) : 0;
            this.InvalidateFill();
            if (fineMask != null)
                this.logger?.LogInfo($"RegionOverlay: fine fill mask set — {fineMask.GetLength(1)}x{fineMask.GetLength(0)} "
                                   + $"@ {texelMeters} m ({this.fineSubdivisions}× per zone). Fill clips to the 30 m waterline.");
        }

        /// <summary>
        /// Supply the Atlas biome palettes: <paramref name="fillColors"/> (per grid label, the region
        /// wash for the fill) and <paramref name="glowColors"/> (per grid label, the sat-floored coast
        /// glow colour). Both indexed by <c>RegionInfo.TransientId</c>, full alpha; the controller scales
        /// alpha at draw. Null leaves Atlas falling back to the lightness ramp / single halo colour.
        /// Invalidates the baked fill + halo so the next Atlas render re-bakes with biome colours.
        /// </summary>
        public void SetBiomePalette(IReadOnlyList<Color32>? fillColors, IReadOnlyList<Color32>? glowColors)
        {
            this.biomePalette = fillColors;
            this.glowPalette = glowColors;
            this.InvalidateFill();
            this.InvalidateHalo();
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
                EnsureHaloTexture(effectiveHalo, style.UsesBiomeFill());
                this.halo!.uvRect = this.haloBaker.WorldAlignedUvRect(DisplayedFrame(fullFrame, vanillaUvRect));
                this.halo.gameObject.SetActive(this.haloTexture != null);
            }
            else
            {
                this.halo!.gameObject.SetActive(false);
            }

            // ── Ink (refined contour-hugging arcs), fog-gated per SUB-SEGMENT, TWO layers by type ────────
            // F7 BoundaryMode is the authority over WHICH boundary types draw, and each type gets its own
            // colour: coast (region↔water) on `ink` in CoastInkColor (blue), interior seams (region↔region)
            // on `inkSeam` in SeamInkColor (amber). This OVERRIDES the Atlas terrestrial-only default — if
            // the user asks for coasts via F7, coasts show even under Atlas (which normally leaves them to
            // the glow). When style draws no ink at all (Vanilla), both layers hide regardless.
            bool styleDrawsInk = style.DrawsInk();
            bool drawCoast = styleDrawsInk && this.BoundaryMode.DrawsCoast();
            bool drawSeam = styleDrawsInk && this.BoundaryMode.DrawsSeam();

            if (drawCoast)
            {
                BuildVisibleArcs(minimap, wantCoast: true, seamLayer: false);
                this.ink!.SetBorders(this.visibleArcs, fullFrame, vanillaUvRect, this.StrokeWidthPx, this.CoastInkColor);
                this.ink.gameObject.SetActive(true);
            }
            else
            {
                this.ink!.gameObject.SetActive(false);
            }

            if (drawSeam)
            {
                BuildVisibleArcs(minimap, wantCoast: false, seamLayer: true);
                this.inkSeam!.SetBorders(this.visibleArcsSeam, fullFrame, vanillaUvRect, this.StrokeWidthPx, this.SeamInkColor);
                this.inkSeam.gameObject.SetActive(true);
            }
            else
            {
                this.inkSeam!.gameObject.SetActive(false);
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
        /// <summary>
        /// Fog-gate the cached refined arcs of ONE type into ONE target ink buffer.
        /// <paramref name="wantCoast"/> selects coast arcs (KeyB == null) when true, interior seams
        /// (KeyB != null) when false; <paramref name="seamLayer"/> selects which pooled buffer set the
        /// visible fragments go into (false = the coast `visibleArcs`/`ink`, true = `visibleArcsSeam`/`inkSeam`).
        /// The sub-segment fog gate (per vertex-pair <c>IsExplored</c>) is unchanged; only the arc-type
        /// filter and the destination buffers differ, so the two ink layers can draw distinct types/colours
        /// in the same frame with the same no-alloc contract.
        /// </summary>
        private void BuildVisibleArcs(Minimap minimap, bool wantCoast, bool seamLayer)
        {
            List<RefinedBorder> outList = seamLayer ? this.visibleArcsSeam : this.visibleArcs;
            outList.Clear();
            if (seamLayer) this.fragmentPoolUsedSeam = 0; else this.fragmentPoolUsed = 0;
            if (this.arcs == null) return;

            for (int a = 0; a < this.arcs.Count; a++)
            {
                // Filter to the requested boundary TYPE: coast arcs carry KeyB == null (region-vs-void),
                // interior seams carry both keys (region-vs-region).
                bool isCoast = this.arcs[a].KeyB == null;
                if (isCoast != wantCoast) continue;

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
                            frag = RentFragment(seamLayer);
                            frag.Add(poly[j - 1]);
                        }
                        frag.Add(poly[j]);
                    }
                    else if (frag != null)
                    {
                        FlushFragment(frag, seamLayer);
                        frag = null;
                    }
                    prevExplored = curExplored;
                }
                if (frag != null) FlushFragment(frag, seamLayer);
            }
        }

        /// <summary>Rent a cleared point-list from the pool for the chosen layer (grows the pool 1:1 with its wrapper).</summary>
        private List<WzVec2> RentFragment(bool seamLayer)
        {
            List<List<WzVec2>> pool = seamLayer ? this.fragmentPoolSeam : this.fragmentPool;
            List<RefinedBorder> wrappers = seamLayer ? this.borderWrapperPoolSeam : this.borderWrapperPool;
            int used = seamLayer ? this.fragmentPoolUsedSeam : this.fragmentPoolUsed;
            if (used >= pool.Count)
            {
                var fresh = new List<WzVec2>(64);
                pool.Add(fresh);
                // A wrapper that PERMANENTLY wraps this pool list — clearing/refilling the list updates
                // what RefinedBorder.Polyline exposes, so the wrapper is reusable every frame. Keys are
                // irrelevant to FillPolylines (it reads Polyline only), so null/false is fine.
                wrappers.Add(new RefinedBorder(fresh, null, null, false));
            }
            List<WzVec2> list = pool[used];
            list.Clear();
            return list;
        }

        /// <summary>Commit the current fragment (≥2 points) as a visible arc via its pooled wrapper (chosen layer).</summary>
        private void FlushFragment(List<WzVec2> frag, bool seamLayer)
        {
            if (frag.Count >= 2)
            {
                if (seamLayer)
                {
                    this.visibleArcsSeam.Add(this.borderWrapperPoolSeam[this.fragmentPoolUsedSeam]);
                    this.fragmentPoolUsedSeam++;
                }
                else
                {
                    this.visibleArcs.Add(this.borderWrapperPool[this.fragmentPoolUsed]);
                    this.fragmentPoolUsed++;
                }
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

            if (this.fillTexture != null) Object.Destroy(this.fillTexture);

            // ── FINE path (phase 2): the fill mask already encodes the 30 m-waterline land/water edge per
            // fine texel. Fog reveals at ZONE granularity (you explore in 64 m chunks), so we fog-gate per
            // ZONE — same ~grid-cell cost as the coarse path — and copy the pre-clipped fine label through
            // for explored zones, −1 for unexplored. The expensive height test was done ONCE at world-load
            // in RegionFillMaskBaker, NOT here, so the per-frame cost stays at the coarse grid's scale. ──
            if (this.fineFillMask != null && this.fineSubdivisions > 0)
            {
                int fh = this.fineFillMask.GetLength(0), fw = this.fineFillMask.GetLength(1);
                int sub = this.fineSubdivisions;
                const double zone = 64.0;
                var fineMasked = new int[fh, fw];
                // Fog state is per zone; cache one IsExplored per zone row/col span instead of per texel.
                int zonesH = fh / sub, zonesW = fw / sub;
                for (int zy = 0; zy < zonesH; zy++)
                {
                    double wz = (zy + this.gridMinIndex) * zone;
                    for (int zx = 0; zx < zonesW; zx++)
                    {
                        double wx = (zx + this.gridMinIndex) * zone;
                        bool explored = this.fog.IsExplored(minimap, wx, wz);
                        int fy0 = zy * sub, fx0 = zx * sub;
                        for (int dy = 0; dy < sub; dy++)
                            for (int dx = 0; dx < sub; dx++)
                            {
                                int fy = fy0 + dy, fx = fx0 + dx;
                                fineMasked[fy, fx] = explored ? this.fineFillMask[fy, fx] : -1;
                            }
                    }
                }
                this.fillTexture = this.baker.BakeFine(fineMasked, this.gridMinIndex, this.fineTexelMeters,
                                                       activePalette, new Color32(0, 0, 0, 0));
                this.fill!.texture = this.fillTexture;
                this.lastFillBakeTime = Time.unscaledTime;
                this.bakedFillWasBiome = biome;
                return;
            }

            // ── COARSE path (fallback, byte-identical to the shipped 64 m fill) ───────────────────────
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
        /// (Re)bake the coast-halo texture when the mode (or biome-glow choice) changes. The halo field
        /// is static per world, so unlike the fill there is no time throttle — we only re-bake when
        /// <paramref name="mode"/> or <paramref name="biomeGlow"/> differs from what was baked.
        /// <paramref name="biomeGlow"/> = true (Atlas) colours each coast by its region's biome
        /// (sat-floored glow palette); false uses the single <see cref="HaloColor"/> (F7 gold halo).
        /// </summary>
        private void EnsureHaloTexture(CoastHaloMode mode, bool biomeGlow)
        {
            if (this.haloField == null) return;
            // Cache key includes the F6 intensity: alpha is baked into the texture, so a different
            // intensity must re-bake even when mode + palette are unchanged.
            if (this.haloTexture != null && this.bakedHaloMode == mode && this.bakedHaloWasBiome == biomeGlow
                && Mathf.Approximately(this.bakedHaloIntensity, this.glowIntensity)) return;

            if (this.haloTexture != null) Object.Destroy(this.haloTexture);
            bool didBiome = biomeGlow && this.glowPalette != null;
            // DIAG (2026-06-28 glow-flat hunt): log the actual bake branch ONCE per bake so the live
            // build self-reports instead of us theorizing. If 'didBiome=False' on Atlas, glowPalette was
            // null at bake time (delivery/timing); if True but still flat in-game, the bug is in BakeBiome.
            this.logger?.LogInfo($"WZ-GLOW bake: requestedBiome={biomeGlow} glowPalette={(this.glowPalette == null ? "NULL" : this.glowPalette.Count.ToString())} mode={mode} → {(didBiome ? "PER-REGION BakeBiome" : "FLAT single-colour Bake")}");
            if (didBiome)
            {
                // Atlas: per-region biome glow. Fallback = the HaloColor's RGB for any out-of-band /
                // unowned texel (shouldn't paint — alpha there is 0 — but keeps the call total).
                // Pass the fine fill mask so the fade is PARTITIONED against the fill: no fade texel where
                // the fill already paints land (kills the coastal land-lip double-layer). The mask shares
                // the halo field's 16 m lattice; BakeBiome no-ops the partition if dims don't match.
                this.haloTexture = this.haloBaker.BakeBiome(
                    this.haloField, mode, this.glowPalette, this.HaloColor, this.ScaledAtlasGlowAlpha(),
                    this.fineFillMask);
            }
            else
            {
                this.haloTexture = this.haloBaker.Bake(this.haloField, mode, this.ScaledHaloColor());
            }
            this.halo!.texture = this.haloTexture;
            this.bakedHaloMode = mode;
            // Cache the ACTUAL bake kind, not the request: if glowPalette was null we baked FLAT, so record
            // FLAT — otherwise the cache key would claim biome and never re-bake once glowPalette arrives
            // (without an explicit InvalidateHalo). Latent flat-glow trap, fixed 2026-06-28.
            this.bakedHaloWasBiome = didBiome;
            this.bakedHaloIntensity = this.glowIntensity;
        }

        private void InvalidateHalo()
        {
            this.bakedHaloMode = CoastHaloMode.Off;
            this.bakedHaloWasBiome = false;
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

            // ── No clip mask ────────────────────────────────────────────────────────────────────────────
            // The bleed is killed AT THE SOURCE: every Clamp-sampled layer (fill + halo) bakes a
            // transparent border, so when the displayed uvRect runs past [0,1] (the ±8192 m vanilla map
            // texture is smaller than the ~±10500 m world, so zoom-out/pan does this routinely) Clamp
            // repeats an alpha-0 edge texel and there is NOTHING to smear. A circular uGUI Mask was tried
            // here and REMOVED: it only clipped the smear to a disc (bars still ran out to the disc edge),
            // it forced a round clip on a rectangular map, and it relied on a runtime sprite. Source-side
            // transparent borders need no stencil and leave the full rectangular map visible.
            // Halo (lowest), then fill, then ink — stacked directly under _content.
            var haloGo = new GameObject("WZ_CoastHalo", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var haloRt = (RectTransform)haloGo.transform;
            haloRt.SetParent(contentRt, worldPositionStays: false);
            StretchFull(haloRt);
            this.halo = haloGo.GetComponent<RawImage>();
            this.halo.raycastTarget = false;
            this.halo.color = new Color32(255, 255, 255, 255); // texture carries its own per-texel alpha
            haloGo.SetActive(false);

            // Fill (below) then ink (above), stacked under _content.
            var fillGo = new GameObject("WZ_RegionFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var fillRt = (RectTransform)fillGo.transform;
            fillRt.SetParent(contentRt, worldPositionStays: false);
            StretchFull(fillRt);
            this.fill = fillGo.GetComponent<RawImage>();
            this.fill.raycastTarget = false;
            this.fill.color = new Color32(255, 255, 255, 0);
            fillGo.SetActive(false);

            var inkGo = new GameObject("WZ_RegionInk", typeof(RectTransform), typeof(CanvasRenderer));
            var inkRt = (RectTransform)inkGo.transform;
            inkRt.SetParent(contentRt, worldPositionStays: false);
            StretchFull(inkRt);
            this.ink = inkGo.AddComponent<RegionInkGraphic>();
            inkGo.SetActive(false);

            // Second ink layer for interior SEAMS (F7 two-colour draw): coast arcs draw on `ink`, seams on
            // `inkSeam`, so each type strokes in its own colour. Stacked above the coast ink.
            var inkSeamGo = new GameObject("WZ_RegionInkSeam", typeof(RectTransform), typeof(CanvasRenderer));
            var inkSeamRt = (RectTransform)inkSeamGo.transform;
            inkSeamRt.SetParent(contentRt, worldPositionStays: false);
            StretchFull(inkSeamRt);
            this.inkSeam = inkSeamGo.AddComponent<RegionInkGraphic>();
            inkSeamGo.SetActive(false);

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

        /// <summary>Tear down the mounted objects (world reload / shutdown).</summary>
        public void Unmount()
        {
            if (this.root != null) Object.Destroy(this.root);
            this.root = null;
            this.content = null;
            this.ink = null;
            this.inkSeam = null;
            this.fill = null;
            this.halo = null;
            this.boundMinimap = null;
            InvalidateFill();
            InvalidateHalo();
        }
    }
}
