<#
.SYNOPSIS
    Exports a biome map PNG for a given Valheim world seed.

.PARAMETER Seed
    The world seed string. Default: "HHcLC5acQt"

.PARAMETER Output
    Output PNG file path. Default: "<seed>_biome_map.png" in current directory.

.EXAMPLE
    .\Export-BiomeMap.ps1 -Seed "HHcLC5acQt"
    .\Export-BiomeMap.ps1 -Seed "MySeed" -Output "C:\maps\myworld.png"
#>
param(
    [string]$Seed = "HHcLC5acQt",
    [string]$Output
)

$ErrorActionPreference = "Stop"

# Resolve paths
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectPath = Join-Path $ScriptDir "..\tests\unity"
$ProjectPath = (Resolve-Path $ProjectPath).Path

# Build the WorldGen DLL first
Write-Host "Building WorldZones.WorldGen.dll..." -ForegroundColor Cyan
$csproj = Join-Path $ScriptDir "..\src\WorldZones.WorldGen\WorldZones.WorldGen.csproj"
dotnet build $csproj -c Release --nologo -v q 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Copy DLL to Unity Plugins
$dllSrc = Join-Path $ScriptDir "..\src\WorldZones.WorldGen\bin\Release\net472\WorldZones.WorldGen.dll"
$dllDst = Join-Path $ProjectPath "Assets\Plugins\WorldZones.WorldGen.dll"
Copy-Item $dllSrc $dllDst -Force
Write-Host "DLL copied to Unity Plugins" -ForegroundColor Green

# Find Unity
$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe"
if (-not (Test-Path $UnityPath)) {
    Write-Host "Unity not found at $UnityPath" -ForegroundColor Red
    exit 1
}

# Set output path
if (-not $Output) {
    $Output = Join-Path (Get-Location) "${Seed}_biome_map.png"
}
$Output = [System.IO.Path]::GetFullPath($Output)

$logFile = Join-Path $ProjectPath "biome-export.log"

Write-Host ""
Write-Host "Exporting biome map..." -ForegroundColor Cyan
Write-Host "  Seed:   $Seed"
Write-Host "  Output: $Output"
Write-Host ""

$sw = [System.Diagnostics.Stopwatch]::StartNew()

& $UnityPath `
    -projectPath $ProjectPath `
    -executeMethod BiomeMapExporter.Export `
    -batchmode `
    -logFile $logFile `
    -seed $Seed `
    -output $Output `
    -quit

$sw.Stop()

# Show results from log
if (Test-Path $logFile) {
    Get-Content $logFile | Select-String -Pattern "(=== |Seed:|Range:|Image|WorldGen|Render:|PNG save:|Total:|Output:)" | ForEach-Object {
        Write-Host $_.Line.Trim()
    }
}

if (Test-Path $Output) {
    $size = (Get-Item $Output).Length
    Write-Host ""
    Write-Host "Success! Biome map exported." -ForegroundColor Green
    Write-Host "  File: $Output"
    Write-Host "  Size: $([math]::Round($size / 1MB, 1)) MB"
    Write-Host "  Wall time: $($sw.Elapsed.TotalSeconds.ToString('F1'))s"
} else {
    Write-Host ""
    Write-Host "Export failed. Check log: $logFile" -ForegroundColor Red
    Get-Content $logFile -Tail 20
    exit 1
}
