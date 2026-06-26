# World-map region overlay — design exploration

> **Status:** DESIGN EXPLORATION opened 2026-06-23; **locks #1, #2, #4 RESOLVED 2026-06-25** (see
> `## Resolved locks` — fog-respecting reveal, borders-only default on a 4-stop dial, BOTH render
> primitives ship). **v2 LOCKED 2026-06-25** (refined-arc lines + disc clip + shader-fill backend —
> see `## v2: refined arcs + disc clip + swappable fill backend` below and the full buildable surface
> in `region-render-seam.md` `## Region-overlay v2`). The buildable surface lives in
> `region-render-seam.md` (`## Steps 2–3 lock` + `## Region-overlay v2`); this doc holds the in-world
> design intent the impl realises. Locks #3 + #5 stay open (deferred — only matter when parchment / a
> multi-label atlas is actually built). Daniel gates the merge.

## The question

The gazetteer now has 162 named regions with borders, biomes, locations, and ore (all on `main`). How
should a player *see* regions on Valheim's in-game world map? Three shapes were floated:
1. a toggleable overlay (borders/tint floating over the normal map),
2. a different "parchment" map view altogether (restyled), or
3. some hybrid.

## The load-bearing fact: Valheim's map is ONE shader-composited quad

Grounded in the decomp (`Minimap`, assembly_valheim ~46894–46923): the world map is **not** a stack of
sprite layers. It's a single `RawImage` (`m_mapImageLarge` / `m_mapImageSmall`) driven by a custom
material that blends three textures:
- `_MainTex` → `m_mapTexture` (terrain color, RGB24)
- `_MaskTex` → `m_forestMaskTexture` (forest overlay)
- `_FogTex`  → `m_fogTexture` (exploration fog-of-war, R8G8)

Pins/labels live in a SEPARATE UI layer: `m_pinRootLarge` (a `RectTransform` parented ABOVE the
RawImage), with `m_pinPrefab` instances. **This split is the whole design decision:**

| Approach | Where it lives | Cost | Touches the shader? |
|---|---|---|---|
| **① Overlay** (lines/tint over vanilla map) | a UI mesh under `m_pinRootLarge` | low | no |
| **② Parchment view** (replace terrain render) | swap `_MainTex` or add a 2nd full RawImage | high | yes |
| **③ Hybrid: styles of ONE overlay** | ① with a style enum | low→med | no (until parchment mode) |

## What's ALREADY built (reuse, don't rebuild)

`src/WorldZones.Mod.RegionOverlay` already ships and is wired:
- `MinimapLabelController` — paints region NAMES on minimap + full map (clones `m_biomeNameLarge`).
- `MinimapUpdateBiomePatch.TryGetHoverWorldPosition` — **already does the full screen→map-UV→world
  transform** (the overlay's hardest math, just inverted: we need world→map-UV to draw borders).
- `IsHoverExplored` — **already gates on fog-of-war**. The fog-respecting path is half-written.
- The csproj compiles clean on this Linux box (no Windows rig; see `regionoverlay-build-and-esp.md`).

So an overlay reuses the projection math + fog gate that already exist. We're extending a working
integration, not starting cold.

## The recommendation: build ① shaped so ② is a SKIN of it

Don't pick "toggle overlay" vs "parchment view" as a fork. Build the overlay with a **style mode enum**
from day one, cycled by one hotkey:

> **vanilla → borders only → borders + translucent tint → full parchment (opaque fills, terrain
> hidden) → back to vanilla**

Then "toggleable overlay" and "parchment map" are the same system at two ends of a dial. This matches
Daniel's reversible/swappable-architecture preference (the start-over-fear antidote): ship the cheap
useful version, see it in-world, then skin toward parchment with the map already proving itself.
Parchment earns its keep as a 4th mode or it doesn't — we don't bet the build on it up front.

The shared substrate both modes need is **region geometry projected into map-UV space** — the borders
from the gazetteer grid, transformed world→map the same way `TryGetHoverWorldPosition` goes map→world.
Build that projection once; every style mode draws from it.

## Resolved locks (Daniel, 2026-06-25) + what stays open

1. **Fog-of-war behavior — ✅ LOCKED: fog-RESPECTING.** Regions appear only as you explore into them.
   Eye-validated 2026-06-23 (`docs/design/mocks/fog_demo.png`): the map-wide reveal is cluttered AND
   spoils the world (all 162 regions visible at spawn); the fog-respecting reveal is clean, native,
   and turns the overlay into a progression *reward*. **Reversible starting default (TWEAK-ME)** — the
   reveal behavior is a setting, not baked. **Implementation correction:** the fog gate is NOT the
   mod's existing `IsHoverExplored` (that's a single-point *hover-label* check on
   `m_biomeNameLarge.text`, not an area mask). The real per-pixel reveal is vanilla
   `Minimap.m_explored[]` via `private bool IsExplored(Vector3)` (`Minimap.cs:1620`) — both private,
   so reached via a cached reflected handle (the `GetRiverWeight` pattern). Full detail +
   `AC-T3-FOG-*` in `region-render-seam.md`.
