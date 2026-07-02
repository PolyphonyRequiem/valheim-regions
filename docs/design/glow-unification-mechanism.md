# Glow unification — design (2026-07-01, from the walk)

> Supersedes the "future work" section of NEXT-unify-fill-glow-water-opacity.md with the CODE-GROUNDED
> mechanism, after reading the fill + halo bakers and re-examining Daniel's screenshot 2.

## The bug, precisely (Daniel's screenshot 2, verified)
Tracing coast→sea in the shot: land fill (full opacity) → coastline → dark water → **then a BRIGHT green
glow blob out in the water, detached from land, brighter than the coastal fill.** A coast glow should be
brightest AT the shore and fade outward. This is inverted/detached.

**Root cause (read in code):** the glow is `CoastHaloField` — a SEPARATE subsystem from the fill, with its
own geometry and its own idea of where to light. Its `depthFadeMeters = 14.0` gate lights water SHALLOWER
than 14 m and cuts deeper water to 0. So a shallow shelf/sandbar OUT in the water stays lit while the
deeper channel between it and the coast goes dark → a bright patch disconnected from land. **The glow lights
"shallow water anywhere," not "the ring of water touching THIS coast."** It genuinely loses the coastline as
the anchor — which is exactly what Daniel challenged ("do you know where the coastline is?"). Answer: the
glow doesn't, by construction.

## Why Daniel's "one fill, opacity ramp" fixes it by construction
The FILL already knows the coast exactly: `fineFillMask` (RegionRingFillBaker/RegionTextureBaker.BakeFine)
carries a region label ONLY on land, clipped to the 30 m waterline; water texels are −1 (transparent). The
fill coast IS the waterline, smooth (Bilinear). If the WATER glow is the fill's own alpha extending from
that same coast, there is ONE coast and they cannot disagree.

## Mechanism (the build)
Replace the separate halo layer with an **alpha ramp on the fill texture in water**:
1. In the fine fill bake, for each WATER texel (label −1) within N metres of a region's coast, paint it that
   nearest region's fill colour at alpha = f(distance-from-coast) OR f(depth) — brightest at the shoreline,
   fading to 0.
   - Anchor = distance from the fill's own land edge (nearest land texel of a region), NOT a depth test. This
     is what keeps it attached to the coast. (Depth MAY additionally clamp it so it doesn't cross a deep
     channel, but distance-from-THIS-coast is the primary key, unlike today's depth-only gate.)
   - Include ALL water (ocean + lakes + swamp water), per Daniel: "if the map shows water texels, we should
     be showing coastal glow or nothing." Lakes/swamp water get the adjacent region's fade too.
2. Kill the separate `halo` RawImage layer + `CoastHaloField`/`CoastHaloBaker` bake in the plugin (or gate
   them off behind the new unified path's flag).
3. One texture, Bilinear → smooth by construction. No 96 m seaward apron; the ramp reach is a small tunable
   (start ~a zone, walk-tune).

## Open decision for Daniel (deferred — he was heading out)
Alpha keyed to: (a) distance-from-coast (fades over N m of water), or (b) depth (fades over M m below sea
level), or (c) min of both (coast-anchored AND depth-clamped so it never crosses a deep channel = the
cleanest, kills the detached-blob bug directly). LEANING (c). Confirm on the next walk.

## Sequencing (this couples with the F-key + ink work)
- The F7 boundary-mode toggle (coast-only / seam-only / all, colour-by-type: BLUE coast / ORANGE seam —
  Daniel is red/green colourblind) is a DIAGNOSTIC that makes this visible: toggle coast boundaries on and
  you can SEE whether the glow's outer edge and the coast ink agree. Build that FIRST (in progress).
- Then this glow unification.
- The 96 m IS the halo band (`CoastHaloField.DefaultBandMeters = 96.0`, "the fade spans this far from the
  shoreline") — Daniel was right; the earlier report muddled it. Killing the separate halo retires the 96 m.

## Guard-rail
Off-by-default flag (UseUnifiedWaterFill or similar), byte-identical to the current fill+halo when off, same
pattern as UseSharedSeamFill. Walk-gated. Compiled ≠ playable.
