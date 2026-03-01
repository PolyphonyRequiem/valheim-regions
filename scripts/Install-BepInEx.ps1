param(
    [string]$ModdedPath,
    [string]$PackageUrl = 'https://gcdn.thunderstore.io/live/repository/packages/denikson-BepInExPack_Valheim-5.4.2333.zip',
    [switch]$ForceReinstall,
    [switch]$StartGameOnce
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ModdedPath)) {
    $ModdedPath = if ($env:VALHEIM_MODDED_PATH) { $env:VALHEIM_MODDED_PATH } else { 'C:\valheim modding\ValheimModded' }
}

if (!(Test-Path $ModdedPath)) {
    throw "Modded Valheim path does not exist: $ModdedPath. Run scripts/Initialize-ModdedValheimClient.ps1 first."
}

$exePath = Join-Path $ModdedPath 'valheim.exe'
if (!(Test-Path $exePath)) {
    throw "valheim.exe not found at $exePath"
}

$bepInExCore = Join-Path $ModdedPath 'BepInEx\core\BepInEx.dll'
if ((Test-Path $bepInExCore) -and -not $ForceReinstall) {
    Write-Host "BepInEx already installed at $ModdedPath" -ForegroundColor Yellow
    exit 0
}

if ($ForceReinstall -and (Test-Path (Join-Path $ModdedPath 'BepInEx'))) {
    Write-Host 'Removing existing BepInEx folder...' -ForegroundColor Yellow
    Remove-Item (Join-Path $ModdedPath 'BepInEx') -Recurse -Force
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('valheim-bepinex-' + [Guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $tempRoot 'bepinex.zip'
$extractPath = Join-Path $tempRoot 'expanded'
New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null

try {
    Write-Host "Downloading BepInEx package from $PackageUrl" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $PackageUrl -OutFile $zipPath

    Write-Host "Extracting BepInEx package" -ForegroundColor Cyan
    Expand-Archive -Path $zipPath -DestinationPath $extractPath -Force

    $packageRoot = $extractPath
    $directCore = Join-Path $extractPath 'BepInEx\core\BepInEx.dll'
    if (!(Test-Path $directCore)) {
        $candidateRoots = Get-ChildItem -Path $extractPath -Directory -Recurse |
            Where-Object { Test-Path (Join-Path $_.FullName 'BepInEx\core\BepInEx.dll') }

        if ($candidateRoots.Count -eq 0) {
            throw 'Could not locate BepInEx package root in extracted archive.'
        }

        $packageRoot = $candidateRoots[0].FullName
    }

    Write-Host "Installing BepInEx files from $packageRoot to $ModdedPath" -ForegroundColor Cyan
    robocopy $packageRoot $ModdedPath /E /R:2 /W:2 /NFL /NDL /NJH /NJS /NC /NS | Out-Null
    $robocopyExit = $LASTEXITCODE
    if ($robocopyExit -gt 7) {
        throw "Robocopy failed with exit code $robocopyExit"
    }

    if (!(Test-Path $bepInExCore)) {
        throw "BepInEx installation did not produce expected file: $bepInExCore"
    }

    Write-Host 'BepInEx installed successfully.' -ForegroundColor Green

    if ($StartGameOnce) {
        Write-Host 'Starting Valheim once so BepInEx can initialize...' -ForegroundColor Cyan
        Start-Process -FilePath $exePath -WorkingDirectory $ModdedPath
    }
}
finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force
    }
}
