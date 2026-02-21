<#
.SYNOPSIS
    Exports a land-component map PNG for a given Valheim world seed.

.PARAMETER Seed
    The world seed string. Default: "HHcLC5acQt"

.PARAMETER Output
    Output PNG file path. Default: "<seed>_land_components.png" in current directory.

.EXAMPLE
    .\Export-LandComponents.ps1 -Seed "HHcLC5acQt"
    .\Export-LandComponents.ps1 -Seed "MySeed" -Output "C:\maps\myworld.png"
#>
param(
    [string]$Seed = "HHcLC5acQt",
    [string]$Output
)

$ErrorActionPreference = "Stop"

# Resolve paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = (Resolve-Path (Join-Path $ScriptDir "..")).Path
$ProjectPath = Join-Path $RepoRoot "tests\unity"

# Build WorldGen DLL
Write-Host "Building WorldZones.WorldGen.dll..." -ForegroundColor Cyan
$worldGenCsproj = Join-Path $RepoRoot "src\WorldZones.WorldGen\WorldZones.WorldGen.csproj"
dotnet build $worldGenCsproj -c Release --nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "WorldGen build failed!" -ForegroundColor Red
    exit 1
}

# Build Regions DLL
Write-Host "Building WorldZones.Regions.dll..." -ForegroundColor Cyan
$regionsCsproj = Join-Path $RepoRoot "src\WorldZones.Regions\WorldZones.Regions.csproj"
dotnet build $regionsCsproj -c Release --nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Regions build failed!" -ForegroundColor Red
    exit 1
}

# Copy DLLs to Unity Plugins
$pluginsDir = Join-Path $ProjectPath "Assets\Plugins"

$worldGenSrc = Join-Path $RepoRoot "src\WorldZones.WorldGen\bin\Release\net472\WorldZones.WorldGen.dll"
$worldGenDst = Join-Path $pluginsDir "WorldZones.WorldGen.dll"
Copy-Item $worldGenSrc $worldGenDst -Force
Write-Host "  WorldZones.WorldGen.dll -> Plugins" -ForegroundColor Green

$regionsSrc = Join-Path $RepoRoot "src\WorldZones.Regions\bin\Release\net472\WorldZones.Regions.dll"
$regionsDst = Join-Path $pluginsDir "WorldZones.Regions.dll"
Copy-Item $regionsSrc $regionsDst -Force
Write-Host "  WorldZones.Regions.dll -> Plugins" -ForegroundColor Green

# Find Unity
$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe"
if (-not (Test-Path $UnityPath)) {
    Write-Host "Unity not found at $UnityPath" -ForegroundColor Red
    exit 1
}

# Set output path
if (-not $Output) {
    $Output = Join-Path (Get-Location) "${Seed}_land_components.png"
}
$Output = [System.IO.Path]::GetFullPath($Output)

$logFile = Join-Path $ProjectPath "land-component-export.log"

Write-Host ""
Write-Host "Exporting land component map..." -ForegroundColor Cyan
Write-Host "  Seed:   $Seed"
Write-Host "  Output: $Output"

& $UnityPath `
    -projectPath $ProjectPath `
    -executeMethod LandComponentExporter.Export `
    -seed $Seed `
    -output $Output `
    -batchmode `
    -nographics `
    -logFile $logFile `
    -quit

$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "Unity exited with code $exitCode" -ForegroundColor Red
    if (Test-Path $logFile) {
        Write-Host "Last 30 lines of log:" -ForegroundColor Yellow
        Get-Content $logFile -Tail 30
    }
    exit $exitCode
}

if (Test-Path $Output) {
    $size = (Get-Item $Output).Length
    Write-Host ""
    Write-Host "Success! Output: $Output ($([math]::Round($size / 1024)) KB)" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Warning: Unity exited OK but output file not found at $Output" -ForegroundColor Yellow
    if (Test-Path $logFile) {
        Write-Host "Last 20 lines of log:" -ForegroundColor Yellow
        Get-Content $logFile -Tail 20
    }
}
