# The region render seam — geometry + Unity helpers for consumer mods

> **Status: Tier 1 DONE + committed. Tier 2 surface + the Tier-3 borders draw LOCKED 2026-06-25
> (build-order steps 2–3 — see `## Steps 2–3 lock` below). The Trailborne adapter (step 4) stays
> deferred. Daniel gates the merge.**
> This captures the architecture for how a *third-party mod* consumes WorldZones regions for
> rendering. Framing (Daniel, 2026-06-24): **Trailborne is our first "third-party modding
> customer."** The seam is designed for a general consumer that has never seen our internals —
> Trailborne is just the first one through it. The dependency arrow is **one-way:
> `consumer → WorldZones`**; WorldZones never names Trailborne (mirrors SBPR's own
> `Sunstone → Cartography.ThreatMarkers` seam).

## The problem this solves

`WorldZones.Runtime` today answers *identity* questions — `RegionAt(x,z)`, `GetByKey`,
`RegionInfo` (centroid, bounds, biome). A consumer can ask "what region am I in?" but **cannot
ask "give me the border shape so I can stroke it on a map."** Renderable region geometry — and
the world→map projection to place it — does not exist as a published surface yet. That export is
the customer-zero deliverable, and it is what *both* render hosts need (the standalone vanilla-map
draw **and** Trailborne's `MapSurface`).

## The constitution tension (and its resolution)

The constitution (`.specify/memory/constitution.md`) mandates **library-first**: core logic is
pure C#, testable with no Unity / BepInEx / Valheim assemblies. "Unity helpers" obviously need
`UnityEngine`. We do **not** bend the constitution. We **quarantine Unity into one thin assembly**
and keep every algorithm in the pure layer. Three tiers:

### Tier 1 — raw geometry (`WorldZones.Runtime`, pure, `net472;net8.0`, headless-tested)
The raw-geometry contract. **No Unity types** — plain structs only (reuse the hand-rolled vector
math; add a `WzVec2`-style world-point if needed).
- **Region boundaries** as world-space polylines / closed rings, keyed by the stable `RegionKey`
  (NOT the int BFS id), extracted from `regionIdGrid` + the classified grid.
- **The world→map-UV projection** as a *parameterized pure function* `(worldPoint, MapFrame) → uv`,
  where `MapFrame` carries center / span / rotation as plain data. This is the projection math
  lifted OUT of the mod (it currently lives welded to the vanilla minimap in
  `MinimapUpdateBiomePatch.TryGetHoverWorldPosition`) and generalized.
- **Polygon triangulation** for fills (region polygon → triangle indices), pure.
- Consumable by a NON-Unity consumer: a CLI tool, a server-side mod, a different engine.
- **This is Daniel's "raw geometry."**

### Tier 2 — Unity helpers (`WorldZones.Unity`, `net472`-only, references `UnityEngine` ONLY)
Thin typed adapters over Tier 1 for the common Valheim-map rendering scenarios. References
`UnityEngine` and **nothing else** — no `assembly_valheim`, no BepInEx. Net472-only (Unity's Mono
runtime), so it is NOT in the net8 headless test path — which is fine *because it carries no
algorithm*, only type adaptation. **LOCKED surface for steps 2–3 (2026-06-25): exactly the three
builders + the one data block below — not the cathedral.** The signatures are pinned in
`## Steps 2–3 lock`; the prose here is the why.
- `MapUvProjector` — Unity `Vector2`/`Vector3` wrapper over the Tier-1 `MapProjector`/`MapFrame`.
- `RegionUiVertexFiller` — populate a `VertexHelper` for a `MaskableGraphic` overlay under
  `m_pinRootLarge` (the RawImage-overlay case — vanilla M map, later a Trailborne disc/modal). **This
  is the border-INK primitive** (mesh: crisp, restyles live, vector at any zoom).
- `RegionTextureBaker` — the `regionIdGrid` raster → `Texture2D` (palette / fill). **This is the
  area-FILL primitive** (one quad, pans + zooms for free via `uvRect`; soft drawn-map edge). Feeds
  the borders+tint and parchment dial stops.
- `ValheimMapConventions` — the Valheim map constants as **DATA, not a game reference**: 64 m/texel
  (`m_pixelSize`), 256² fog texture, the M-map UV formula, world radius / edge wall. So a consumer
  does not re-derive them. **This is the "valheim-modding-informed" knowledge — encoded, not coupled.**
- **This is Daniel's "unity-helpers ... to simplify common valheim-modding-informed rendering
  scenarios."**

> **Deferred helpers (NOT in steps 2–3 — later scenarios, do not build the cathedral):**
> `RegionFillMeshBuilder` (polygon→tint `Mesh`) is dropped off the critical path because Daniel's
> 2026-06-25 call ships **texture** fills, not mesh fills — the `regionIdGrid` raster feeds the
> texture directly, which also drops polygon triangulation from the path. `RegionInkMeshBuilder`
> (a standalone border `Mesh` decoupled from a `VertexHelper`), `RegionLineRendererBinder` (the
> ground-projected world-line ESP), and `RegionLabelAnchorer` (centroid→anchored label) are real
> future scenarios but unbuilt until a host needs them. A `RegionTextureBaker` SDF-border mode is a
> later enrichment of the same baker, not a new type.

### Tier 3 — game-coupled consumers (`WorldZones.Mod.RegionOverlay`, + Trailborne's adapter)
References Unity + Valheim + BepInEx. Reads the live game objects (`Minimap.instance.m_mapImageLarge`,
`m_pixelSize`, terrain height, the `IsHoverExplored` fog gate) and feeds them into Tier 2. This is
the **thin, per-game-version, swappable** layer — where Valheim 1.0 / Deep North churn lands,
quarantined away from the substrate. The standalone `WorldZones.Mod.RegionOverlay` is Tier 3;
**Trailborne's region adapter is ALSO Tier 3, on Trailborne's side, as a soft-dependency.**

## The seam contract (what a customer-zero leans on)

1. **Geometry is keyed by stable `RegionKey`**, never the int BFS index (which renumbers on any
   seed change — see `docs/design/region-identity.md`).
2. **Boundaries are world-space meters; projection is a separate parameterized step.** A consumer
   that has its own map surface (Trailborne's disc, a future regional-map item) supplies its own
   `MapFrame` and never touches our projection presets if it doesn't want them.
3. **Tier 1 + Tier 2 name nothing game-specific beyond documented vanilla constants, and nothing
   Trailborne.** The one-way arrow holds by construction.
4. **Versioning:** Tier 1 / Tier 2 are game-version-independent (stable). Tier 3 tracks the game
   version. A consumer pins Tier 1/2 and owns its own Tier 3.

## The "Valheim-informed vs Valheim-coupled" call (✅ LOCKED 2026-06-25 — Tier 2 stays `UnityEngine`-only)

The genuinely game-coupled convenience — reading `m_pixelSize` off a *live* `Minimap` to build a
projector in one line — could have lived in a `WorldZones.Valheim` assembly that references
`assembly_valheim`. **LOCKED: do NOT.** Tier 2 is `UnityEngine`-only; the mod (Tier 3, which already
references the game) does the one-line live read (`m_pixelSize` / `m_textureSize` / `uvRect`) and
passes those constants into Tier 2 via a `MapFrame` + `ValheimMapConventions`. Rationale (unchanged):
a reusable helper lib that hard-references game assemblies must be version-matched to the game and
breaks on 1.0 — a bad trade for a substrate meant to outlive game versions. A thin game-coupled
convenience shim can still bolt on later if ergonomics demand it (reversible) — but it is NOT in
steps 2–3.

## Test discipline (why this layering, beyond ergonomics)

Every algorithm — boundary extraction, projection math, triangulation — lives in **Tier 1**, under
the **net8 headless test path**. Tier 2 is a typed shell verified by compile (Linux, via
`VALHEIM_MODDED_PATH`) + the in-world walk; Tier 3 by the in-world walk. This directly answers the
roadmap's #1 anxiety (the deleted ground-truth guard): the math that can silently break stays under
test; the un-headless-testable surface is thin and walk-verified, not load-bearing.

## Build order

1. **Tier 1 geometry** — boundary extraction (`regionIdGrid` → per-`RegionKey` polylines/rings) +
   the parameterized projection function + triangulation. Headless-tested. Eye-validatable offline
   (the border-explorer / map renderer already render region fills). **← start here.**
2. **Tier 2 Unity helpers** — the adapter set above. Compile-verified on Linux.
3. **Tier 3 standalone draw** — `WorldZones.Mod.RegionOverlay` consumes Tier 2 to draw borders on
   the vanilla map (nomap-OFF / no-Trailborne). First thing actually worth installing.
4. **Trailborne adapter** — Tier 3 on Trailborne's side: a region layer its `MapSurface` consumes
   each rebuild (the `IThreatMarkerProvider` pattern), under nomap-ON where the vanilla map is gone.

## Steps 2–3 lock (the buildable spec — 2026-06-25)

This is the precise, buildable surface for build-order steps 2 (Tier-2 `WorldZones.Unity`) and 3
(Tier-3 borders-only live draw). The impl card (`t_db872f11`, assignee `engineer-ui`) builds exactly
this; the QA card (`t_2dd1dd15`) verifies it. **Every type/signature below is bound to a real
committed Tier-1 type** (commit `89926ed`) **or a decomp-verified vanilla constant** — nothing here
is invented. Acceptance criteria are NAMED (`AC-T2-*`, `AC-T3-*`) so QA can check them one by one.

### The render-primitive decision (CANONICAL — resolves old open lock #4)

**BOTH primitives ship.** They do different jobs and the style dial is literally the crossover:
- **Mesh ink** for the border seams — crisp, restyles live, vector-sharp at any zoom. Built by
  `RegionUiVertexFiller` into a `VertexHelper` under `m_pinRootLarge`.
- **Baked texture** for area fills / parchment — one quad, pans + zooms free via `uvRect`, soft edge
  = drawn-map look. Built by `RegionTextureBaker` from the `regionIdGrid` raster the runtime already
  holds. This drops polygon triangulation + a fill-mesh builder OFF the critical path.

### Step 2 — `WorldZones.Unity` (`net472`, references `UnityEngine` ONLY)

New project `src/WorldZones.Unity/WorldZones.Unity.csproj`. **References: `UnityEngine.CoreModule`,
`UnityEngine.UIModule`, `UnityEngine.UI` (for `VertexHelper` / `MaskableGraphic`) — and a
`ProjectReference` to `WorldZones.Runtime` (Tier 1). NO `assembly_valheim`, NO BepInEx, NO Harmony.**
That quarantine is the whole point (constitution §II); the headless build (below) proves it by
resolving only the client Managed DLLs. `net472`-only — it carries no algorithm, only type adaptation,
so it is correctly outside the net8 test path. Namespace `WorldZones.Unity` (drop any type prefix).

Three builders + one data block. Pinned signatures:

**`ValheimMapConventions`** — the vanilla map constants as DATA (decomp-verified against
`Minimap`, sbpr-corpus `subsystems/Minimap.cs`). NOT a live game read; Tier 3 passes the live values
in, these are the documented defaults + the formula:
```csharp
public static class ValheimMapConventions
{
    public const float DefaultPixelSize  = 64f;   // Minimap.m_pixelSize  (Minimap.cs:213) — metres per texel
    public const int   DefaultTextureSize = 256;   // Minimap.m_textureSize (Minimap.cs:211) — fog/map tex is 256²
    public const float WorldRadius        = 10000f; // ZoneGrid.WorldRadius — region-GEN grid radius.
                                                    // (Real walled world is ±10500: EnvMan edge-of-world,
                                                    // Player pushback @10420. Real ForTheWort regions reach
                                                    // centroid 10008 m / bounds-corner 10637 m — see below.)

    // Full-map world span = pixelSize * textureSize = 64 * 256 = 16384 m (±8192 m ON-AXIS around origin).
    //
    // 🔴 LOAD-BEARING (the thing that bites): when drawing OVER the vanilla M map, build the MapFrame
    //    from THIS span (16384), NOT MapFrame.WholeWorld() (span 20000). They are different frames:
    //    WholeWorld(10000) is the OFFLINE-render frame (whole world on one synthetic map); the vanilla
    //    M map is the texture frame. Cross them and every border renders at 16384/20000 = 0.82× scale,
    //    mis-registered against the terrain underneath. Our overlay shares vanilla's WorldToMapPoint,
    //    so it clips coincident with vanilla automatically — do NOT special-case rim clipping.
    //
    // The texture is a SQUARE over a CIRCULAR world, so coverage is non-uniform (NOT a clean rim):
    //    • on-axis (N/S/E/W): texture edge 8192 m  <  world wall 10500 m → under-reaches ~2308 m of rim.
    //    • diagonal corners:  texture reach 8192·√2 ≈ 11585 m  >  10500 m → over-reaches ~1085 m (ocean).
    //    This is vanilla's own behaviour; our borders inherit it for free by sharing the projection.
    public static float FullMapWorldSpan(float pixelSize = DefaultPixelSize, int textureSize = DefaultTextureSize)
        => pixelSize * textureSize;

    // The vanilla WorldToMapPoint formula (Minimap.cs:1496), as a MapFrame the Tier-1 projector
    // consumes. centre = world origin; span = pixelSize*textureSize on both axes; rotation 0 (M map
    // is north-up). A consumer with a live Minimap passes minimap.m_pixelSize / m_textureSize here.
    public static MapFrame FullMapFrame(float pixelSize = DefaultPixelSize, int textureSize = DefaultTextureSize)
        => new MapFrame(0.0, 0.0, pixelSize * textureSize, pixelSize * textureSize, 0.0);
}
```
- **AC-T2-CONV-1:** `FullMapFrame()` round-trips a known world point to the same UV the vanilla
  `WorldToMapPoint` produces (e.g. world `(0,0)` → uv `(0.5,0.5)`; world `(8192,8192)` → uv `(1,1)`).
- **AC-T2-CONV-2:** the constants match the decomp (`64f`, `256`) — not re-derived, not guessed.

**`MapUvProjector`** — Unity `Vector2`/`Vector3` wrapper over the committed Tier-1
`MapProjector` + `MapFrame`. Pure type adaptation, zero new math (the math is Tier-1, under test):
```csharp
public static class MapUvProjector
{
    // world (XZ of a Vector3, or a Vector2) -> normalised map UV as a Unity Vector2.
    public static Vector2 Project(Vector3 world, MapFrame frame);   // uses world.x, world.z
    public static Vector2 Project(Vector2 worldXZ, MapFrame frame);
    // inverse: normalised UV -> world XZ (Vector2). Mirrors MapProjector.Unproject.
    public static Vector2 Unproject(Vector2 uv, MapFrame frame);
}
```
- **AC-T2-PROJ-1:** `Project` delegates to `MapProjector.Project` (convert `Vector3.x/z` →
  `WzVec2`, `MapUv` → `Vector2`); no projection arithmetic re-implemented in Tier 2.
- **AC-T2-PROJ-2:** `Unproject(Project(w)) ≈ w` within float epsilon (round-trip).

**`RegionUiVertexFiller`** — the border-INK primitive. Consumes the Tier-1
`RegionBoundaryGraph.Segments` (the stroke-once `BorderSegment` set) — or the refined
`IReadOnlyList<RefinedBorder>` polylines from `RegionBoundaryRefiner` — projects each endpoint with
`MapUvProjector`, and emits quad strokes into a uGUI `VertexHelper` for a `MaskableGraphic` mounted
under `m_pinRootLarge`:
```csharp
public sealed class RegionUiVertexFiller
{
    public RegionUiVertexFiller(MapFrame frame, Rect uiRect, float strokeWidthPx, Color32 ink);

    // Stroke the deduplicated seams (borders-only style). uvRect maps the frame's [0,1] UV onto the
    // RawImage's currently-displayed sub-rect (vanilla pans/zooms via uvRect — pass minimap.m_mapImageLarge.uvRect).
    public void FillSegments(VertexHelper vh, IReadOnlyList<BorderSegment> segments, Rect uvRect);
    // Stroke refined contour-hugging arcs instead of the 64 m staircase (same ink, richer line).
    public void FillPolylines(VertexHelper vh, IReadOnlyList<RefinedBorder> borders, Rect uvRect);
}
```
- **AC-T2-INK-1:** one `BorderSegment` → one stroked quad (two triangles) in the `VertexHelper`;
  N segments → N quads, no double-stroke (the segment set is already deduplicated in Tier 1).
- **AC-T2-INK-2:** a segment whose projected UV falls outside `uvRect` (panned off-screen) is
  clipped/skipped, not drawn off the rect.
- **AC-T2-INK-3:** stroke width is in UI pixels (constant on screen), independent of map zoom.
- **AC-T2-INK-4:** `UnityEngine`-only — no `Minimap`/`assembly_valheim` symbol in the file.

**`RegionTextureBaker`** — the area-FILL primitive. Consumes the runtime's
`int[,] regionIdGrid` (`RegionWorld.RegionIdGrid`, indexed `[gy, gx]`, `< 0` = unassigned) + a
region-key→palette-index map, and bakes a `Texture2D` a consumer samples under the map quad via
`uvRect`:
```csharp
public sealed class RegionTextureBaker
{
    // palette is indexed by the SAME int label the grid carries (RegionInfo.TransientId);
    // unassigned (id < 0) bakes to transparent. minIndex = RegionWorld.Grid.MinIndex (grid origin).
    public Texture2D Bake(int[,] regionIdGrid, int minIndex, IReadOnlyList<Color32> paletteByLabel,
                          Color32 unassigned);
    // The uvRect a consumer sets on its fill RawImage so the baked texture aligns to the SAME world
    // span as the vanilla map (so fills register under the ink). Derived from ValheimMapConventions.
    public Rect WorldAlignedUvRect(MapFrame mapFrame);
}
```
- **AC-T2-FILL-1:** texel `[gy,gx]` paints `paletteByLabel[regionIdGrid[gy,gx]]`; `id < 0` →
  `unassigned` (transparent for an overlay fill).
- **AC-T2-FILL-2:** the baked texture's world span aligns to `ValheimMapConventions` so a fill quad
  registers under the ink strokes (no half-texel drift between fill and border).
- **AC-T2-FILL-3:** colorblind-safe is NOT baked here — the palette is supplied by Tier 3; the baker
  is hue-agnostic. (Tier 3 chooses lightness-stepped palettes; see step 3.)

> **`regionIdGrid` orientation is load-bearing:** Tier-1's `RegionBoundaryExtractor` reads the grid
> `[gy, gx]` with world corner `(cx+minIndex)*64 − 32`. `RegionTextureBaker` MUST use the identical
> indexing + origin so the fill texture and the ink seams share one coordinate frame. The impl card
> carries this as a cross-check against `RegionBoundaryExtractor.Corner`.

### Step 3 — Tier-3 borders-only live draw into `RegionOverlay`

Extend the EXISTING `WorldZones.Mod.RegionOverlay` (don't start cold). The mod already has: the
inverse transform (`MinimapUpdateBiomePatch.TryGetHoverWorldPosition`, the world↔map-UV math we
generalised into Tier 1), the name labels (`MinimapLabelController`), and the realised-region data
(`RegionWorld` via `WorldZonesRuntime.Build`). Add:

1. **A style-mode enum cycled by ONE hotkey:**
   ```csharp
   public enum RegionOverlayStyle { Vanilla, Borders, BordersTint, Parchment }
   ```
   Cycle order `Vanilla → Borders → BordersTint → Parchment → Vanilla`. **Resting default =
   `Borders`** (borders-only — the cheap useful version). Each stop draws:
   | Style | Ink (mesh) | Fill (texture) |
   |---|---|---|
   | `Vanilla` | off | off |
   | `Borders` (default) | on | off |
   | `BordersTint` | on | translucent fill over vanilla terrain |
   | `Parchment` | on | opaque fill, terrain read replaced |
   Every default is a **TWEAK-ME starting dial, reversible via the enum** — Daniel tunes stroke width,
   tint alpha, palette, and the resting default in-world; none are baked constants.

2. **The mount** — a `MaskableGraphic` (ink) + a `RawImage` (fill) parented under
   `m_pinRootLarge` (the pin layer ABOVE the RawImage map — NOT a shader touch; old approach ①).
   Per the loaded uGUI failure-mode skill: **toggle a `_content` child via `SetActive`, never the host
   GameObject** (a host that deactivates itself kills its own `Update` pump), and **log an explicit
   mount-success line** (don't trust "patch applied" as proof the overlay drew).

3. **Fog gate (CORRECTION — read carefully, the card's shorthand was imprecise):** the regions reveal
   **fog-respectingly** (a region's borders/fill draw only where explored). The existing
   `IsHoverExplored` in `MinimapUpdateBiomePatch` is a **single-point hover-label** check
   (`!string.IsNullOrWhiteSpace(minimap.m_biomeNameLarge.text)`) — it gates the *hover name*, it is
   **NOT an area reveal mask** and cannot fog-gate a whole border layer. The real per-pixel reveal is
   vanilla `Minimap.m_explored[]` / `Minimap.m_exploredOthers[]` via `private bool IsExplored(Vector3)`
   (`Minimap.cs:1620`). **Both the array and the method are PRIVATE** (decomp-verified). So the impl
   must reach them through a **cached reflected handle** — the SAME pattern the mod already uses for
   the private `WorldGenerator.GetRiverWeight` (`RegionOverlayPlugin.BuildRiverResolver`). Gate each
   drawn seam/fill-texel on `IsExplored(worldPoint)`; if the reflected handle can't be found (a future
   version rename), **degrade to drawing nothing fog-gated and log a warning** — never silently draw
   the whole unfogged map (that spoils the world, the exact failure the fog mock rejected).
   - **AC-T3-FOG-1:** a region the player has NOT explored draws no ink and no fill.
   - **AC-T3-FOG-2:** the reflected `IsExplored` handle is resolved once + cached; a resolution
     failure logs a warning and disables the overlay (no unfogged fallback draw).

4. **The draw path (must be traceable end-to-end for QA):** hotkey → cycle enum → select ink/fill per
   the table → project the cached `RegionBoundaryGraph` (built once per world via
   `RegionWorld.BuildBoundaryGraph()`, cached — NOT per frame) with a `MapFrame` from the live
   `Minimap` (`m_pixelSize`/`m_textureSize`/`uvRect`) → `RegionUiVertexFiller.FillSegments` for ink,
   `RegionTextureBaker.Bake` for fill → fog-gate via reflected `IsExplored` → mount under
   `m_pinRootLarge`.
   - **AC-T3-DRAW-1:** `BuildBoundaryGraph()` is called once per world load and cached, not per frame.
   - **AC-T3-DRAW-2:** the hotkey actually advances the enum and the visible layers change per the table.
   - **AC-T3-DRAW-3:** mount logs a success line; the host is not `SetActive(false)`-ing itself.
   - **AC-T3-CB-1 (colorblind):** `BordersTint`/`Parchment` palettes differentiate adjacent regions by
     **lightness + hatch + label, never hue** (Daniel is colorblind). The default palette is a
     lightness ramp, TWEAK-ME.

### Headless compile constraint (the impl card carries the recipe)

`WorldZones.Unity` MUST compile-verify on this Linux box. Because it is `UnityEngine`-only it needs
only the client Managed DLLs (no BepInEx) — all present at
`~/.local/share/Trailborne/Valheim-Modded/valheim_Data/Managed/` (verified 2026-06-25:
`UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.UIModule.dll`, `UnityEngine.UI.dll` all
present — `UnityEngine.UI.dll` carries `VertexHelper`/`MaskableGraphic`). The Tier-3 mod build is
already proven on this box (`regionoverlay-build-and-esp.md`). The recipe:
```bash
ROOT=~/.cache/wz-modref
mkdir -p "$ROOT/valheim_Data"
ln -sfn ~/.local/share/Trailborne/Valheim-Modded/valheim_Data/Managed "$ROOT/valheim_Data/Managed"
ln -sfn ~/valheim/niflheim/data/bepinex/BepInEx "$ROOT/BepInEx"
VALHEIM_MODDED_PATH="$ROOT" dotnet build src/WorldZones.Unity/WorldZones.Unity.csproj -c Release
VALHEIM_MODDED_PATH="$ROOT" dotnet build src/WorldZones.Mod.RegionOverlay/WorldZones.Mod.RegionOverlay.csproj -c Release
```
- **AC-BUILD-1:** `WorldZones.Unity` → `Build succeeded. 0 Warning(s) 0 Error(s)`, `net472`.
- **AC-BUILD-2:** the mod project still builds clean after the Tier-3 wire.
- **AC-BUILD-3:** the Tier-1 net8 test net stays green (the projection the overlay leans on is tested).

### The honesty bar (carried into both child cards)

**Compile-verified ≠ playable.** This box has no GPU Valheim client. The impl proves the build + a
traceable draw path; it CANNOT prove the borders render correctly over the real map with real fog —
that is Daniel's in-world walk (the one true gate, same wall as the ESP). The QA card authors the
walk checklist; nobody upgrades "reasoned" to "verified."

## Region-overlay v2 — refined-arc lines + bake-source bleed fix (Path A uGUI) + shader-composite fill (Path B), one swappable dial (✅ LOCKED 2026-06-25; bleed approach corrected 2026-06-28)

> The borders-only overlay RENDERS in-world (verified 2026-06-25, Daniel's walk on a clean reference
> install: stock Valheim + stock BepInEx + only WorldZones.RegionOverlay, world SolidHmm). Two quality
> gaps remain — Daniel's words: **"all blocky"** + **"boundaries stretch into the beyond."** v2 closes
> both, and ships the fill backend as a SWAP (uGUI vs shader) so a modder sees both patterns. Impl A
> = `t_aae2c4e9`; impl B = `t_40737416`; both gated on this lock. **The disc-clip Open Lock is
> decisively resolved below — NO mask; bleed is clipped at the bake source (corrected 2026-06-28).**
> Daniel gates the merge.

### The two gaps

1. **BLOCKY** — lines + fill are the raw 64 m zone staircase.
2. **BLEED** — the fill rectangle (and rim arcs) spill past the circular map disc into the black
   starfield (3 grey bars off the left rim in Daniel's screenshot). **Cure (shipped 2026-06-28):** a
   transparent alpha-0 edge ring baked into every Clamp-sampled layer — no mask. See below.

### What's already built (REUSE — do not rebuild)

- `RegionBoundaryRefiner.RefineCoastlinesSmoothed(graph, heightField)` + `RefineBiomeSeams(graph,
  biomeField)` (`src/WorldZones.Runtime/Geometry`) — the smooth contour-hug polylines, tested on real
  Niflheim, emitted by `gazetteer --boundaries` (224 coast arcs + 59 biome-seam arcs vs 12,541 coarse
  64 m seams on seed bkpcEynZXm3). **The geometry is real and proven — it is simply not wired into the
  live draw.**
- `RegionUiVertexFiller.FillPolylines(vh, IReadOnlyList<RefinedBorder>, uvRect)` (`WorldZones.Unity`)
  — strokes refined arcs; **exists, unused by the live path.**

The **live wiring gap (the actual bug):** `RegionOverlayController.cs:136-140` strokes
`this.visibleSegments` (filtered from `graph.Segments` = the raw 64 m staircase) via `ink.SetSegments`.
v2 routes the ink through the refined arcs instead.

### Layer 0 — shared arc substrate (built once per world load)

Build + cache the refined arcs at the SAME site that already caches the boundary graph:
`RegionOverlayPlugin.cs` → `CacheOverlayGeometry` (called from `TryInitializeRegionData`, right after
`regionDataReady = true`). **Wiring fact (verified):** the live `ValheimWorldSampler`
(`: IWorldSampler`) is a LOCAL in `TryInitializeRegionData` (the `new ValheimWorldSampler(...)` a few
lines above the `Build` call) and is **in scope at the `CacheOverlayGeometry` call** — so pass it in.
`HeightScalarField(IWorldSampler, isoLevel=CoastIso=25)` and `BiomeCategoryField(IWorldSampler)` both
take the sampler directly. **`CacheOverlayGeometry` already extracts the graph** (from
`ProtoResult.Regions` id→key) and hands it to `SetWorld` — v2 just adds the arc refinement and a third
`SetWorld` arg. The flow:

```
CacheOverlayGeometry(regionWorld, sampler):              // ← add the sampler param
    idToKey   = ProtoResult.Regions  (Id→RegionKey)      // EXISTING — flag-independent, keep verbatim
    graph     = RegionBoundaryExtractor.Extract(grid, minIndex, idToKey)   // EXISTING — reuse, don't re-source
    heightF   = new HeightScalarField(sampler)            // default 25 m coast iso
    biomeF    = new BiomeCategoryField(sampler)
    arcs      = RefineCoastlinesSmoothed(graph, heightF)  // 224 coast arcs
              ∪ RefineBiomeSeams(graph, biomeF)           // 59 biome-seam arcs  → one List<RefinedBorder>
    controller.SetWorld(graph, arcs, grid, minIndex)      // ← SetWorld gains the arcs param; cache them
```

This is **byte-for-byte the headless gazetteer path** (`Gazetteer.cs:294-297` — same two refiners,
same fields, same default isos). So the in-world arcs == `{seed}_boundaries.json` == what players walk
(the consistency bar from the border work). **Run the arcs on the graph `CacheOverlayGeometry` already
extracts** (real durable keys from `ProtoResult.Regions`) — do NOT introduce a SECOND graph source.
Sourcing id→key from `ProtoResult.Regions` is **independent of the `ComputeRegionInfo` flag** (it is
always populated; `ProtoRegion.Id` = grid label, `ProtoRegion.RegionKey` = durable key) — note the live
build now sets `ComputeRegionInfo=true` for rich naming (2026-06-25), but the overlay geometry path is
deliberately decoupled from that flag, so it neither breaks nor needs changing if the flag flips back.

- **AC-V2-L0-1:** refined arcs (coast ∪ biome-seam) are built ONCE per world load at the cache site,
  never per frame (the existing `RegionBoundaryGraph` caching contract — AC-T3-DRAW-1 — extended).
- **AC-V2-L0-2:** arcs are built on the graph `CacheOverlayGeometry` already extracts (id→key from
  `ProtoResult.Regions`, flag-independent), NOT a freshly-sourced second graph.
- **AC-V2-L0-3:** arcs use the same refiner + default isos as `gazetteer --boundaries`, so the in-world
  geometry equals the shipped dataset (same refiner, same 25 m coast iso).

### Layer 1 — Path A (uGUI), ships first ("looks good, ships now")

**Lines — stroke refined arcs, not the staircase.** Route the ink graphic through
`RegionUiVertexFiller.FillPolylines(vh, arcs, uvRect)` instead of `FillSegments(...,
graph.Segments)`. `RegionInkGraphic` gains a `SetBorders(IReadOnlyList<RefinedBorder>, ...)` parallel
to `SetSegments` and `OnPopulateMesh` calls `FillPolylines`. **Fog-gate at SUB-SEGMENT granularity —
this is load-bearing, do NOT gate whole arcs:** a refined arc is a long chained polyline (many
vertices spanning explored AND unexplored fog); gating it by "either endpoint of the whole arc" would
reveal unexplored interior (spoiling the world — the exact failure the fog mock rejected). The
controller walks each cached arc PER VERTEX-PAIR and emits only the sub-runs whose local endpoints
touch explored fog (the same per-pair `IsExplored` test the old `BuildVisibleSegments` ran on each 64 m
seam, now applied to each arc sub-segment), then hands those visible fragments to `FillPolylines`.
Net **cheaper** than today's path — fewer total vertices than 12,541 seams × 2 endpoints — so no
per-frame regression (the reflected `IsExplored` cost already paid by the seam path shrinks).

**Fill — keep `FilterMode.Point`; do NOT bilinear.** The card floated "bilinear interim, or
fill-from-arcs." **Bilinear is REJECTED (grounded):** the baker's own doc-comment
(`RegionTextureBaker.cs:84-88`) keeps Point specifically so "adjacent region colours stay pure (no
cross-seam bleed under colourblind lightness stepping)" — bilinear interpolates a muddy intermediate
LIGHTNESS band along every seam, which directly defeats Daniel's colourblind lightness palette. So
**Path A's fill stays Point (flat, pure-colour, intentionally 64 m-blocky) read UNDER the crisp refined
arcs** — the smooth lines carry the region-shape read; the fill is a flat tint behind them. The
default style is `Borders` (no fill at all), so the blocky fill only shows in `BordersTint`/`Parchment`.
**Shape-accurate per-pixel fill is Path B's deliverable, not a bilinear fudge here** — this is exactly
why both paths exist (genuinely different fill-quality patterns to demonstrate).

- **AC-V2-A-LINE-1:** the live ink strokes the cached refined arcs via `FillPolylines`, never
  `graph.Segments`.
- **AC-V2-A-LINE-2:** arcs fog-gate at sub-segment granularity — an unexplored region draws no line;
  a region at the fog frontier draws its explored sub-runs only.
- **AC-V2-A-LINE-3:** the refined-arc draw is net ≤ the old per-seam cost (no new per-frame regression).
- **AC-V2-A-FILL-1:** fill stays `FilterMode.Point` (colourblind palette purity preserved — bilinear
  rejected). Path A fill is intentionally flat-tint; shape-accurate fill is Path B.

**Bleed fix — bake a transparent edge ring (corrected 2026-06-28; NO mask).** A circular uGUI Mask was
tried here and REMOVED: it only clipped the smear to a disc (bars still ran to the disc edge), forced a
round clip on a rectangular map, and relied on a runtime sprite. The shipped fix is source-level: every
Clamp-sampled layer (fill + halo) bakes a transparent alpha-0 border, so when the displayed `uvRect`
runs past `[0,1]` Clamp repeats a transparent edge texel — nothing to smear, full rectangular map
preserved. No stencil component, no `_content` clip subtree:

```
_content  (existing toggled child under the mount root)
   ├─ WZ_RegionFill (RawImage)   ← alpha-0 edge ring baked into the fill texture
   └─ WZ_RegionInk  (RegionInkGraphic : MaskableGraphic)
```

**Mount stays under `m_pinRootLarge` (least-disruptive — the existing working draw is unchanged).**
The bake-source approach needs no asset bundle and no runtime sprite, sidestepping the `Knob.psd`
builtin-sprite gotcha the same way `RegionInkGraphic` uses `s_WhiteTexture`.

- **AC-V2-A-EDGE-1:** every Clamp-sampled layer (fill + halo) carries a transparent alpha-0 border in
  its bake — nothing draws into the black starfield corners (bleed gone for lines AND fill).
- **AC-V2-A-EDGE-2:** the full rectangular map stays visible (no round clip imposed); shipped `6d48e51`.
- **AC-V2-A-EDGE-3:** no stencil/Mask component and no runtime sprite (Knob.psd-class load failure
  avoided by construction). Verify: `grep -c 'new GameObject("WZ_DiscClip"' RegionOverlayController.cs`
  == 0; live block at `RegionOverlayController.cs:497–504` "No clip mask."

> **History (2026-06-28):** AC-V2-A-CLIP-1/2/3 (one uGUI Mask on the content root, code-generated circle
> sprite, concentric/co-sized with the vanilla disc) are RETIRED. The mask only ever clipped the smear to
> a disc and was killed for good in `6d48e51`. There is no mask subtree to mount or reparent.

### Layer 2 — Path B (shader composite), follow-on (the fragile one)

Fold the region FILL into a minimap compose pass the way vanilla composites `_FogTex` — feed the
held `regionIdGrid` (`RegionOverlayController.cs:54`) + palette as a texture to a compose material,
paint fill per-pixel. Per-pixel region-id sampling gives **sharp region edges at screen resolution
with pure palette colours** — this is the shape-accurate fill Path A's Point texture cannot be, and the
real "fill tracks the region SHAPE" win. **Lines REUSE Path A's refined-arc layer — do NOT re-implement
lines in-shader.**

Precedent: vanilla's `m_mapLargeShader` composites `_MainTex/_MaskTex/_HeightTex/_FogTex` in one pass
(`Minimap.cs:435-438`), and JotunnLib ships `MinimapComposeOverlay.shader` as the documented
"add-your-own-compose-layer" pattern (`jotunn-source/.../Resources`).

**The injection-point fork (Path B card resolves against the live material):**
- **Inject-into-vanilla-material** (region fill added as an extra blended layer in `m_mapImageLarge`'s
  OWN material): disc + fog + pan/zoom **inherited free** (it paints inside the RawImage vanilla
  already clips). Most invasive — owns the vanilla map material, fights `Minimap.Start` which
  re-instantiates `m_mapImageLarge.material` every Start (`Minimap.cs:431`), so the injection must
  survive / re-apply across world reloads.
- **Separate overlay RawImage** (JotunnLib `MinimapManager` style — a custom RawImage with a compose
  material): lower-risk injection, but does **NOT** inherit the disc → must **reuse Path A's
  manufactured circle Mask** (shared seam). This is the safer default.

- **AC-V2-B-1:** region fill renders as a shader compose pass (region-id texture + palette), fog-gated
  + disc-bounded + pan/zoom-tracking.
- **AC-V2-B-2:** Path B reuses Path A's refined-arc LINE layer (lines not re-implemented in shader).
- **AC-V2-B-3:** if inject-into-vanilla-material fights `Minimap.Start` (line 431) or the clip is not
  inherited, Path B falls back to a separate overlay RawImage REUSING Path A's circle Mask — and the
  impl card `kanban_comment`s + blocks for re-spec if materially larger than assumed (the
  `Knob.psd`-class material-surgery risk is real — do not under-budget).

### The swap seam (named explicitly — Daniel's reversible-architecture preference)

The fill backend is a SWAP **orthogonal to the style dial.** The existing dial
`RegionOverlayStyle { Vanilla, Borders, BordersTint, Parchment }` decides WHETHER fill draws; a NEW
config flag decides HOW:

```csharp
public enum RegionFillBackend { UguiTexture, ShaderCompose }   // Path A vs Path B
```

The seam lives in the controller's `Render`: the **fill branch** (`if (style.DrawsFill())`,
`RegionOverlayController.cs:148-159`) switches on `RegionFillBackend` — `UguiTexture` → the existing
`EnsureFillTexture` + RawImage path (Path A); `ShaderCompose` → the compose-material path (Path B).
The **ink branch** (lines 136-145) is backend-INDEPENDENT — always the refined-arc `FillPolylines`.
So both backends ship, the LINE layer is shared, and the swap is one config flag.

- **AC-V2-SWAP-1:** a named `RegionFillBackend { UguiTexture, ShaderCompose }` dispatches the FILL
  branch only; the LINE branch is identical for both backends.
- **AC-V2-SWAP-2:** flipping the flag swaps fill rendering with no change to lines, fog gate, or the
  style dial; both backends ship and are documented as reference patterns.

### OPEN LOCK — what clips the vanilla large map to a circle (✅ RESOLVED, decomp-grounded)

The architect owed (a) what clips vanilla to a disc, (b) how Path A clips, (c) whether Path B is free.

**(a) What makes the vanilla large map circular — it is NOT in code; the overlay does NOT inherit it.**
Three converging decomp findings (`sbpr-corpus/subsystems/Minimap.cs`, vanilla — clean-room-fair to
read per ADR-0001):
  - `GenerateWorldMap` (Minimap.cs:1639-1682) paints the **entire square** 256² texture edge-to-edge —
    every texel gets a biome colour; **no radial alpha is baked into any of `_MainTex` / `_MaskTex` /
    `_HeightTex` / `_FogTex`.**
  - The map material composites those four textures with **no radial-clip term** — corroborated by
    JotunnLib's reverse-engineered `MinimapCompose{Overlay,MainR,Fog,Forest,Height}.shader`
    (`jotunn-source/.../Resources`), which carry **zero circle / radius / smoothstep math.**
  - Pins (siblings under `m_pinRootLarge`, the same layer family our overlay mounts in) are culled by
    `IsPointVisible` (Minimap.cs:1474-1482) — a **RECTANGULAR `uvRect` bounds test**, NOT a disc.
  ∴ The circular appearance is defined in the **prefab / material asset config** (a uGUI Mask or
    sprite mask on the map image, set in Unity scene data) — invisible to the C# decomp, and **not
    inherited by a layer injected as a sibling/child of `m_pinRootLarge`.**
  **Decisive corroboration:** JotunnLib's `MinimapManager` **manufactures its OWN `CircleMask` sprite
  + uGUI `Mask(showMaskGraphic=false)`** for its large-map overlay (`MinimapManager.cs:586` loads
  `CircleMask`; `609-614` adds `Image(sprite=CircleMask, preserveAspect=true)` + `Mask`) — the entire
  reason that code exists is that an injected map layer does **not** inherit the vanilla disc clip.

**(b) How Path A handles bleed (corrected 2026-06-28):** NOT a mask. Every Clamp-sampled layer (fill +
halo) bakes a transparent alpha-0 edge ring, so `uvRect` running past `[0,1]` clamps to a transparent
texel — nothing smears into the corners (Layer 1 above). A uGUI Mask was tried and removed (it only
clipped the smear to a disc and forced a round clip on a rectangular map); source-side borders need no
stencil and keep the full rectangular map visible.

**(c) Does Path B get bleed-handling free — CONDITIONALLY.** Only if Path B composites INTO the vanilla
map RawImage's OWN material (painting region fill as an extra blended layer in the same `m_mapImageLarge`
vanilla already clips) — then disc + fog + pan are inherited. If Path B adds a SEPARATE overlay
RawImage (the safer injection), it does **not** inherit and must reuse Path A's source-side transparent
edge-ring bake. The "free clip" is real but contingent on the injection point; the Path B card resolves
it against the live material (AC-V2-B-3).

### v2 honesty bar (carried into both child cards)

Same wall as steps 2–3: **compile-verified ≠ playable.** The impl proves build + a traceable draw
path; only Daniel's in-world walk on the clean reference install (world SolidHmm) proves the arcs draw
smooth, the source-side transparent edge ring kills the bleed, fog gates, and the dial + backend swap
read right. Nobody upgrades
"reasoned" to "verified." Colourblind judging throughout: lightness + hatch + label + RGB text, never
hue.

## Out of scope for steps 2–3 (deferred — do not build)

- **Trailborne adapter** (build-order step 4 — Tier 3 on Trailborne's side, soft-dependency).
- **The world-line ESP** (`RegionLineRendererBinder` + the `RegionBorderEsp` scaffold) — a separate
  ground-projected instrument, not the map overlay.
- **Parchment biome-glyph design** (old open lock #3) — only when/if parchment mode is actually built;
  with terrain hidden, parchment loses the biome read, so regions would need a biome glyph or faint
  biome-tinted paper. Design TBD then, not now.
- **The four deferred Tier-2 helpers** (`RegionFillMeshBuilder`, `RegionTextureBaker`-as-SDF,
  `RegionLineRendererBinder`, `RegionLabelAnchorer`) — later scenarios; don't spec the cathedral.

## The dataset SHIPS the geometry now — `{seed}_boundaries.json` sidecar (2026-06-25)

Tier-1 geometry was reachable in-process (`RegionWorld.BuildBoundaryGraph()` + the refiner) but the
HEADLESS DATASET did not carry it — the gazetteer emitted only the per-zone `{seed}_gazetteer_grid.bin`
raster, so any consumer that wanted the renderable boundary had to re-run the extractor + refiner
itself. The gazetteer CLI now has an **optional `--boundaries` flag** that emits
`{seed}_boundaries.json` alongside the existing outputs:

- **`segments`** — the deduplicated stroke-once `BorderSegment` seams (`a`/`b` world-metre endpoints,
  `keyA`/`keyB` region pair, `keyB:null` = coastline / region-vs-void). The borders-only render primitive.
- **`rings`** — closed `RegionRing` fill loops (`regionKey`, `isHole`, `signedAreaM2`, `vertices`).
  CCW outer / CW hole — the polygon-with-holes input a triangulator / UI-fill expects.
- **`coastArcs`** — the smoothed sub-zone coastline contour (`RefineCoastlinesSmoothed`, 25 m coast
  iso), the additive render-detail layer the overlay draws (chained → despiked → Chaikin).
- **`biomeSeamArcs`** — the smoothed region-vs-region biome-transition contour (`RefineBiomeSeams`).
- Standard provenance header (seed, worldId, portCommit, coordinateSpace, `valueSource:computed`).

The flag is **purely additive** (default off): the three existing files are byte-identical with or
without it (verified by SHA, modulo the always-present `generatedUtc` timestamp), so existing
grid-only consumers are unaffected. Real Niflheim (ForTheWort): 12,569 seams (11,956 coast), 178 rings
(162 outer + 16 holes), 201 coast arcs, 54 biome-seam arcs, ~2.5 MB. Deterministic across runs. The
arcs match what the overlay would draw in-world (same refiner, same iso) — so the dataset modders
consume == the tessellation players walk, the consistency bar from the border work. Join on `regionKey`
to `{seed}_gazetteer.json`. (CLI: `WorldZones.Cli gazetteer --seed <S> --output <dir> --inland-water
--boundaries`.)

## Compatibility watchdog (Daniel's ask: background Trailborne compat checks)

Stand up **once the seam exists** (after step 1–2 — guarding an empty surface is theater). Shape:
a **watchdog, not a reporter** — builds both mods, asserts the seam (the Tier-1/Tier-2 surface
Trailborne pins still has the shape it depends on; the patch targets still stack, don't truly
collide), and **stays silent unless something breaks.** You only hear from it on real drift.
Precondition verified: Trailborne builds on this box (`.sdk` present, prior `SBPR.Trailborne.dll`
artifact exists).

## Cross-references
- `docs/design/worldmap-region-overlay.md` — the in-world overlay design (style dial, fog gate).
- `docs/design/region-identity.md` — `RegionKey` (the stable key Tier 1 geometry is keyed by).
- `docs/partner-integrators/trailborne.md` — the consumer-specific seam + persistence hazards.
- SBPR `docs/design/map-provider-model.md` — Trailborne's cartography model (why nomap-ON deletes
  the vanilla map surface, why regions must render *through* `MapSurface` there).

## v2 walk-feedback FIX — bleed clip + ink-lag throttle (2026-06-26; resolution superseded 2026-06-28)

Daniel walked the v2 build (refined arcs + disc-clip) and reported: **only the central portion renders /
boundaries "freeze" and stop zooming out**, plus blocky fill and pan/zoom lag.

🔴 **FIRST ATTEMPT WAS WRONG — recorded so the mistake isn't repeated.** I initially diagnosed the freeze
as the screen-anchored `WZ_DiscClip` Mask over-clipping on zoom-in, deleted the Mask, and replaced it with
a **world-space disc cull** (`x²+z² ≤ R²`, R = 8192 m inscribed texture radius) folded into the fog gate.
That made the freeze WORSE and Daniel re-reported it on the new build. **Measured why:** the map texture is
a SQUARE — ±8192 m on-axis but ±11585 m to the corners — and real Niflheim border geometry reaches
**10352 m**, so a cull at R=8192 m **deletes 32.7% of genuine border vertices** (51088 arc verts, 16713
beyond 8192 m — measured via a throwaway Runtime probe). Deleting a third of the geometry, increasingly at
the rim, IS the "only the centre renders / won't zoom out" symptom. The lesson: the bleed was never
content beyond a WORLD circle; it's the square texture's corners painting into the black starfield, a
PANEL/UV-space phenomenon. Don't fix a UV-space clip in world space.

**⚠️ HISTORICAL — second attempt (PANEL-space Mask) was also abandoned, 2026-06-28:** This section once
claimed a "CORRECT FIX (shipped, verified)" restoring a circular uGUI Mask parented to
`m_mapImageLarge.transform`. **That fix was NEVER committed** (`git log -S 'm_mapImageLarge.transform' --
RegionOverlayController.cs` = 0) and the disc mask is now killed for good. The real history:
- The mask survived a `git reset` mishap (`f613f17` redo kept only F6), and `6d48e51` ("kill the disc
  mask — restore the source-level bleed fix lost in last night's reset") finally removed it. Rescue
  insurance parked at `parked/beam-bleed-clip-2026-06-27` + tag `rescued/ae49d64-f6-and-mask-kill`.
- **Bleed → source-level transparent edge ring** (`5027576`, "transparent edge-ring on fill+halo bakes
  to kill Clamp-smear beams"; finalized `6d48e51`): every Clamp-sampled layer bakes an alpha-0 border so
  there is nothing to smear past `[0,1]`. NO mask, full rectangular map preserved. Live: `RegionOverlay
  Controller.cs:497–504` "No clip mask"; AC-V2-A-CLIP-1/2/3 retired (see Layer 1, "Bleed fix").
- **Pan/zoom lag → throttle the reflected fog walk** (`ArcFogRefreshIntervalSeconds`=0.5s + forced on map
  (re)open), and re-project the mesh only when the arc set changed OR `uvRect` moved. This removes the
  per-frame fog cost on a static map and during pan (pan does only re-projection, not the fog walk).
  ⚠️ **PARTIAL** — the residual lag is the uGUI canvas rebuild of up to ~203k verts / ~305k indices
  (50837 sub-segment quads, measured) when re-projecting all visible arcs on a fully-explored map during
  an active drag. That's inherent to re-projecting every arc vertex each frame; reducing it needs
  view-frustum arc culling / LOD decimation at the displayed zoom — a separate measured optimization, not
  done here. Flagged honestly, not silently shipped as "fixed."
- **Blocky fill → UNCHANGED** (deferred Path B shape-accurate fill; default `Borders` has no fill).

Files: `RegionOverlayController.cs` (mask removed; edge-ring bleed fix is `5027576`/`6d48e51`).