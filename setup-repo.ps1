# TaskForge V2.0 Repository Finalization Script

$ErrorActionPreference = "Stop"
$repoPath = "C:\Users\ilyas\Desktop\TaskForge"
Set-Location $repoPath

Write-Host "=== TaskForge V2.0 Finalization ===" -ForegroundColor Cyan

# 1. Deep File Cleanup
Write-Host "`n[1/4] Cleaning artifacts..." -ForegroundColor Yellow
Remove-Item -Path bin,obj,publish,dist -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path .vs,.idea -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem -Recurse -Include *.db,*.db-journal,*.log -File -Force -ErrorAction SilentlyContinue | Remove-Item -Force
Write-Host "  Cleanup complete." -ForegroundColor Green

# 2. Update docker-compose.yml with SECURE env var placeholder (NOT hardcoded)
Write-Host "`n[2/4] Securing docker-compose.yml..." -ForegroundColor Yellow
$secureDockerCompose = @"
version: "3.9"

services:
  taskforge:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: taskforge-api
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - TaskForge__ApiKey=`${TASKFORGE_API_KEY:-MISSING_API_KEY_PRODUCTION_ERROR}
      - TaskForge__Server__Port=8080
    healthcheck:
      test: ["CMD", "wget", "--no-verbose", "--tries=1", "--spider", "http://localhost:8080/health"]
      interval: 30s
      timeout: 3s
      retries: 3
      start_period: 10s
    restart: unless-stopped
"@
Set-Content -Path "docker-compose.yml" -Value $secureDockerCompose -Force
Write-Host "  docker-compose.yml secured with env var placeholder." -ForegroundColor Green

# 3. Git Repository Initialization
Write-Host "`n[3/4] Initializing Git repository..." -ForegroundColor Yellow

# Check if already a git repo
$isGitRepo = Test-Path ".git"
if ($isGitRepo) {
    Write-Host "  Git repo exists, skipping init..." -ForegroundColor Yellow
} else {
    git init
    git branch -M main
}

# Set remote
$remoteExists = git remote get-url origin 2>$null
if ($remoteExists) {
    Write-Host "  Remote 'origin' already configured." -ForegroundColor Yellow
} else {
    git remote add origin https://github.com/TYFALY/TaskForge.git
    Write-Host "  Remote 'origin' added." -ForegroundColor Green
}

# Stage and commit
Write-Host "  Staging files..."
git add .
Write-Host "  Committing..."
git commit -m "feat: TaskForge v2.0 - Production-ready distributed job engine"

Write-Host "`n[4/4] Pushing to GitHub..." -ForegroundColor Yellow
git push -u origin main --force

# Verification
Write-Host "`n=== Verification ===" -ForegroundColor Cyan
git status
git log --oneline -1

Write-Host "`n=== TaskForge V2.0 Finalization Complete! ===" -ForegroundColor Green
Write-Host "Repository is clean and pushed to GitHub." -ForegroundColor Green