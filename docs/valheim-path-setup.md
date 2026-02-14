# Valheim Game Path Configuration

This project can optionally reference Valheim game assemblies for utilities and type definitions.

## Setup

Set the `VALHEIM_INSTALL_PATH` environment variable to your Valheim installation:

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

## Verify Setup

Run from repository root:
```powershell
.\scripts\verify-valheim-path.ps1
```

This will check if Valheim assemblies are accessible and report their location.