2. **Style dial + default — ✅ LOCKED.** Ship the full dial `vanilla → borders → borders+tint →
   parchment` on one hotkey; **borders-only as the resting default**. Eye-validated
   (`mocks/overlay_mock.png` — panel 4 parchment is the most readable as pure territory, so it ships
   as a cycle mode, not the default). **Every default reversible via the style enum — Daniel tunes the
   resting default, stroke, tint, palette in-world.** The enum + per-stop draw table is locked in
   `region-render-seam.md` `## Steps 2–3 lock` (`RegionOverlayStyle { Vanilla, Borders, BordersTint,
   Parchment }`).
3. **Parchment hides biome — ⏳ STILL OPEN (deferred).** With terrain removed, parchment loses the
   biome read — a player can't tell a Mountain region from a Meadows one. If/when parchment mode is
   actually built, regions need biome conveyed another way: a small biome glyph per region, OR faint
   biome-tinted paper instead of uniform cream. **Design TBD when we build parchment — out of scope
   for steps 2–3.** (Daniel is colorblind: any such glyph/tint differentiates by lightness + shape +
   label, never hue.)
4. **Border rendering primitive — ✅ LOCKED: BOTH ship.** Resolved (Daniel, 2026-06-25): **mesh ink**
   for the border seams (crisp, restyles live, vector-sharp at any zoom) + **baked texture** for the
   area fills / parchment (one quad, pans + zooms free via `uvRect`, soft drawn-map edge). They do
   different jobs and the style dial is literally the crossover between them. Committing to texture
   fills also drops polygon triangulation + a fill-mesh builder off the critical path (the
   `regionIdGrid` raster the runtime already holds feeds the texture directly). Builders:
   `RegionUiVertexFiller` (ink) + `RegionTextureBaker` (fill), pinned in `region-render-seam.md`.
5. **Names: per-region labels vs centroid labels — ⏳ STILL OPEN (deferred).** The current
   `MinimapLabelController` shows ONE current/hover name; an atlas view wants many. A multi-label mode
   may be needed — but it's not on the steps 2–3 path (the borders draw reuses the existing
   single/hover label untouched). Revisit when a many-labels atlas is actually wanted.

## v2: refined arcs + disc clip + swappable fill backend (LOCKED 2026-06-25)

The borders-only overlay shipped and Daniel walked it in-world (clean reference install, world
SolidHmm, 2026-06-25). It DRAWS — fog-gated, borders on the real map. Two quality gaps remained, in
Daniel's words: **"all blocky"** and **"boundaries stretch into the beyond."** v2 is the lock that
closes both. The in-world intent (the buildable surface is `region-render-seam.md`
`## Region-overlay v2`):

1. **Kill "blocky" — draw the refined arcs, not the 64 m staircase.** The smooth contour-hug geometry
   already exists (`RefineCoastlinesSmoothed` + `RefineBiomeSeams`, proven on real Niflheim, shipped in
   the `{seed}_boundaries.json` dataset) — it was simply never wired into the live draw. The live ink
   path strokes `graph.Segments` (raw 64 m); v2 routes it through the cached refined arcs via the
   already-built `RegionUiVertexFiller.FillPolylines`. The line a player sees becomes the same smooth
   contour the dataset carries — the consistency bar from the border work, now visible.

