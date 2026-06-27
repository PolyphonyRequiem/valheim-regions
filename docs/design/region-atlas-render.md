# Region Atlas Render — Locked Model (2026-06-26)

**Status:** LOCKED via offline validation on real Niflheim (`ForTheWort`). This is the
render model Daniel approved across an iterative design review; it ships as a new **gated,
additive** overlay style (`RegionOverlayStyle.Atlas`) alongside the existing
Borders/BordersTint/Parchment stops — those are unchanged. Flip F8 to Atlas to get this look;
flip away to fall back to the proven path. (Start-over-fear antidote: reversible/swappable.)

## The five layers (bottom to top)

1. **Biome-tinted region fill** — each region washed with its `DominantBiome` colour (Valheim
   minimap palette), at **0.28 alpha** so the hillshaded terrain reads *through* the tint.
   Territories read as biome bodies; terrain detail (relief, mountains) survives.
2. **Seaward coast glow** — a fade from the **real `GetHeight` shoreline** (sea level 30 m)
   OUT into the ocean, coloured by the **nearest owned region's biome**, continuous with the
   interior fill (same colour flows land→shore→water, no gap). Three corrections from the walk:
   - **Depth-gated:** glow dies over water deeper than ~14 m below sea level, so it hugs the
     coast and cannot haze the open sea (fixes the Mistlands violet-ocean bleed — 77% of an
     un-gated 140 m band lands on deep ocean).
   - **Saturation floor (HSV S ≥ 0.55):** muted biomes (Mountain S=0.10, Mistlands S=0.27,
     DeepNorth S=0.36) otherwise glow too faintly to read; the floor lifts them without
     touching the punchy ones (Ashlands S=0.78, Plains S=0.64).
   - **Band 96 m** (≈1.5 zones), not 140 m — the long tail was most of the haze.
3. **Terrestrial-only ink** — stroke ONLY interior **land|land** borders (two owned land
   regions meeting on foot). Coast/water seams are NOT inked (the glow carries them). On real
   Niflheim this is 583 of 12,876 seams — a 95% reduction in inked line. Smoothed:
   chain per region-pair → moving-average pre-pass → Chaikin ×5, endpoints pinned at junctions
   so borders stay joined and real corners don't round off.
4. **Zoom-tier centroid labels** — region name (Norse map font) at the centroid, gated so only
   the biggest regions name themselves when zoomed out (offline proxy: top ~40 by land-zone
   count; in-game: gate on `m_largeZoom`). Avoids the 166-name wall.

## The edge-classification insight (why terrestrial-only is cheap)

Every border seam divides two zones with a known `DepthClass` (Land/Shallow/Deep). The
terrestrial-vs-coastal axis is a pure read of those two depths — NOT the existing
`IsCoastline` flag (which means "far side unowned", a *different* axis that lumps ocean with
dry unowned islets). Atlas inks the seam iff **both divided zones are Land AND both sides are
owned regions** (`KeyA != null && KeyB != null`, both land).

## Exact dials (the TWEAK-ME values, validated offline)

| Dial | Value | Notes |
|---|---|---|
| wash alpha | 0.28 | terrain reads through |
| glow band | 96 m | fade reach from shore |
| glow depth-fade | 14 m | below-sea-level depth where glow → 0 |
| glow sat-floor | 0.55 | HSV S minimum for glow colour |
| glow peak alpha | ~0.95 | at the shoreline |
| ink smoothing | pre-avg + Chaikin ×5 | endpoints pinned |
| label tier | top ~40 by land zones | in-game: gate on zoom |

## Biome palette (Valheim minimap colours, from `WorldZones.Cli.Program`)

Ocean (0,0,153) · Meadows (145,167,91) · BlackForest (52,94,59) · Swamp (163,113,87) ·
Mountain (255,255,255) · Plains (199,199,49) · Mistlands (82,82,82) · AshLands (255,0,0) ·
DeepNorth (200,200,255). Atlas uses slightly punchier wash variants so the tint reads as
territory, with the HSV saturation floor applied to the GLOW colour only.

## Known honest residuals (not bugs)

- **Ashlands reads as solid red** — its game minimap colour is pure (255,0,0) and it has no
  relief detail to break up. Honest game colour; mute Atlas's Ashlands wash specifically only
  if Daniel calls it.
- **Mistlands dominates the outer ring** — Niflheim's fringe genuinely is Mistlands-heavy
  (62 of 166 regions). Data, not render. The depth-gate keeps it off the open water.

## Validation provenance

Offline renderer chain (throwaway, `/tmp/wz_composite/`): `WorldZones.Cli composite` +
`basemap` subcommands dump real region grids + per-pixel terrain; Python composites the five
layers on the hillshaded game basemap. Iterated v1→v7 with Daniel; each step eye-verified on
real Niflheim. This doc is the spec that re-lands that model into the live Tier-1/2/3 overlay.
