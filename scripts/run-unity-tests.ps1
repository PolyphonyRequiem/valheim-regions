# Run Unity Test Framework tests from command line
# Returns exit code 0 if all tests pass, non-zero otherwise

param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.8f1\Editor\Unity.exe",
    [string]$ProjectPath = "$PSScriptRoot\..\unity-perlin-wrapper",
    [string]$TestFilter = "",
    [switch]$ShowResults
)

$ErrorActionPreference = "Stop"

Write-Host "================================" -ForegroundColor Cyan
Write-Host "Unity Test Runner" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
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

Write-Host "Running Unity tests..." -ForegroundColor Yellow
Write-Host "This will take 30-60 seconds..." -ForegroundColor Gray
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
        Get-Content $logFile | Select-String -Pattern "WorldGeneratorUnity|OVERALL ACCURACY|Ground Truth|Matches:|accuracy:" -Context 0,1
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
