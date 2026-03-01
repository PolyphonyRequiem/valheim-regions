param(
    [ValidateSet('Vanilla', 'Modded')]
    [string]$Mode = 'Modded'
)

# Valheim Path Verification Script

$defaultVanillaPath = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
$defaultModdedPath = "C:\valheim modding\ValheimModded"

if ($Mode -eq 'Vanilla') {
    $valheimPath = if ($env:VALHEIM_INSTALL_PATH) { $env:VALHEIM_INSTALL_PATH } else { $defaultVanillaPath }
} else {
    $valheimPath = if ($env:VALHEIM_MODDED_PATH) { $env:VALHEIM_MODDED_PATH } else { $defaultModdedPath }
}

Write-Host "Checking $Mode Valheim installation..." -ForegroundColor Cyan
Write-Host "Path: $valheimPath" -ForegroundColor Yellow

if (!(Test-Path $valheimPath)) {
    Write-Host ([char]0x274C) "Valheim not found at: $valheimPath" -ForegroundColor Red
    Write-Host ""
    if ($Mode -eq 'Vanilla') {
        Write-Host "Please set VALHEIM_INSTALL_PATH environment variable:" -ForegroundColor Yellow
        Write-Host "  `$env:VALHEIM_INSTALL_PATH = 'C:\path\to\Valheim'" -ForegroundColor Gray
    } else {
        Write-Host "Run scripts/Initialize-ModdedValheimClient.ps1 or set VALHEIM_MODDED_PATH:" -ForegroundColor Yellow
        Write-Host "  `$env:VALHEIM_MODDED_PATH = 'C:\valheim modding\ValheimModded'" -ForegroundColor Gray
    }
    exit 1
}

$exePath = Join-Path $valheimPath 'valheim.exe'
if (!(Test-Path $exePath)) {
    Write-Host ([char]0x274C) "valheim.exe not found at: $exePath" -ForegroundColor Red
    exit 1
}

$managedPath = Join-Path $valheimPath "valheim_Data\Managed"
if (!(Test-Path $managedPath)) {
    Write-Host ([char]0x274C) "Managed assemblies folder not found at: $managedPath" -ForegroundColor Red
    exit 1
}

Write-Host ([char]0x2705) "Valheim installation found" -ForegroundColor Green
Write-Host ""

# Check key assemblies
$assemblies = @("assembly_utils.dll", "assembly_valheim.dll", "Assembly-CSharp.dll")

foreach ($asm in $assemblies) {
    $asmPath = Join-Path $managedPath $asm
    if (Test-Path $asmPath) {
        $size = (Get-Item $asmPath).Length / 1MB
        Write-Host "  " ([char]0x2705) "$asm ($([math]::Round($size, 2)) MB)" -ForegroundColor Green
    } else {
        Write-Host "  " ([char]0x274C) "$asm (not found)" -ForegroundColor Red
        exit 1
    }
}

if ($Mode -eq 'Modded') {
    $bepInExCore = Join-Path $valheimPath 'BepInEx\core\BepInEx.dll'
    $bepInExPlugins = Join-Path $valheimPath 'BepInEx\plugins'

    if (!(Test-Path $bepInExCore)) {
        Write-Host "" 
        Write-Host ([char]0x274C) "BepInEx core not found at: $bepInExCore" -ForegroundColor Red
        Write-Host "Install BepInEx into the modded client path before deploying plugins." -ForegroundColor Yellow
        exit 1
    }

    if (!(Test-Path $bepInExPlugins)) {
        New-Item -Path $bepInExPlugins -ItemType Directory -Force | Out-Null
    }

    Write-Host "  " ([char]0x2705) "BepInEx core detected" -ForegroundColor Green
}

Write-Host ""
Write-Host "All required assemblies found!" -ForegroundColor Green
exit 0
