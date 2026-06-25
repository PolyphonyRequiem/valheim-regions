# The region render seam — geometry + Unity helpers for consumer mods

> **Status: PROVISIONAL — direction in progress (2026-06-24). Daniel gates the lock.**
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
algorithm*, only type adaptation. Planned surface (scenario-shaped, not a cathedral):
- `MapUvProjector` — Unity `Vector2`/`Vector3` wrapper over the Tier-1 projection.
- `RegionInkMeshBuilder` — polylines → border `Mesh` (UI-space or world-space).
- `RegionFillMeshBuilder` — region polygon → translucent tint `Mesh` (the "borders+tint" mode).
- `RegionUiVertexFiller` — populate a `VertexHelper` for a `MaskableGraphic` overlay under a canvas
  (the RawImage-overlay case — vanilla M map, Trailborne disc/modal).
- `RegionTextureBaker` — region-id grid → `Texture2D` (palette / SDF border) for the baked-border /
  parchment style. Pans + zooms for free via `uvRect`; cheap.
- `RegionLineRendererBinder` — polyline + a terrain-height delegate → `LineRenderer` positions
  (the ground-projected ESP).
- `RegionLabelAnchorer` — centroid world→UV → anchored UI position for name labels.
- `ValheimMapConventions` — the Valheim map constants as **DATA, not a game reference**: 64 m/texel
  (`m_pixelSize`), 256² fog texture, the M-map UV formula, world radius / edge wall. So a consumer
  does not re-derive them. **This is the "valheim-modding-informed" knowledge — encoded, not coupled.**
- **This is Daniel's "unity-helpers ... to simplify common valheim-modding-informed rendering
  scenarios."**

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

## The "Valheim-informed vs Valheim-coupled" call (⏳ OPEN — Starbright's lean, Daniel gates)

The genuinely game-coupled convenience — reading `m_pixelSize` off a *live* `Minimap` to build a
projector in one line — could live in a `WorldZones.Valheim` assembly that references
`assembly_valheim`. **Lean: do NOT.** Keep Tier 2 `UnityEngine`-only; let the mod (Tier 3, which
already references the game) do the one-line read and pass constants into Tier 2. Rationale: a
reusable helper lib that hard-references game assemblies must be version-matched to the game and
breaks on 1.0 — a bad trade for a substrate meant to outlive game versions. A thin game-coupled
convenience shim can bolt on later if ergonomics demand it (reversible). **Flag if you'd rather a
`WorldZones.Valheim` one-liner shim up front.**

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
