# Coast ink misalignment — root cause (2026-07-01, autonomous walk)

> First fully-autonomous in-world diagnosis (load_world → set_map_mode → screenshot, no human clicks).
> Daniel's report: blue coast ink ≠ Valheim's map coastline. Coast-iso 25→30 fix helped the uniform
> offset but the misalignment REMAINS at peninsulas/islets.

## Verified facts (decompiled + deployed-binary checked, NOT guessed)
1. **Coast ink is at 30 m in the running build** — deployed DLL shows `new HeightScalarField(sampler2, 30.0)`
   feeding `RefineCoastlinesSmoothed`. Confirmed the client running is the 30 m build.
2. **Valheim's map uses the SAME height + threshold** — `Minimap.GetMaskColor` tests `height < 30f`, and the
   height is `WorldGenerator.GetBiomeHeight(...)`. My sampler's `GetHeight` IS `GetBiomeHeight` (decomp:
   `WorldGenerator.GetHeight(wx,wy) => GetBiomeHeight(biome,wx,wy,out mask)`). So ink and map agree on WHAT
   contour the coast is. The 25→30 fix was correct; it is NOT an iso mismatch.
3. **The residual error is at PENINSULAS / ISLETS / narrow inlets**, not a uniform inland offset. On open
   coast the blue tracks the shore well (zoom crop confirmed). At a peninsula the blue cuts across the neck,
   leaving green land outside (seaward of) the line.

## Root cause (RegionBoundaryRefiner, verified)
The coast ink is built as: **64 m zone-grid region boundary → local perpendicular march to the 30 m
contour**, where the march is **BOUNDED** (`RefineOptions.MarchRadius` default 40 m ≈ one zone-half,
"bounded so refinement stays local to the boundary cell"). So:
- Start = the coarse 64 m region-boundary staircase (the deterministic gameplay substrate).
- Refine = each sample point marches ≤~40 m perpendicular to snap onto the 30 m isoline.
- FAILURE MODE = any real shoreline feature whose true edge is **>~40 m from the coarse 64 m boundary** is
  unreachable — the march can't get there, so the ink stays on the cut-across line. Peninsulas, small
  islands (the 64 m grid may not resolve them as land at all), and narrow inlets all fall in this gap.

This is the **coarse-substrate + bounded-refine resolution ceiling**, the same class of limit as the fill's
64 m origins. It is independent of the fill/glow unification and the fork-B seam work.

## Fix options (all real work — get Daniel's call)
- **A. Raise MarchRadius** — quick lever, but it's bounded for a reason: a longer march can snap onto the
  WRONG contour (a different nearby shore / inland lake edge), creating new errors. Band-aid.
- **B. Trace the coast off the real land/water raster, decoupled from the 64 m region grid** — sample the
  30 m contour DIRECTLY at fine resolution (marching squares over GetBiomeHeight) and use THAT as the coast
  ink, instead of refining the coarse region boundary. This is the correct fix and it's the SAME "vector
  coast from the height field" primitive that the fill/glow unification wants. Bigger, but it kills the
  whole class of coast-misalignment bugs at once.
- **C. Sub-sample the region grid finer than 64 m near water** so the boundary START is already close to the
  true shore, keeping the march short. Middle-ground; touches the substrate resolution.

## Recommendation
B. The coast (ink AND fill edge AND glow anchor) should ALL come from one fine-resolution trace of the 30 m
height contour — one vector coastline, shared. That single primitive fixes: (1) this peninsula/islet
misalignment, (2) fill-vs-ink coast agreement, (3) the glow's coast anchor (glow-unification doc). It's the
convergence point of the three open render threads. Scope it as its own milestone.

## Tooling milestone (DONE this session)
Autonomous walk harness proven live: `load_world`/`list_saves`/`set_map_mode` ValBridgeServer tools +
main-thread marshaling (MenuManager) — fixed a real off-thread Unity scene-load crash ("Graphics device is
null"). Agent can now: menu → load world → open map → screenshot → inspect, zero human clicks. Source in
~/valheim/mcp-harness/ValBridgeServer (Src/Tools/MenuTools.cs, MapTools.cs, Src/MenuManager.cs).
