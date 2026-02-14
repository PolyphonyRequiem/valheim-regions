# Valheim Path Verification Script

$defaultPath = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
$valheimPath = if ($env:VALHEIM_INSTALL_PATH) { $env:VALHEIM_INSTALL_PATH } else { $defaultPath }

Write-Host "Checking Valheim installation..." -ForegroundColor Cyan
Write-Host "Path: $valheimPath" -ForegroundColor Yellow

if (!(Test-Path $valheimPath)) {
    Write-Host ([char]0x274C) "Valheim not found at: $valheimPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please set VALHEIM_INSTALL_PATH environment variable:" -ForegroundColor Yellow
    Write-Host "  `$env:VALHEIM_INSTALL_PATH = 'C:\path\to\Valheim'" -ForegroundColor Gray
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

Write-Host ""
Write-Host ([char]0x1F389) "All required assemblies found!" -ForegroundColor Green
exit 0
