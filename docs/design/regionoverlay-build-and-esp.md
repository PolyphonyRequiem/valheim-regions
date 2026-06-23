# RegionOverlay mod — build & verification findings (2026-06-23)

> **Status:** Findings record. Corrects a stale "needs a Windows client rig" assumption that had been
> blocking the launch plan. The correction: the mod — including the in-world ESP — **compiles clean on
> this Linux box.** What's genuinely client-gated is *runtime* (walking the world), not the build.

## The correction

The roadmap (`docs/ROADMAP-2026-06.md` Part 4) framed the September path as deadlocked: "border
decisions gate on the ESP → the ESP can't build without a client rig → the Linux worker box isn't set
up." The "client rig = Windows" premise was **wrong** — it was a months-old skill note, never
re-checked against the machine.

### What's actually true

`src/WorldZones.Mod.RegionOverlay` references Valheim/Unity/BepInEx assemblies via
`$(ValheimModdedPath)`. The csproj **already reads `$(VALHEIM_MODDED_PATH)` env first**; it only falls
back to the hardcoded `C:\valheim modding\ValheimModded` when that env is unset. With it unset on
Linux, the build resolves *no* game assemblies → **39 `CS0246`/`CS0400` "type not found" errors**.
Every one is a missing-reference error; **zero are code faults** (0 warnings of any other kind).

This box has a full modded-**client** assembly set locally:
- `~/.local/share/Trailborne/Valheim-Modded/valheim_Data/Managed/` — all 9 Unity + game DLLs the
  csproj wants (`UnityEngine.*`, `assembly_valheim.dll`, `Unity.TextMeshPro`).
- BepInEx **core** (`BepInEx.dll`, `0Harmony.dll`) is *not* in that client path, but is under
  `~/valheim/niflheim/data/bepinex/BepInEx/core/`.

### The headless build recipe (verified)

```bash
# assemble a merged reference root (symlink client Managed + a real BepInEx dir)
ROOT=~/.cache/wz-modref
mkdir -p "$ROOT/valheim_Data"
ln -sfn ~/.local/share/Trailborne/Valheim-Modded/valheim_Data/Managed "$ROOT/valheim_Data/Managed"
ln -sfn ~/valheim/niflheim/data/bepinex/BepInEx "$ROOT/BepInEx"

# build the mod against it — no Windows, no csproj edit
VALHEIM_MODDED_PATH="$ROOT" dotnet build src/WorldZones.Mod.RegionOverlay/WorldZones.Mod.RegionOverlay.csproj -c Release
# -> Build succeeded. net472. 0 errors.
```

So **compile is no longer a gate.** The skill `valheim-worldzones-development` has been corrected.

## RegionOverlay vs the ESP — two subsystems, only one wired in

A recurring confusion worth pinning: the mod contains **two distinct subsystems**.

| | **RegionOverlay** (the minimap labels) | **RegionBorderEsp** (the ESP) |
|---|---|---|
| Job | paints region **names** on the minimap + discovery banner; persists discovery | draws region **borders** as ground-projected world-space lines you walk up to |
| Wired in? | ✅ yes — `RegionOverlayPlugin.Awake()` patches `Minimap`, builds `MinimapLabelController` | ❌ **no** — `RegionBorderEsp.cs` is referenced by nothing; it's a `MonoBehaviour` the plugin never instantiates |
| Verb | *read a label* | *judge a seam where you stand* |

"Is the region overlay the ESP?" — **No.** The overlay ships names-on-minimap; the ESP (lines-on-the-
ground) is unwired scaffold. The ESP is the instrument for the four border decisions (you can't judge
a border from a top-down PNG), so wiring it in is a prerequisite for the border backtrack — see
`region-borders.md` "What's still gated."

## What remains genuinely gated (and on what)

| Step | Gate |
|---|---|
| Build the mod incl. ESP | **none** — compiles on Linux (above) |
| Offline ESP render (decision bench) | **none** — `tools/borders-explorer/offline_esp.py` |
| Region gazetteer | **none** — shipped (PR #8) |
| **Walk the world to eye-judge borders / per-biome weights** | a real, **licensed Valheim client** (Steam DRM, appid 892970). This box has only headless *servers* + 20-byte stub client binaries. This is the one true wall — a license wall, not a rig wall. |

The ESP's GEOMETRY is already proven offline (Python mirror: segments on real borders). What the walk
adds is the felt judgment — does the 64m staircase read as wrong, what are the per-biome weights —
which is inherently a runtime-on-a-client question.

## Takeaway

The launch plan's "ESP deadlock" was half myth. Compile + offline iteration are fully unblocked and
done headless. The only thing needing Daniel's hardware is the in-world walk, and that's a Steam
client install, not a Windows build rig.
