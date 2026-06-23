# World-map region overlay — design exploration

> **Status:** DESIGN EXPLORATION, opened 2026-06-23. Nothing here is locked. This captures the
> through-line of the "how should regions show on the world map" conversation so the architecture is
> written down before any code. Daniel gates the locks (marked ⏳ OPEN below).

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

## ⏳ OPEN locks (Daniel decides — do NOT build past these)

1. **Fog-of-war behavior.** 🟢 **PROPOSED: fog-respecting** — regions appear only as you explore into
   them. Eye-validated 2026-06-23 (`docs/design/mocks/fog_demo.png`): the map-wide reveal is cluttered
   AND spoils the world (all 162 regions visible at spawn); the fog-respecting reveal is clean, native,
   and turns the overlay into a progression *reward*. The `IsHoverExplored` gate already exists in the
   mod. **← awaiting Daniel's ratify.**
2. **Style dial + default.** 🟢 **PROPOSED:** ship the full dial `vanilla → borders → borders+tint →
   parchment` on one hotkey; **borders-only as the resting default**. Parchment DOES earn its place
   (eye-validated `mocks/overlay_mock.png` panel 4 — the most readable as pure territory) so it ships,
   but as a cycle mode, not the default. **← awaiting Daniel.**
3. **Parchment hides biome (new finding).** With terrain removed, parchment loses the biome read — a
   player can't tell a Mountain region from a Meadows one. If parchment ships, regions need biome
   conveyed another way: a small biome glyph per region, OR faint biome-tinted paper instead of uniform
   cream. **← design TBD when we build parchment mode.**
4. **Border rendering primitive.** UI mesh vs. baked border texture sampled in map-UV space. (Baked is
   cheaper + pans/zooms for free with `uvRect`; mesh is crisper and restyles live. Likely baked fills +
   mesh ink borders — decide when we see it in-world.)
5. **Names: reuse `MinimapLabelController` per-region labels, or draw centroid labels from the overlay?**
   Current controller shows ONE current/hover name; an atlas view wants many. May need a multi-label mode.

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

## Next concrete step (once lock #1 is answered)

Prototype the **world→map-UV projection** + a borders-only draw under `m_pinRootLarge`, fog-gated per
lock #1. Offline-render a mock of how it'd look (over a captured map screenshot) before the in-world
walk, so Daniel eye-checks the geometry cheaply first.
