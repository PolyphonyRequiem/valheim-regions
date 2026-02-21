# Sync WorldZones.WorldGen source files to Unity test project
# Run this after making changes to src files

$srcDir = "C:\Users\dangreen\projects\valheim\worldzones\src\WorldZones.WorldGen"
$destDir = "C:\Users\dangreen\projects\valheim\worldzones\tests\unity\Assets\WorldZones.WorldGen"

# Files needed by Unity tests (excluding old WorldGenerator.cs that uses lookup table)
$files = @(
    "BiomeType.cs",
    "MathUtils.cs",
    "UnityRandom.cs",
    "UnitySeedOffsets.cs",
    "WorldGeneratorCore.cs",
    "StringExtensions.cs"
)

Write-Host "Syncing source files to Unity test project..." -ForegroundColor Cyan

foreach ($file in $files) {
    $src = Join-Path $srcDir $file
    if (Test-Path $src) {
        Copy-Item $src -Destination $destDir -Force
        Write-Host "  ✓ $file" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $file (not found)" -ForegroundColor Red
    }
}

Write-Host "`nSync complete!" -ForegroundColor Cyan
