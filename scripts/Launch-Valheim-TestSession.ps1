param(
    [Parameter(Mandatory = $true)]
    [string]$WorldName,

    [Parameter(Mandatory = $true)]
    [string]$CharacterName
)

$ErrorActionPreference = 'Stop'

$valheimPath = $env:VALHEIM_MODDED_PATH
if ([string]::IsNullOrWhiteSpace($valheimPath)) {
    throw 'VALHEIM_MODDED_PATH is not set. Run scripts/Initialize-ModdedValheimClient.ps1 and see docs/valheim-path-setup.md.'
}

$exePath = Join-Path $valheimPath 'valheim.exe'
if (!(Test-Path $exePath)) {
    throw "Could not find valheim.exe at $exePath"
}

Write-Host "Launching Valheim test session for world '$WorldName' and character '$CharacterName'..."
Write-Host 'Note: automatic profile selection is game/UI dependent and handled manually in-game for now.'

Start-Process -FilePath $exePath -WorkingDirectory $valheimPath -ArgumentList '-console', '-window-mode', 'exclusive'