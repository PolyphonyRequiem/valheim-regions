# Handoff — region min-size merge bug + swamp-floor A/B

**Created:** 2026-06-29 (end of a long SBPR regions session). **Owner:** next engineering session with Daniel.
**Status:** two threads parked here, both grounded, neither finished. Read this before touching region
determination or the swamp floor again.

---

## Session context (what shipped tonight, so you don't redo it)

The session drove from a gold-glow nag → a full **authoritative-ring fill** for the worldmap overlay.
Shipped + deployed + walk-confirmed by Daniel:
- Authoritative refined `RegionRing` (watertight guards, regression test) — `RegionRingRefiner`,
  `RefinedRegionBoundary`.
- Fill clips to the 30 m waterline; **fill XOR fade partition** (no double-layer, lakes fade) — `RegionFillMaskBaker`, `CoastHaloField.Build(includeLakes/swampLandFloor/isSwamp)`, `CoastHaloBaker.BakeBiome(fillMaskOrNull)`.
- **Ring fill** — fill rasterizes from the refined ring, not the 64 m zone grid, so the coast edge is
  smooth — `RegionRingFillBaker`. Live on Prime.

Then we chased the **"grey gap"** (unincorporated land showing as holes) and that opened the two threads below.

**Reverted tonight (do NOT resurrect without re-reading why):** a *shallow-bridge growth pass* that let
regions claim islands across shallow water. It broke the `EveryRegion_HasExactlyOneOuterRing` invariant
(a bridged island = a second disconnected outer ring), which the entire ring model depends on. If you
ever want non-contiguous regions you must first decide to drop that invariant and update the ring model +
test. We chose not to. Git: it was committed then reverted in the same session; check `git log` around
the `ProtoRegionGenerator` changes if you need the diff.

---

## THREAD 1 — the region min-size MERGE BUG (the real task) — ✅ RESOLVED 2026-06-30

### RESOLUTION (2026-06-30): there was NO merge bug. Fixed via the SEED-ELIGIBILITY floor, not the merge.
Measured on Astley with a read-only probe (`WorldZones.Cli mergebug`, kept as the validation record):
- **Param flow works.** Built at floor 6 / 25 / 130: 6→25 identical (182 regions, merged=0), but
  floor=130 → 164 regions, **merged=18**. The option reaches the merge and the merge fires when
  handed mergeable input. Hyp #1 (param-not-flowing) is dead.
