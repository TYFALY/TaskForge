# TaskForge Build Script
# Builds the UI, copies assets, and publishes the single-file executable

param(
    [switch]$SkipClean,
    [switch]$Debug
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
$TaskForgeUiDir = Join-Path $ProjectRoot "taskforge-ui"
$ApiProjectDir = Join-Path $ProjectRoot "src\TaskForge.Api"
$PublishDir = Join-Path $ProjectRoot "publish"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "TaskForge Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean previous build (optional)
if (-not $SkipClean) {
    Write-Host "[1/5] Cleaning previous build..." -ForegroundColor Yellow
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }
    Write-Host "      Done." -ForegroundColor Green
}
else {
    Write-Host "[1/5] Skipping clean..." -ForegroundColor Gray
}

# Step 2: Build UI
Write-Host "[2/5] Building React UI..." -ForegroundColor Yellow
Push-Location $TaskForgeUiDir
try {
    npm run build 2>&1 | ForEach-Object { Write-Host "      $_" }
    if ($LASTEXITCODE -ne 0) {
        throw "npm run build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
Write-Host "      Done." -ForegroundColor Green

# Step 3: Copy UI assets to wwwroot
Write-Host "[3/5] Copying UI assets to wwwroot..." -ForegroundColor Yellow
$DistDir = Join-Path $TaskForgeUiDir "dist"
$WwwrootDir = Join-Path $ApiProjectDir "wwwroot"

# Create wwwroot directory if it doesn''t exist
if (-not (Test-Path $WwwrootDir)) {
    New-Item -ItemType Directory -Path $WwwrootDir -Force | Out-Null
}

# Clear existing files in wwwroot
Get-ChildItem $WwwrootDir -Recurse | Remove-Item -Recurse -Force

# Copy new files
Copy-Item -Path "$DistDir\*" -Destination $WwwrootDir -Recurse -Force
$fileCount = (Get-ChildItem $WwwrootDir -Recurse -File).Count
Write-Host "      Copied $fileCount files to wwwroot" -ForegroundColor Gray
Write-Host "      Done." -ForegroundColor Green

# Step 4: Publish single-file executable
Write-Host "[4/5] Publishing single-file executable..." -ForegroundColor Yellow
$PublishArgs = @(
    "publish",
    "$ApiProjectDir\TaskForge.Api.csproj",
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-o", $PublishDir
)

if ($Debug) {
    $PublishArgs += "-c Debug"
}

& dotnet @PublishArgs 2>&1 | ForEach-Object { Write-Host "      $_" }
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}
Write-Host "      Done." -ForegroundColor Green

# Step 5: Verify output
Write-Host "[5/5] Verifying build output..." -ForegroundColor Yellow
$ExePath = Join-Path $PublishDir "TaskForge.exe"
if (Test-Path $ExePath) {
    $exeSize = (Get-Item $ExePath).Length / 1MB
    Write-Host "      Executable: $ExePath ($([math]::Round($exeSize, 2)) MB)" -ForegroundColor Gray
} else {
    Write-Host "      WARNING: TaskForge.exe not found at $ExePath" -ForegroundColor Red
}

$IndexPath = Join-Path $PublishDir "wwwroot\index.html"
if (Test-Path $IndexPath) {
    Write-Host "      index.html: $IndexPath" -ForegroundColor Gray
} else {
    Write-Host "      WARNING: index.html not found at $IndexPath" -ForegroundColor Red
}
Write-Host "      Done." -ForegroundColor Green

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To run the server:" -ForegroundColor White
Write-Host "  .\$PublishDir\TaskForge.exe" -ForegroundColor Cyan
Write-Host ""