2. **Kill "stretch into the beyond" — manufacture our own circular clip.** The overlay bleeds past the
   map disc into the black starfield because **the vanilla disc clip is NOT inherited by an injected
   layer.** Decomp-grounded (see the Open Lock answer in `region-render-seam.md`): vanilla paints the
   whole SQUARE map texture and culls pins with a RECTANGULAR bounds test — the circle lives in
   prefab/material asset config, invisible to and uninherited by our mount. JotunnLib hit the exact
   same wall and manufactures its own `CircleMask` + uGUI `Mask`. v2 does the same: one uGUI Mask
   (runtime-generated circle sprite, no asset bundle, no builtin sprite — honouring the mod's
   no-`Knob.psd` doctrine) on the content root, clipping both lines and fill to the disc.

3. **Ship the fill backend as a SWAP (Daniel's reversible-architecture preference).** Two genuinely
   different fill-quality patterns, both shipped, both documented as reference patterns for modders:
   - **Path A (uGUI), ships first:** flat Point-filtered texture fill UNDER the crisp refined arcs.
     The arcs carry the region-shape read; the fill is a flat tint. (`FilterMode.Point` is KEPT, not
     bilinear — bilinear muddies the colourblind lightness palette along every seam.)
   - **Path B (shader composite), follow-on:** region fill folded into a minimap compose pass like
     `_FogTex` — per-pixel region-id sampling gives sharp region edges at screen resolution (the
     shape-accurate fill Path A's texture can't be), with disc + fog + pan/zoom potentially free if it
     injects into the vanilla map material. Lines REUSE Path A's arcs — never re-implemented in-shader.

   The swap is **orthogonal to the style dial**: the existing `RegionOverlayStyle` enum decides WHETHER
   fill draws; a new `RegionFillBackend { UguiTexture, ShaderCompose }` flag decides HOW. The seam is
   the controller's fill branch; the ink branch is backend-independent. This is the reference mod's
   whole job — demonstrate BOTH a uGUI path and a shader path for the same problem.

**Why both, not pick-one:** this mod's deliverable is *demonstrating patterns for modders*. A uGUI
overlay-with-manufactured-mask and a shader-compose-layer are the two canonical ways to add a data
layer to Valheim's map; shipping both behind one swap dial is itself the teaching artifact.

## The mocks (eye-validation, headless)

`docs/design/mocks/overlay_mock.png` — the 4-style dial in Valheim's circular world-disc framing.
`docs/design/mocks/fog_demo.png` — map-wide vs fog-respecting vs parchment+fog (settles lock #1 by eye).

These are MOCKS: the region geometry, borders, and fills are REAL gazetteer data, but the "terrain" is
our raster stylized to evoke the game map — NOT a Steam screenshot (we can't run the licensed client
headless). They validate the *concept + geometry*; the in-world walk validates the *composite*.

## The one true gate (same wall as the ESP)

Everything else we've built was eye-validatable as a headless PNG. **This is not** — an in-world map
overlay must be judged compositing over the *real* map with *real* fog, which needs the licensed Steam
Valheim client running (appid 892970). We can iterate the geometry offline (border explorer + the map
renderer already do), but "does it read right floating over the actual map" is a client-runtime
judgment. Plan for a walk to tune it; don't expect to finalize it from a screenshot.

## Next concrete step (locks answered — now buildable)

Locks #1/#2/#4 are answered, so the next step is no longer a prototype-to-decide; it's the locked
build. **Tier-1 projection is DONE + committed** (`MapProjector`/`MapFrame`, `89926ed`). The
remaining work is build-order steps 2–3, specified to the signature in `region-render-seam.md`
`## Steps 2–3 lock`: build Tier-2 `WorldZones.Unity` (`MapUvProjector` + `RegionUiVertexFiller` ink +
`RegionTextureBaker` fill + `ValheimMapConventions`), then wire the Tier-3 borders-only live draw
under `m_pinRootLarge` into `RegionOverlay` (style enum, borders-only default, fog-gated via the
reflected `IsExplored`). The impl card is `t_db872f11` (`engineer-ui`); QA + the in-world walk
checklist is `t_2dd1dd15` (`qa-playtest`). A cheap offline mock of the projected borders over a
captured map can still front-run Daniel's walk, but it no longer gates the build.