- **Hyp #4 CONFIRMED — the "12 mergeable runts" were a diagnostic artifact.** For every sub-25
  region, reconstructed the merge's land-only 4-neighbour adjacency from the grid and cross-tabbed
  against `NeighborKeys`. Result: **all 27 survivors have landAdj==0, ZERO have a real land
  neighbour.** The 12 that showed `NeighborKeys≥1` were counting a POST-FRINGE shallow-water phantom
  the land-only merge never saw (`GazetteerBuilder`'s neighbour scan runs post-`ExpandRegionsInto
  AdjacentShallowZones` and is NOT land-gated; `MergeTinyRegions` runs pre-fringe, land-only).
  `MergeTinyRegions` is **correct**. Moreover every survivor is its OWN whole land component
  (comp==land, cReg==1) → nothing to merge into by construction.

### THE FIX (Daniel LOCKED design A, 2026-06-30): raise `MinComponentZonesForProto` 12 → 25.
The real lever for tiny-island runts is the SEED-ELIGIBILITY floor (a land component below it never
earns a region seed → demotes to an unincorporated MinorIslet), NOT the merge floor.
- Plumbed `RegionBuildOptions.MinComponentZonesForProto` (was hardcoded
  `ProtoRegionGenerator.DefaultMinComponentZonesForProto=12`) → `WorldZonesRuntime.Build` →
  `GenerateLand(minComponentZonesForProto:)`. **Default set to 25.**
- `MinRegionZones` stays **6** — orthogonal knob, handles the OTHER runt case (a sub-split of a big
  component that DOES share a land border). Both floors documented as the two distinct runt cases.
- Intent set by the `compfloor` sweep (measure-distribution-then-pick, same method as the swamp
  floor): 25 is the only floor taking runt regions to exactly 0 on Astley. Cost: 27 components
  demote, world-wide unincorporated land 4.52% → 6.24% (~+1.7 pts). Daniel will catch the grey on
  the walk. The 12–24 zone band had no natural gap, so 25 is an intent line, not a cliff.
- **Verification:** full suite green **186/186** (58 Runtime + 79 Regions + 49 WorldGen). The
  `GazetteerCompositionTests` golden-value lock passed UNCHANGED (spawn region r.7.7 is 445 zones,
  far above the floor; dropping smallest-area tail components doesn't perturb the large components'
  RNG draws — no re-pin needed). Gazetteer at the new default: 155 regions, 860 islets (matches the
  sweep's floor-25 row exactly).

### Original investigation notes (kept for context)
### What Daniel asked for
"We should have a minimum region size in our configuration. Maybe 25 zones?" — triggered by the worldmap
showing **"the Eldkyst Marshlands" (r.-55.-21 on seed Astley)**: a **17-zone / 0.07 km²** swamp region,
**rank 171/182** by area (median region = 113 zones). It's too small to read as a real place.

### What's DONE
- `RegionBuildOptions.MinRegionZones` is now a **real config option** (was hardcoded
  `ProtoRegionGenerator.DefaultMinRegionZones = 6`). Plumbed through `WorldZonesRuntime.Build` →
  `GenerateLand(minRegionZones: options.MinRegionZones)`. **Default left at 6** on purpose (see below).

### The BUG you must fix (this is the actual work)
Raising `MinRegionZones` 6 → 25 on Astley changed **NOTHING**: still 182 regions, still 27 regions under
25 zones, still a 12-zone minimum. Measured breakdown of those 27 sub-25-zone regions:
- **15 are isolated islands (0 neighbours)** — `MergeTinyRegions` *correctly* can't merge them (line
  ~549 `if (borderCounts.Count == 0) continue; // isolated, no neighbor`). These are the "deep-water
  island" case; whether they should be regions at all is a SEPARATE design question (Daniel's standing
  call elsewhere: deep-separated islands stay unincorporated). Not a merge bug.
- **12 have ≥1 neighbour and SHOULD merge at floor 25 but SURVIVE** ← **THE BUG.** Example:
  `r.32.-7 "the Hold of Ragnar"`, 13 zones, 1 neighbour, Swamp. With `minRegionZones=25` it should be
  absorbed into its neighbour and isn't.

### Where to look
`src/WorldZones.Regions/ProtoRegionGenerator.cs`:
- Merge gate: line ~273 `if (minRegionZones > 0 && seeds.Count > 1)` → `MergeTinyRegions(...)`.
- `MergeTinyRegions` (line ~461): iterative, recomputes areas, merges tiny→longest-border-neighbour,
  tie-break lower id. The re-check at line ~516 `if (currentArea >= minRegionZones || currentArea == 0) continue;`.

### Hypotheses to test (not yet checked — START HERE)
1. **Param flow:** confirm `minRegionZones=25` actually reaches `MergeTinyRegions` at runtime (add a temp
   log of `minRegionZones` + `mergedCount`). The 6-vs-25 result being byte-identical is suspicious — it
   may indicate the merge isn't seeing 25 at all, OR that on this seed nothing the merge *can* touch is
   between 6 and 25 (unlikely given the 12 mergeable runts).
2. **`MinorIslet` partition (step 1, line ~112):** components below `DefaultMinComponentZonesForProto`
   never get a proto-seed at all — they become `MinorIslet`s and may be re-attached separately, bypassing
   `MergeTinyRegions`. The 12 "have a neighbour but survive" regions may be islets re-surfaced as regions
   downstream, never eligible for the merge. CHECK whether the 12 are `MinorIslet`-derived.
