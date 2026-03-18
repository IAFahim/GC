# GC Installation Script for Windows
# This script downloads and installs the latest version of GC

$ErrorActionPreference = "Stop"

# Colors
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

Write-ColorOutput Green "Fetching latest release version..."

# Repository name (hardcoded to avoid git dependency)
$repoName = "IAFahim/GC"

try {
    # Get latest release
    $latestRelease = Invoke-RestMethod -Uri "https://api.github.com/repos/$repoName/releases/latest"
    $version = $latestRelease.tag_name

    if ([string]::IsNullOrEmpty($version)) {
        Write-ColorOutput Yellow "Warning: Could not fetch latest version, using v0.1.0"
        $version = "v0.1.0"
    }
}
catch {
    Write-ColorOutput Yellow "Warning: Could not fetch latest version, using v0.1.0"
    $version = "v0.1.0"
}

# Construct download URL
$archiveName = "gc-windows-x64.zip"
$downloadUrl = "https://github.com/$repoName/releases/download/$version/$archiveName"

# Create temp directory
$tempDir = Join-Path $env:TEMP "gc-install"
if (Test-Path $tempDir) {
    Remove-Item -Recurse -Force $tempDir
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    # Download archive
    Write-ColorOutput Green "Downloading GC $version for Windows x64..."
    $outputPath = Join-Path $tempDir $archiveName
    Invoke-WebRequest -Uri $downloadUrl -OutFile $outputPath

    # Extract archive
    Write-ColorOutput Green "Extracting archive..."
    $extractDir = Join-Path $tempDir "gc"
    Expand-Archive -Path $outputPath -DestinationPath $extractDir -Force

    # Install
    $installDir = Join-Path $env:LOCALAPPDATA "Programs\GC"
    if (!(Test-Path $installDir)) {
        New-Item -ItemType Directory -Path $installDir | Out-Null
    }

    $binaryPath = Join-Path $extractDir "GC.exe"
    if (Test-Path $binaryPath) {
        Copy-Item -Path $binaryPath -Destination "$installDir\git-copy.exe" -Force
        Write-ColorOutput Green "Successfully installed GC to $installDir\git-copy.exe"

        # Add to PATH if not already there
        $pathEnv = [Environment]::GetEnvironmentVariable("Path", "User")
        if ($pathEnv -notlike "*$installDir*") {
            Write-ColorOutput Yellow "Adding $installDir to user PATH..."
            [Environment]::SetEnvironmentVariable("Path", "$pathEnv;$installDir", "User")
            Write-ColorOutput Yellow "Please restart your terminal for PATH changes to take effect."
        }

        Write-ColorOutput Green "Installation complete! Run 'git-copy --help' to get started."
    }
    else {
        Write-ColorOutput Red "Error: Could not find GC.exe in archive"
        exit 1
    }
}
finally {
    # Cleanup temp directory
    if (Test-Path $tempDir) {
        Remove-Item -Recurse -Force $tempDir
    }
}
