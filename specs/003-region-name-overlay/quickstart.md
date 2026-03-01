# Quickstart: Valheim Region Name Overlay

**Audience**: Developer implementing and validating feature 003  
**Goal**: Build, deploy, and rapidly validate minimap/map/discovery region name behavior using the fixed 500-name catalog.

---

## 1) Prerequisites

- Valheim installed locally.
- BepInEx installed in the Valheim game directory.
- `VALHEIM_INSTALL_PATH` configured for vanilla source install (see [docs/valheim-path-setup.md](../../docs/valheim-path-setup.md)).
- `VALHEIM_MODDED_PATH` configured for dedicated modded client (recommended via `scripts/Initialize-ModdedValheimClient.ps1`).
- .NET SDK available for `dotnet build` / `dotnet test`.

Optional:
- Unity dev console checks (permitted but not required).

---

## 2) Build and Test Core Libraries

From repository root:

```powershell
dotnet test tests/WorldZones.Regions.Tests/WorldZones.Regions.Tests.csproj
dotnet build WorldZones.slnx
```

Expected:
- Deterministic region naming and lookup tests pass.
- Solution build succeeds.
- Name catalog integrity checks pass (exactly 500 literal names, deterministic mapping).

### 2.1) Foundational Dependency-Split Validation

Before mod-specific validation, confirm dependency boundaries are intact:

- `WorldZones.Regions` builds without a project reference to `WorldZones.WorldGen`.
- `WorldZones.Cli` continues to classify zones using `StandaloneWorldDataProvider`.
- `WorldZones.Mod.RegionOverlay` references `WorldZones.Regions` and does not reference `WorldZones.WorldGen`.

---

## 3) Deploy Mod to Valheim Path

Use deployment automation script (to be implemented in this feature):

```powershell
./scripts/Deploy-RegionOverlayMod.ps1 -Configuration Debug
```

Script responsibilities:
- Validate `VALHEIM_MODDED_PATH`.
- Validate BepInEx folder structure exists.
- Build `WorldZones.Mod.RegionOverlay`.
- Copy plugin and required dependencies into `BepInEx/plugins/WorldZones/`.

---

## 4) Launch Rapid Test Session

Use launch automation script (to be implemented in this feature):

```powershell
./scripts/Launch-Valheim-TestSession.ps1 -WorldName <KnownTestWorld> -CharacterName <KnownTestCharacter>
```

Script responsibilities:
- Ensure known test world/character assets are present.
- Start Valheim with fast dev flags (including console where applicable).
- Minimize repetitive manual setup steps between test runs.

---

## 5) Validate Acceptance Behavior In-Game

### A. Minimap Current Region Label
- Enter world with minimap visible.
- Move within one region and verify bottom minimap region name remains stable.
- Cross a region boundary and verify label updates.

Expected results:
- Label text is hidden when minimap is hidden.
- Label text is blank when current position resolves to no region.
- Label text updates to deterministic catalog name when a region is resolved.

### B. Full Map Hover Label
- Open full map.
- Hover explored locations and verify top-left region name updates with cursor position.
- Verify vanilla biome text remains intact.

### C. Discovery Banner
- Enter an undiscovered region and verify one discovery banner appears.
- Leave and re-enter same region; verify banner does not reappear.
- Restart game and re-enter previously discovered region; verify suppression still holds.

### D. Catalog Determinism & Overflow
- Confirm the source catalog contains exactly 500 literal names.
- Validate the same world/region pair always resolves to the same visible name across restarts.
- Validate behavior for region counts beyond 500 remains stable and deterministic (catalog reuse allowed).

---

## 6) Guardrails During Implementation

- Keep Harmony patch count minimal (target two primary patches).
- Put region/domain logic in `WorldZones.Regions`.
- Keep Unity/BepInEx-specific logic only in mod integration project.
- Do not reference `WorldZones.WorldGen` from mod project directly or indirectly.
- Keep CLI behavior on hand-spun worldgen path unchanged.

---

## 7) Deferred Scope Reminder

- Multiplayer authority/synchronization for region resolution and discovery state is deferred and must remain out of implementation scope for this feature.