3. **Identity/seed survival:** `MergeTinyRegions` works on `regionIdGrid` + `seeds`; a region whose seed
   was consumed but whose id persists in the grid could dodge the area re-check. Verify the 12 are in the
   `areas` dict the merge iterates.
4. **`NeighborKeys` vs land-adjacency mismatch:** `RegionInfo.NeighborKeys` (what we measured "has a
   neighbour" with) is computed LATER and may count neighbours across water/diagonals that
   `MergeTinyRegions`'s 4-neighbour `borderCounts` does NOT — so a region can have `NeighborKeys.Count≥1`
   yet `borderCounts.Count==0` in the merge. **This is the most likely explanation** — verify by logging
   `borderCounts.Count` for the 12 inside the merge. If true, the "bug" is that our diagnostic's
   neighbour definition ≠ the merge's, and the real fix is either (a) accept these as legit isolated-ish
   regions, or (b) widen the merge's adjacency to match (8-neighbour / shallow-bridge — but mind the
   contiguity invariant from the reverted thread!).

### Repro / measurement tools (already built, throwaway CLI in WorldZones.Cli)
All on `seed Astley` unless noted. Build: `dotnet build src/WorldZones.Cli/...csproj -c Release -p:TargetFramework=net8.0` with `VALHEIM_MODDED_PATH="$HOME/.cache/wz-modref"`.
- `dotnet WorldZones.Cli.dll unincland -seed Astley` — world-wide unincorporated-land %, shallow vs deep split.
- `greygapviz -seed Astley -output /tmp/x` — renders a region window, magenta = unincorporated land.
- `rivergap -seed Astley` — classifies unincorporated land by nearest-water type (river/ocean/lake).
- Ad-hoc region-stats programs were written under `/tmp/howecheck*`, `/tmp/tinycheck`, `/tmp/minzone`
  (throwaway, may be gone — re-create from the pattern: build a tiny console proj referencing
  `WorldZones.Runtime`, call `WorldZonesRuntime.Build(PortWorldSampler.FromSeed("Astley"), opts)`).

