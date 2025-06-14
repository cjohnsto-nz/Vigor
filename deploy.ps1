# Vigor Mod Deployment Script
# This script stops the game, builds the project, copies the output, and restarts the game.

# --- Configuration ---
$ErrorActionPreference = 'Stop' # Exit script on any error
$ProjectName = "Vigor"
$ProjectRoot = $PSScriptRoot # This special variable gets the directory where the script is located
$ModsDir = "C:\Users\chris\AppData\Roaming\VintagestoryData\Mods"
$VSProcessName = "Vintagestory"
$VSExePath = "C:\Users\chris\AppData\Roaming\Vintagestory\Vintagestory.exe"

# --- Pre-Build: Stop Game ---
Write-Host "Checking for running Vintage Story process..." -ForegroundColor Cyan
$vsProcess = Get-Process -Name $VSProcessName -ErrorAction SilentlyContinue
if ($vsProcess) {
    Write-Host "Vintage Story is running. Stopping process..."
    Stop-Process -Name $VSProcessName -Force
    # Give it a moment to release file locks
    Start-Sleep -Seconds 2
}

# --- Build Step ---
Write-Host "Cleaning project..." -ForegroundColor Cyan
dotnet clean

Write-Host "Building Vigor project..." -ForegroundColor Cyan
dotnet build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED. Deployment aborted." -ForegroundColor Red
    exit 1
}

Write-Host "Build SUCCEEDED." -ForegroundColor Green

# --- Deploy Step ---
$SourceDir = Join-Path $ProjectRoot "bin\Debug\ModPackage\$ProjectName"
$DestinationDir = Join-Path $ModsDir $ProjectName

if (-not (Test-Path $SourceDir)) {
    Write-Host "Error: Packaged mod directory not found at '$SourceDir'." -ForegroundColor Red
    Write-Host "Please check the packaging <Target> in your .csproj file." -ForegroundColor Red
    exit 1
}

Write-Host "Deploying mod to '$ModsDir'..." -ForegroundColor Cyan

if (Test-Path $DestinationDir) {
    Write-Host "Removing old version at '$DestinationDir'"
    Remove-Item -Recurse -Force $DestinationDir
}

Write-Host "Copying new version from '$SourceDir'"
Copy-Item -Path $SourceDir -Destination $ModsDir -Recurse

# --- Post-Deploy: Start Game ---
Write-Host "`nDeployment COMPLETE. Launching Vintage Story..." -ForegroundColor Green
Start-Process -FilePath $VSExePath

# --- Post-Launch: Commit Logs ---
# Give the game a moment to start and write initial logs before committing.
Start-Sleep -Seconds 5 
$LogRepoDir = "C:\Users\chris\AppData\Roaming\VintagestoryData\Logs"
if (Test-Path (Join-Path $LogRepoDir ".git")) {
    Write-Host "Committing logs to reset diff for next session..." -ForegroundColor Cyan
    Push-Location $LogRepoDir
    git add .
    # Use a generic commit message. The timestamp will differentiate commits.
    git commit -m "Autocommit logs post-launch" | Out-Null
    Pop-Location
    Write-Host "Log commit complete." -ForegroundColor Green
} else {
    Write-Host "Git repository not found in Logs directory. Skipping commit." -ForegroundColor Yellow
}
