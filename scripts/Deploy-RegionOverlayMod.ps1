param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$projectPath = Join-Path $repoRoot 'src/WorldZones.Mod.RegionOverlay/WorldZones.Mod.RegionOverlay.csproj'

$valheimPath = $env:VALHEIM_MODDED_PATH
if ([string]::IsNullOrWhiteSpace($valheimPath)) {
    throw 'VALHEIM_MODDED_PATH is not set. Run scripts/Initialize-ModdedValheimClient.ps1 and see docs/valheim-path-setup.md.'
}

$bepInExCoreDll = Join-Path $valheimPath 'BepInEx/core/BepInEx.dll'
if (!(Test-Path $bepInExCoreDll)) {
    throw "BepInEx is not installed in modded client path: $valheimPath"
}

$bepInExPlugins = Join-Path $valheimPath 'BepInEx/plugins/WorldZones'
if (!(Test-Path $bepInExPlugins)) {
    New-Item -Path $bepInExPlugins -ItemType Directory -Force | Out-Null
}

Write-Host "Building RegionOverlay mod ($Configuration)..."
dotnet build $projectPath -c $Configuration

$outputDir = Join-Path $repoRoot "src/WorldZones.Mod.RegionOverlay/bin/$Configuration/net472"
$dllPath = Join-Path $outputDir 'WorldZones.Mod.RegionOverlay.dll'
if (!(Test-Path $dllPath)) {
    throw "Build output not found at $dllPath"
}

$builtDlls = Get-ChildItem -Path $outputDir -Filter '*.dll' -File
foreach ($builtDll in $builtDlls) {
    Copy-Item $builtDll.FullName (Join-Path $bepInExPlugins $builtDll.Name) -Force
}

Write-Host "Region overlay deployed to $bepInExPlugins"