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
# Force clean build artifacts
Write-Host "Removing old build artifacts..."
if (Test-Path "bin") { Remove-Item -Recurse -Force "bin" }
if (Test-Path "obj") { Remove-Item -Recurse -Force "obj" }

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
$TempDir = Join-Path $env:TEMP "VigorTempDeploy"

if (-not (Test-Path $SourceDir)) {
    Write-Host "Error: Packaged mod directory not found at '$SourceDir'." -ForegroundColor Red
    Write-Host "Please check the packaging <Target> in your .csproj file." -ForegroundColor Red
    exit 1
}

# Get version from modinfo.json to create versioned zip file
$ModInfoPath = Join-Path $SourceDir "modinfo.json"
$Version = "0.0.0" # Default version if not found
if (Test-Path $ModInfoPath) {
    $ModInfo = Get-Content $ModInfoPath | ConvertFrom-Json
    $Version = $ModInfo.version
}

# Create zip filename
$ZipFileName = "vigor_$Version.zip"
$ZipFilePath = Join-Path $ModsDir $ZipFileName

Write-Host "Deploying mod as '$ZipFileName' to '$ModsDir'..." -ForegroundColor Cyan

# Clean temp directory if it exists
if (Test-Path $TempDir) {
    Remove-Item -Recurse -Force $TempDir
}

# Create temp directory for packaging
New-Item -ItemType Directory -Path $TempDir | Out-Null

# Copy all files from source to temp
Copy-Item -Path "$SourceDir\*" -Destination $TempDir -Recurse

# Ensure modicon.png is included
$ModIconSource = Join-Path $ProjectRoot "modicon.png"
if (Test-Path $ModIconSource) {
    Write-Host "Including modicon.png in package"
    Copy-Item -Path $ModIconSource -Destination $TempDir
} else {
    Write-Host "Warning: modicon.png not found at '$ModIconSource'. Icon will not be included." -ForegroundColor Yellow
}

# Remove any existing zip file
if (Test-Path $ZipFilePath) {
    Write-Host "Removing existing zip at '$ZipFilePath'"
    Remove-Item -Force $ZipFilePath
}

# Create zip file
Write-Host "Creating zip file '$ZipFilePath'"
Compress-Archive -Path "$TempDir\*" -DestinationPath $ZipFilePath

# Delete old config if it exists
if (Test-Path "C:\Users\chris\AppData\Roaming\VintagestoryData\ModConfig\vigor.json") {
    Write-Host "Removing old config at 'C:\Users\chris\AppData\Roaming\VintagestoryData\ModConfig\vigor.json'"
    Remove-Item -Path "C:\Users\chris\AppData\Roaming\VintagestoryData\ModConfig\vigor.json" -Force
}

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
