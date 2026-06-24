# Location gazetteer API — potential vs actual locations

> **Status:** IMPLEMENTED (provisional), 2026-06-24. The location data model + the offline source + the
> Build join + the live source all landed and are green (Runtime 10/10, mod compiles net472). This doc
> captures the design through-line and the OPEN LOCKS Daniel still gates. Naming is provisional until
> locked. Companion: `docs/design/location-port.md` (the offline port + validation verdict).

## The requirement (Daniel, locked)

The gazetteer must (1) build **alongside** regions data in the same pass, (2) be **directly exposed to
modders through an in-process API surface** (not a JSON file they parse), and (3) be **game-runtime
accurate**. This forces a "potential vs actual" mechanism: some locations (the merchant Haldor, the
PlaceOfMystery sites, Hildir) register **N candidate sites** of which exactly **one** ever spawns, and
which one is decided at *runtime by exploration*, not by the seed.

## The two-source architecture (mirrors IWorldSampler)

The faithful-vs-buildable tension resolves by recognising there are two deployment contexts, each with
the right source — exactly the split the terrain sampler already uses:

| Context | `ILocationSource` impl | Faithfulness | Lives in |
|---|---|---|---|
| **In-game** (mod, live session) | `ValheimLiveLocationSource` | EXACT — reads the game's own `ZoneSystem.m_locationInstances` | `WorldZones.Mod.RegionOverlay` (net472, game types) |
| **Offline** (CLI, seed-scoring, headless) | `PortLocationSource` | computed — runs the `LocationModel` port from the seed | `WorldZones.Runtime` (Unity-free) |

The key realisation: **a mod running in-game doesn't recompute locations** — `GenerateLocations` already
ran on the server at world load and populated the public `m_locationInstances`. The live source reads
that. So "runtime-accurate" isn't *reproduced*, it *is* the game's data, zero reproduction error. The
port's imperfection (see location-port.md §5) is an **offline-only** concern that never reaches the
in-game API. Same discipline as `ValheimWorldSampler` / `PortWorldSampler`: the seam + the offline impl
live in the pure runtime; only the plugin touches game types.

## The data model (`WorldZones.Runtime`)

- **`PlacementStatus`** — `Registered` (a normal location; WILL spawn when its zone loads) · `Candidate`
  (one of N sites for a unique; exactly one wins) · `Realized` (confirmed on the ground, `m_placed`;
  **live source only** — offline never emits it).
- **`GazetteerLocation`** — prefab, (x,z), `RegionKey` (the join), `Status`, `CandidateGroupKey`
  (non-null ⇔ unique), `IsUnique`.
- **`CandidateGroup`** — world-scoped set of N candidate sites for ONE unique. `Candidates`,
  `CandidateCount`, `Resolved` (a live source observed the winner), `RealizedSite`,
  `CandidateRegionKeys` ("Haldor could appear in any of these regions"). Lives at `RegionWorld` level,
  not a region, because candidates span many regions and the "exactly one" constraint is global.
- **`ILocationSource`** — `EnumerateLocations() -> IEnumerable<LocationRecord>`. A source knows only
  locations; the Build does the region join. `LocationRecord` carries `IsUnique` + `IsRealized` (live).

## The Build wiring (the "same pass" requirement)

`RegionBuildOptions.LocationSource` is optional. When set, `WorldZonesRuntime.Build`:
1. enumerates the source,
2. binds each location to its region via the existing `RegionLookupService` (`ResolveCurrent(x,z)`),
3. maps status (`IsRealized → Realized`, else `IsUnique → Candidate`, else `Registered`),
4. groups unique candidates into `CandidateGroup`s,
5. attaches per-region slices (`RegionInfo.Locations`) + world-wide views (`RegionWorld.AllLocations`,
   `RegionWorld.CandidateGroups`).

When unset, those collections come back empty — a regions-only build is byte-for-byte unchanged (guarded
by a test). The consumer writes:

```csharp
var catalogue = LocationCatalogue.Load("data/valheim_locations_catalogue.json"); // offline only
var world = WorldZonesRuntime.Build(
    PortWorldSampler.FromSeed("ForTheWort"),
    new RegionBuildOptions { LocationSource = PortLocationSource.FromSeed("ForTheWort", catalogue) });

foreach (var r in world.Regions)
    foreach (var loc in r.Locations)            // POIs in this region, each with PlacementStatus
        ...
foreach (var g in world.CandidateGroups)        // "this unique resolves to 1 of N"
    Log($"{g.PrefabName}: {g.CandidateCount} sites, resolved={g.Resolved}");
```

In-game, the plugin swaps `PortLocationSource` for `ValheimLiveLocationSource` (fed by a closure over
`ZoneSystem.instance.GetLocationList()`), and the same `world.Regions[i].Locations` now carries live
`Realized` status. **The realization overlay** = the difference between the world's *plan* (offline:
Registered/Candidate) and the world's *state* (in-game: + Realized). It only exists when a world is
running to read.

## The unique-collapse subtlety (honest, not a gap)

When a unique resolves in a live world, the game DELETES the losing candidates
(`RemoveUnplacedLocations`). So a live read *after* resolution sees only the winner; the full N-candidate
set is visible only before resolution (offline, a fresh world, or a live session pre-discovery). The
model reflects this truthfully — `CandidateGroup` is a snapshot of world-state-at-build-time. Documented
on `CandidateGroup`.

## Dependency hygiene

The JSON catalogue loader (`LocationCatalogue`) is compiled **net8.0-only** (`#if NET8_0_OR_GREATER`), so
the net472 ship surface a mod consumes carries no `System.Text.Json` package dependency. A live mod never
parses the catalogue anyway — it reads `m_locationInstances`. Offline callers supply configs via the
loader; in-process callers pass an already-parsed config list.

## ⏳ OPEN LOCKS (Daniel gates)

1. **Naming.** `PlacementStatus{Registered, Candidate, Realized}`, `CandidateGroup`, `ILocationSource`,
   `GazetteerLocation`. Provisional — rename freely before this calcifies.
2. **Should `Realized` ever appear OFFLINE?** ✅ **LOCKED: NO** (ratified 2026-06-24). Offline can't know
   realization, so `PortLocationSource` emits only Registered/Candidate, and Realized is a live-only
   enrichment. This is the honest boundary — an offline gazetteer describes the world's PLAN, a live
   session adds the world's STATE.
3. **Live event surface.** ✅ **BUILT** (2026-06-24). `LiveLocationOverlay` (Runtime) holds the built
   `RegionWorld` and exposes `OnLocationRealized` (any location) + `OnUniqueResolved` (a candidate group
   collapses to its winner), driven by `NotifyRealized(prefab, x, z)`. The mod's
   `PlaceLocationsRealizationPatch` (Harmony Postfix on `ZoneSystem.PlaceLocations`) reads the now-placed
   `LocationInstance` and pushes the signal; the plugin forwards it to the overlay. Idempotent (re-notify
   = no-op), fires each event once. Snapshot (`CandidateGroup.Resolved`) still available for consumers
   that don't want the push. Tested Unity-free (5 overlay tests). The plugin hook is wired but the live
   gazetteer activation (assigning a real overlay) is behind the same client-runtime walk wall as the ESP.
4. **Catalogue coverage.** ✅ **CLOSED** (2026-06-24). `parse_locations.py` now walks `_ZoneSystem.prefab`
   + every `LocationList` in `m_sortOrder` order → **178 configs / 145 enabled**, covering 144/147 `.db`
   prefab types (incl. all Mistlands/Ashlands). 6 unique types now (was 1). 3 newest-content locations
   (`BigRockClearing`, `BogWitch_Camp`, `CombatRuin01`) remain outside this export — a small residual.
   The LIVE source has them all regardless (it reads the running game).

## Test coverage

`tests/WorldZones.Runtime.Tests/LocationGazetteerTests.cs` (5, green): regions-only build leaves location
collections empty; locations populate + join + reconcile per-region; StartTemple is Registered near spawn
in a region; Haldor is an unresolved multi-site Candidate group; offline never reports Realized.

## ⚠️ Known ergonomics issue

`PortLocationSource` runs the full ~6,100-location placement each Build; the Runtime test suite takes
~5.5min in Debug (4× location gen). Fine for correctness, slow for iteration. Options if it bites:
cache the computed plan, shrink the test catalogue, or gate the heavy tests behind a trait. Not yet done.
