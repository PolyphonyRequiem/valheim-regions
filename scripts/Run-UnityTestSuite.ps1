# Run Unity Test Suite (tests/unity)
# Builds WorldZones.WorldGen DLL and runs tests in headless Unity runtime

param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe",
    [string]$ProjectPath,
    [string]$TestFilter = "",
    [switch]$ShowResults
)

$ErrorActionPreference = "Stop"

# Default to tests/unity if not specified
if (-not $ProjectPath) {
    $ProjectPath = Join-Path $PSScriptRoot "..\tests\unity"
}

Write-Host "================================" -ForegroundColor Cyan
Write-Host "Unity Test Suite Runner" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the WorldZones.WorldGen DLL
Write-Host "Step 1: Building WorldZones.WorldGen.dll..." -ForegroundColor Yellow
$libProjectPath = Join-Path $PSScriptRoot "..\src\WorldZones.WorldGen"
Push-Location $libProjectPath
try {
    $buildOutput = dotnet build -c Release 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Failed to build WorldZones.WorldGen" -ForegroundColor Red
        Write-Host $buildOutput
        exit 1
    }
    Write-Host "✓ DLL built successfully" -ForegroundColor Green
} finally {
    Pop-Location
}

# Step 2: Copy DLL to Unity Plugins folder
Write-Host "Step 2: Copying DLL to Unity project..." -ForegroundColor Yellow
$dllPath = Join-Path $libProjectPath "bin\Release\net472\WorldZones.WorldGen.dll"
$pluginsDir = Join-Path $ProjectPath "Assets\Plugins"

if (-not (Test-Path $dllPath)) {
    Write-Host "ERROR: DLL not found at: $dllPath" -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
Copy-Item $dllPath -Destination $pluginsDir -Force
Write-Host "✓ DLL copied to Unity Plugins" -ForegroundColor Green
Write-Host ""

# Paths
$resultsFile = Join-Path $ProjectPath "TestResults.xml"
$logFile = Join-Path $ProjectPath "test-run.log"

# Clean old results
if (Test-Path $resultsFile) { Remove-Item $resultsFile }
if (Test-Path $logFile) { Remove-Item $logFile }

Write-Host "Unity Path: $UnityPath"
Write-Host "Project: $ProjectPath"
Write-Host "Results: $resultsFile"
Write-Host ""

# Verify Unity exists
if (-not (Test-Path $UnityPath)) {
    Write-Host "ERROR: Unity not found at: $UnityPath" -ForegroundColor Red
    exit 1
}

# Verify project exists
if (-not (Test-Path $ProjectPath)) {
    Write-Host "ERROR: Unity project not found at: $ProjectPath" -ForegroundColor Red
    exit 1
}

# Build test command
$testArgs = @(
    "-runTests"
    "-batchmode"
    "-nographics"
    "-projectPath", $ProjectPath
    "-testPlatform", "PlayMode"
    "-testResults", $resultsFile
    "-logFile", $logFile
)

if ($TestFilter) {
    $testArgs += "-testFilter", $TestFilter
}

Write-Host "Step 3: Running Unity tests in headless mode..." -ForegroundColor Yellow
Write-Host "This will take 30-90 seconds..." -ForegroundColor Gray
Write-Host ""

$startTime = Get-Date

try {
    $process = Start-Process -FilePath $UnityPath -ArgumentList $testArgs -Wait -PassThru -NoNewWindow
    $exitCode = $process.ExitCode
}
catch {
    Write-Host "ERROR: Failed to run Unity tests" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

$duration = ((Get-Date) - $startTime).TotalSeconds

Write-Host ""
Write-Host "Unity test run completed in $([math]::Round($duration, 1))s" -ForegroundColor Gray
Write-Host ""

# Parse results
if (Test-Path $resultsFile) {
    [xml]$results = Get-Content $resultsFile
    $testRun = $results.'test-run'
    
    $total = [int]$testRun.total
    $passed = [int]$testRun.passed
    $failed = [int]$testRun.failed
    $skipped = [int]$testRun.skipped
    
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "Test Results" -ForegroundColor Cyan
    Write-Host "================================" -ForegroundColor Cyan
    Write-Host "Total:   $total"
    Write-Host "Passed:  $passed" -ForegroundColor Green
    Write-Host "Failed:  $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Gray" })
    Write-Host "Skipped: $skipped" -ForegroundColor Gray
    Write-Host ""
    
    if ($failed -gt 0) {
        Write-Host "FAILED TESTS:" -ForegroundColor Red
        $testRun.'test-suite'.'test-suite'.'test-case' | Where-Object { $_.result -eq "Failed" } | ForEach-Object {
            Write-Host "  × $($_.name)" -ForegroundColor Red
            if ($_.failure) {
                Write-Host "    $($_.failure.message)" -ForegroundColor Gray
            }
        }
        Write-Host ""
    }
    
    if ($ShowResults -and (Test-Path $logFile)) {
        Write-Host "Unity Log Output:" -ForegroundColor Yellow
        Write-Host "================================" -ForegroundColor Gray
        Get-Content $logFile | Select-Object -Last 50
        Write-Host ""
    }
    
    # Exit with appropriate code
    if ($failed -gt 0) {
        Write-Host "❌ Tests FAILED" -ForegroundColor Red
        exit 1
    }
    else {
        Write-Host "✅ All tests PASSED" -ForegroundColor Green
        exit 0
    }
}
else {
    Write-Host "ERROR: Test results file not found" -ForegroundColor Red
    Write-Host "Check log file: $logFile" -ForegroundColor Gray
    
    if (Test-Path $logFile) {
        Write-Host ""
        Write-Host "Last 30 lines of log:" -ForegroundColor Yellow
        Get-Content $logFile -Tail 30
    }
    
    exit 1
}
