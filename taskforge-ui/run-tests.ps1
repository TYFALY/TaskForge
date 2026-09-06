# TaskForge Dashboard Test Runner
# This script starts the dev server, runs Playwright tests, and takes a screenshot

$ErrorActionPreference = "Continue"
$projectDir = $PSScriptRoot
$serverUrl = "http://localhost:5173"
$maxWait = 30

Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  TaskForge Dashboard QA Test Runner" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Check if server is already running
$serverRunning = $false
try {
    $response = Invoke-WebRequest -Uri $serverUrl -TimeoutSec 2 -ErrorAction SilentlyContinue
    if ($response.StatusCode -eq 200) {
        $serverRunning = $true
        Write-Host "[INFO] Dev server already running at $serverUrl" -ForegroundColor Green
    }
} catch {
    # Server not running
}

# Start dev server if not running
$serverJob = $null
if (-not $serverRunning) {
    Write-Host "[INFO] Starting Vite dev server..." -ForegroundColor Yellow
    Push-Location $projectDir
    $serverJob = Start-Job -ScriptBlock {
        param($dir)
        Set-Location $dir
        npm run dev
    } -ArgumentList $projectDir

    # Wait for server to start
    Write-Host "[INFO] Waiting for server to start..." -ForegroundColor Yellow
    $waited = 0
    while ($waited -lt $maxWait) {
        try {
            $response = Invoke-WebRequest -Uri $serverUrl -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Host "[INFO] Server is ready!" -ForegroundColor Green
                break
            }
        } catch {
            Start-Sleep -Seconds 1
            $waited++
        }
    }

    if ($waited -ge $maxWait) {
        Write-Host "[ERROR] Server failed to start within $maxWait seconds" -ForegroundColor Red
        if ($serverJob) { Stop-Job $serverJob; Remove-Job $serverJob }
        exit 1
    }
    Pop-Location
}

Write-Host ""

# Run Playwright tests
Write-Host "[INFO] Running Playwright tests..." -ForegroundColor Yellow
Push-Location $projectDir
node test-dashboard.js
$testResult = $LASTEXITCODE
Pop-Location

# Cleanup
if ($serverJob) {
    Write-Host "[INFO] Stopping dev server..." -ForegroundColor Yellow
    Stop-Job $serverJob
    Remove-Job $serverJob -Force
}

# Check if screenshot was created
$screenshotPath = Join-Path $PSScriptRoot "..\docs\dashboard-preview.png"
if (Test-Path $screenshotPath) {
    $size = (Get-Item $screenshotPath).Length / 1KB
    Write-Host "[INFO] Screenshot saved: $screenshotPath ($([math]::Round($size, 2)) KB)" -ForegroundColor Green
} else {
    Write-Host "[WARN] Screenshot not found at $screenshotPath" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  Test run complete. Exit code: $testResult" -ForegroundColor $(if ($testResult -eq 0) { "Green" } else { "Red" })
Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Cyan

exit $testResult