param(
    [string]$SourcePath,
    [string]$ModdedPath = 'C:\valheim modding\ValheimModded',
    [switch]$ForceRefresh,
    [switch]$SetUserEnvironmentVariable
)

$ErrorActionPreference = 'Stop'

$defaultVanillaPath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = if ($env:VALHEIM_INSTALL_PATH) { $env:VALHEIM_INSTALL_PATH } else { $defaultVanillaPath }
}

$sourceResolved = (Resolve-Path $SourcePath).Path
$moddedResolved = $ModdedPath

$sourceExePath = Join-Path $sourceResolved 'valheim.exe'
if (!(Test-Path $sourceExePath)) {
    throw "Source path does not look like a Valheim install: $sourceResolved"
}

if ($ForceRefresh -and (Test-Path $moddedResolved)) {
    Write-Host "Removing existing modded client at $moddedResolved" -ForegroundColor Yellow
    Remove-Item -Path $moddedResolved -Recurse -Force
}

if (!(Test-Path $moddedResolved)) {
    New-Item -Path $moddedResolved -ItemType Directory -Force | Out-Null
}

$targetExePath = Join-Path $moddedResolved 'valheim.exe'
if (!(Test-Path $targetExePath)) {
    Write-Host "Copying Valheim from '$sourceResolved' to '$moddedResolved'..." -ForegroundColor Cyan
    robocopy $sourceResolved $moddedResolved /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NC /NS | Out-Null
    $robocopyExit = $LASTEXITCODE
    if ($robocopyExit -gt 7) {
        throw "Robocopy failed with exit code $robocopyExit"
    }
} else {
    Write-Host "Modded client already exists at '$moddedResolved'. Skipping copy." -ForegroundColor Yellow
}

$env:VALHEIM_MODDED_PATH = $moddedResolved

if ($SetUserEnvironmentVariable) {
    [System.Environment]::SetEnvironmentVariable('VALHEIM_MODDED_PATH', $moddedResolved, 'User')
    Write-Host "Set user environment variable VALHEIM_MODDED_PATH=$moddedResolved" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Next steps:' -ForegroundColor Cyan
Write-Host '  1) Install BepInEx into the modded path if not already installed.'
Write-Host '  2) Run .\scripts\verify-valheim-path.ps1 -Mode Modded'
Write-Host '  3) Run .\scripts\Deploy-RegionOverlayMod.ps1'
