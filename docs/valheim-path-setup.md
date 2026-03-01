# Valheim Game Path Configuration

This project uses two Valheim paths:
- `VALHEIM_INSTALL_PATH`: vanilla Steam install (read-only source copy)
- `VALHEIM_MODDED_PATH`: dedicated modded client used for BepInEx and plugin deployment

## Setup

Set the `VALHEIM_INSTALL_PATH` environment variable to your vanilla Steam installation:

**Windows (PowerShell):**
```powershell
$env:VALHEIM_INSTALL_PATH = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

**Or permanently:**
```powershell
[System.Environment]::SetEnvironmentVariable('VALHEIM_INSTALL_PATH', 'C:\Program Files (x86)\Steam\steamapps\common\Valheim', 'User')
```

**Linux/Mac:**
```bash
export VALHEIM_INSTALL_PATH="/path/to/Valheim"
```

## Default Path

If not set, defaults to: `C:\Program Files (x86)\Steam\steamapps\common\Valheim`

## Create a Dedicated Modded Client Copy

Run from repository root:

```powershell
.\scripts\Initialize-ModdedValheimClient.ps1 -SetUserEnvironmentVariable
```

Defaults:
- Source: `VALHEIM_INSTALL_PATH` (or Steam default path)
- Target modded client: `C:\valheim modding\ValheimModded`

After this, install BepInEx into the modded client folder.

### Install BepInEx into Modded Client

Run from repository root:

```powershell
.\scripts\Install-BepInEx.ps1
```

Optional reinstall:

```powershell
.\scripts\Install-BepInEx.ps1 -ForceReinstall
```

Optional full refresh:

```powershell
.\scripts\Initialize-ModdedValheimClient.ps1 -ForceRefresh -SetUserEnvironmentVariable
```

## Verify Setup

Run from repository root:
```powershell
.\scripts\verify-valheim-path.ps1 -Mode Vanilla
.\scripts\verify-valheim-path.ps1 -Mode Modded
```

`-Mode Modded` additionally verifies BepInEx core is present in the modded client.
