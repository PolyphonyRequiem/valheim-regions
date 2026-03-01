# Mod Ideas

A backlog of potential mods or tooling improvements for future consideration.

---

## Auto-Launch Character & World (Dev Tooling)

**Motivation**: The current `Launch-Valheim-TestSession.ps1` script accepts `-WorldName` and `-CharacterName` parameters but Valheim has no CLI args for auto-selecting them. Manual selection in the main menu adds friction to rapid test cycles.

**Approaches to explore**:

1. **Save-file swap**: Before launch, copy a known-good character (`.fch`) and world (`.db` + `.fwl`) into a dedicated `LocalLow` profile slot, launch, then optionally restore after exit.
2. **BepInEx startup patch**: Write a small BepInEx plugin that hooks the main menu `Awake` or the character/world selection screen and programmatically triggers the desired selections via a config file (`BepInEx/config/worldzones.autolaunch.cfg`).
3. **Unity args / deeplink**: Investigate whether Valheim's Unity build exposes any undocumented `-savedatapath` or save directory override flags usable from the command line.

**Preferred approach**: Option 2 (BepInEx startup patch) is the cleanest — keeps game files untouched and is fully self-contained as a dev-only plugin deployed only to the modded client.

**Notes**:
- Config would specify `AutoSelectWorld` and `AutoSelectCharacter` by name.
- Should only activate when a flag like `DevAutoLaunch=true` is set in the config.
- Could be bundled as a separate plugin (`WorldZones.Mod.DevTools`) to keep it out of any release build.