### Acceptance for THREAD 1
The 12 neighbour-having sub-`MinRegionZones` regions either (a) get merged away when the floor is raised,
or (b) are proven to be legitimately isolated (the diagnostic's neighbour count was wrong) and we accept
them. THEN decide Daniel's actual floor value (he proposed 25) and set the default. Do NOT ship a 25
default that silently does nothing.

---

## THREAD 2 — swamp-floor — ✅ RESOLVED + LOCKED at 27.5 (2026-06-30)

### RESOLUTION (2026-06-30): locked at 27.5, NOT 28.5. The A/B killed 28.5.
The gate was a same-window 22-vs-floor A/B on swamp-heavy NON-runt regions (the `swampab` CLI probe,
seed Astley). The decisive addition over the 2026-06-29 attempt was an HONEST coastal-vs-interior
split: flood from open water through the shed band — a shed cell the flood REACHES is the shoreline
retreating inward (coastal/good), a shed cell walled off by surviving land is an interior hole in the
bog (bad). At **28.5** the shed punched interior holes: Kjellvik 89% interior, Marshlands 36%,
Nordreach 29%, Galdhavn 11% — water pockets inside the swamp body, exactly what Daniel's eye caught
("they look interior often"). The holes were almost entirely terrain in the razor-thin **[27.5, 28.5)**
band, so dropping the floor 1 m to **27.5** rescues 94–100% of them and every region reads CLEAN
COASTAL TRIM (interior 0–5%; Kjellvik sheds nothing). Daniel locked 27.5 after seeing the A/B.

🔴 The 2026-06-29 "99.6% / 100% COASTAL @28.5" claim was WRONG — it came from a broken proximity
metric (counted any shed cell within 128 m of *any* water as coastal, including the bog being shed).
In a wet swamp that reports ~100% by construction. Superseded by the flood-based split. **Do not
resurrect proximity-to-any-water as a coastal test.**

### THE LOCK — all 3 load-bearing sites moved together (they MUST, or fill/fade/membership disagree)
- `RegionBuildOptions.SwampLandFloorMeters` default → **27.5f**.
- `RegionOverlayPlugin.cs` fade-field `swampLandFloor:` arg → **27.5** (the build inherits the option
  default for membership+fill; only the fade field hardcodes it).
- `GazetteerCompositionTests.SwampFloor` golden const → **27.5f**.
- Suite green **186/186**. Golden `OriginRegion_HasExpectedCorrectedValues` passed UNCHANGED — the
  floor is swamp-gated and spawn r.7.7 is Meadows (no swamp), so it doesn't move. net472 mod build
  clean. Committed; net472 DLLs built and ready to deploy to Prime (was NOT yet deployed at lock time).

### Original investigation notes (kept for context) — these describe the SUPERSEDED 28.5 path
### What's DONE (committed, green, NOT deployed to Prime)
`RegionBuildOptions.SwampLandFloorMeters` raised **22 → 28.5**, and the plugin's fade `swampLandFloor`
arg matched to 28.5 (`RegionOverlayPlugin.cs`). Full runtime suite green (58/58) — the golden-value test
`GazetteerCompositionTests.OriginRegion_HasExpectedCorrectedValues` was re-pinned to the VERIFIED new
origin region (`r.7.7 "Greater Eldkyst"`, Meadows, 445 land zones, Meadows frac ~0.438) on seed
ForTheWort. NOTE: that test uses `IncludeInlandWater=true` ONLY (no feature-aware borders) — match its
options exactly when re-grounding.

### Why it's the right value (data-driven, seed Astley)
Swamp terrain sits in a tight **24–33 m** band (min 24.4, max 33.4, mean 29.6, peak 29 m). Floor 28.5 =
~1.5 m below the 30 m waterline → rescues near-surface walkable swamp, lets deeper bog read as water.
**Critically (Daniel's hypothesis, CONFIRMED): the swamp zones that flip Land→water at 28.5 vs 22 are
99.6% COASTAL** (232 of 233 within 128 m of ocean), ~0% inland body. So 28.5 cleans wet swamp *coasts*
without shrinking inland swamp. Tool: `swampheight -seed Astley`, `swampfloordelta -seed Astley`.

### Why it's NOT deployed
The `greygapviz` render at 28.5 was **ambiguous** — still showed heavy magenta speckle — BUT we later
discovered the rendered region was the 17-zone runt (Thread 1), so the window was a magnified worst-case
outlier, not representative. We never produced a clean **22-vs-28.5 side-by-side on an identical window of
a MEDIAN-sized region**. That A/B is the gate.

### Acceptance for THREAD 2
Render 22-vs-28.5 side-by-side on the SAME window of a **median (~113-zone) swamp region** on Astley
(NOT the runt). If 28.5 visibly cleans the coast without eating inland swamp, deploy (build mod net472,
scp 5 DLLs to `prime-u:~/wz-refmod/BepInEx/plugins/WorldZones.RegionOverlay/`, hash-verify, confirm a
real string literal is present, gate on `pgrep valheim.x86_64` — BepInEx does NOT hot-reload). If not,
revert the floor to 22 (and re-pin the golden test back).

---

## Deploy/verify reminders (both threads, when shipping to Prime)
- Build mod: `dotnet build src/WorldZones.Mod.RegionOverlay/...csproj -c Release -p:TargetFramework=net472`.
- Gate: `ssh prime-u 'pgrep -f valheim.x86_64'` — if RUNNING, the deploy is STAGED not live (BepInEx holds
  old DLLs in memory; needs full quit + relaunch via `~/wz-refmod/walk.sh` → world Astley → M for map).
- Verify: all 5 `WorldZones.*.dll` hashes match local↔remote AND a real UTF-16 string literal from the
  change is present in the deployed dll (`strings -a -e l <dll> | grep -c '<marker>'`). Hash proves
  transfer, the literal proves it's the right CODE. Don't claim "deployed" on hash alone.
- The session ran on seed **Astley** (Daniel's active world), spawn-region golden test on **ForTheWort**.
