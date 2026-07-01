# NEXT: unify fill + glow into one water-opacity fill (2026-07-01 walk outcome)

> **Walk verdict (Daniel, on the `worldzones` GABS client, Bilinear build):** interior region
> **seams look great** — fork B (shared-seam fill == ink) + the Point→Bilinear fill-filter fix landed.
> Two remaining complaints, BOTH the same root cause → this spec.

## The two symptoms (screenshots 2026-07-01)
1. **"Blocky coastal bits"** — the *coast glow* layer's outer edge steps (its band is computed on the grid),
   even though the fill coast itself is now smooth.
2. **"Glow looks distinct from the fill, not coastal-aligned"** — a green glow blob sits OUT IN THE WATER,
   detached from the land fill, a different colour, not on the coastline art.

## Root cause (verified in code, not theory)
The coastal glow is a **separate render subsystem** from the fill — `CoastHaloField` + `CoastHaloBaker` +
its own `halo` RawImage layer (RegionOverlayController line ~50), independent of the fill's
`RegionRingFillBaker` + `fill` RawImage. Measured constants:
- `CoastHaloField.DefaultBandMeters = 96.0` → the glow floods **96 m SEAWARD** from the shoreline. That is
  why it "starts out to sea" instead of hugging the coast.
- Glow colour = `BiomeRenderPalette.Glow` (sat-floored ≥0.55), fill = `.Wash` → why glow reads as a
  distinct colour, not the fill.
- Both use `SeaLevel = 30.0` for the land/water cut (they agree there), but the glow is a wide apron layer,
  not an opacity ramp on the fill.

So symptom 1 (blocky) + symptom 2 (detached/misaligned) are the SAME thing: glow is a second, grid-scale,
separately-coloured fill instead of being part of THE fill.

## The fix Daniel specified (his words, 2026-07-01)
> "Treat coastal glow as a specific rendering form of the overall fill problem. It shouldn't be two
> different fills. One fill, just with different opacity once we get into waters. This would include lakes
> or swamp waters as well. If the map shows water texels, we should be showing coastal glow or nothing at
> all. This should ALSO be non-blocky."

**One boundary → one fill → opacity ramp at water.** Concretely:
- KILL `CoastHaloField` / `CoastHaloBaker` / the separate `halo` layer as a distinct subsystem.
- The region fill extends past the 30 m waterline into that region's adjacent water (ocean, lake, AND
  swamp water) at **reducing alpha with depth** — same region colour (the Wash), just fading out.
- Alpha keyed to the SAME height field / waterline the fill already samples, so the glow shoreline is
  EXACTLY the fill coast (no 96 m seaward apron, no separate geometry to misalign).
- Result reads smooth because it's the (Bilinear) fill texture's own alpha channel, not a second grid layer.

This collapses 3 subsystems (fill + halo + ink) toward one source: they can't disagree because there's one.

## Sequencing note — this couples with two still-open items from this session
1. **Wire the INK to the shared seam too.** Fork B wired only the FILL to `SharedSeamSet`; the ink still
   draws the OLD `RegionBoundaryRefiner.RefineBiomeSeams` (plugin line ~447-448). So "fill == ink by
   construction" is only half-true today — they match because same fields, not same geometry. Point the ink
   (`SetWorld` arcs) at the shared seam to truly unify.
2. **Vector membership (`RegionAt`).** Daniel's other walk finding: crossing the ink did NOT trigger region
   discovery, crossing the 64 m ZONE line did → membership still resolves on the coarse grid
   (`RegionWorld.RegionAt` → `Lookup.ResolveCurrent` → `RegionIdGrid`, verified). "What region am I in" must
   use the refined polygon (point-in-refined-ring), not zones. Bigger, touches gameplay/spawns — its own card.

## Dependency order (proposed)
A. **Fill+glow water-opacity unification** (this doc's headline) — render-layer, isolated, most-visible win.
B. **Ink → shared seam** — small, makes fill==ink real not coincidental.
C. **Vector membership** — the deep one; makes "what region am I in" honest, fixes discovery-on-ink.

A and B are cosmetic/safe (off-by-default flags, walk-gated). C is the gameplay-touching one.

## Live-walk harness (set up this session — reuse it)
TWO isolated GABS scenarios now exist (`~/.gabs/config.json`, 2-game split):
- `valheim` → Trailborne client (`~/.local/share/Trailborne/Valheim-Modded`), port 49154, SBPR.Trailborne.
- `worldzones` → wz-refmod client (`~/wz-refmod`), port 49152, WorldZones.RegionOverlay + ValBridgeServer.
Drive the overlay walk via GABS game `worldzones`: `games_start` → `games_connect` → `games_tools` (32
`worldzones.*` tools incl. `capture_screenshot` full-map, `run_command`, `navigate_to_position`). NB: the
game tools register mid-session on GABS but need a Hermes `/reload-mcp` to enter the agent's schema. Deploy
the net472 build to `~/wz-refmod/BepInEx/plugins/WorldZones.RegionOverlay/` (5 DLLs); BepInEx doesn't
hot-reload (quit+relaunch). Keep the two clients' plugin sets DISTINCT (Daniel's call): Trailborne never
gets WorldZones, wz-refmod never gets SBPR.Trailborne.
