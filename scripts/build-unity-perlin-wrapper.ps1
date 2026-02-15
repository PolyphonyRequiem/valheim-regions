# Build Unity PerlinNoise Wrapper DLL
# Compiles a simple wrapper around Unity's Mathf.PerlinNoise

param(
    [string]$UnityPath = ""
)

$ErrorActionPreference = "Stop"

Write-Host "=== Building Unity PerlinNoise Wrapper ===" -ForegroundColor Cyan

# Find Unity installation
if ([string]::IsNullOrEmpty($UnityPath)) {
    Write-Host "Searching for Unity installation..." -ForegroundColor Yellow
    
    $possiblePaths = @(
        "C:\Program Files\Unity\Hub\Editor\*\Editor",
        "C:\Program Files\Unity\Editor",
        "C:\Program Files (x86)\Unity\Editor"
    )
    
    foreach ($pattern in $possiblePaths) {
        $found = Get-Item $pattern -ErrorAction SilentlyContinue | Sort-Object -Descending | Select-Object -First 1
        if ($found) {
            $UnityPath = $found.FullName
            Write-Host "Found Unity at: $UnityPath" -ForegroundColor Green
            break
        }
    }
    
    if ([string]::IsNullOrEmpty($UnityPath)) {
        Write-Error "Unity not found. Please specify -UnityPath parameter."
        exit 1
    }
}

$projectPath = Join-Path $PSScriptRoot "..\unity-perlin-wrapper"
$outputPath = Join-Path $projectPath "Build"

Write-Host "Building with dotnet..." -ForegroundColor Yellow

# Build with dotnet
$env:UnityPath = $UnityPath
Push-Location $projectPath
try {
    dotnet build -c Release 2>&1 | Tee-Object -Variable buildOutput
    $buildSuccess = $LASTEXITCODE -eq 0
} finally {
    Pop-Location
}

if ($buildSuccess -and (Test-Path (Join-Path $outputPath "UnityPerlinNoise.dll"))) {
    Write-Host "`n✓ Build completed successfully!" -ForegroundColor Green
    
    $dllPath = Join-Path $outputPath "UnityPerlinNoise.dll"
    $dllInfo = Get-Item $dllPath
    
    Write-Host "`nDLL Location:" -ForegroundColor Cyan
    Write-Host "  $dllPath" -ForegroundColor White
    Write-Host "  Size: $($dllInfo.Length) bytes" -ForegroundColor Gray
    
    # Copy Unity DLL
    $unityCoreDLL = Join-Path $UnityPath "Data\Managed\UnityEngine\UnityEngine.CoreModule.dll"
    Copy-Item $unityCoreDLL $outputPath -Force
    Write-Host "`nCopied dependency: UnityEngine.CoreModule.dll" -ForegroundColor Gray
    
    Write-Host "`nNext Steps:" -ForegroundColor Cyan
    Write-Host "  1. mkdir lib" -ForegroundColor Gray
    Write-Host "  2. Copy-Item '$outputPath\*.dll' '.\lib\'" -ForegroundColor Gray
    Write-Host "  3. Reference DLLs in WorldZones.WorldGen.csproj" -ForegroundColor Gray
} else {
    Write-Host "`n✗ Build failed!" -ForegroundColor Red
    Write-Host $buildOutput
    exit 1
}
