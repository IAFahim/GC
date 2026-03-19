# gc Installation Script for Windows
# This script downloads and installs the latest version of gc

$ErrorActionPreference = "Stop"

function Write-ColorOutput {
    param(
        [Parameter(Mandatory=$true)]
        [string]$Color,
        [Parameter(Mandatory=$true)]
        [string]$Message
    )
    Write-Host $Message -ForegroundColor $Color
}

# Repository details
$repoName = "IAFahim/gc"

try {
    # Get latest release version
    Write-ColorOutput Green "Fetching latest release version..."
    $releaseUrl = "https://api.github.com/repos/$repoName/releases/latest"
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers @{"Accept"="application/vnd.github.v3+json"}
    $version = $release.tag_name

    if (-not $version) {
        $version = "latest"
        Write-ColorOutput Yellow "Warning: Could not fetch latest version, using latest release"
    }

    # Construct download URL
    $archiveName = "gc-windows-x64.zip"
    $downloadUrl = "https://github.com/$repoName/releases/download/$version/$archiveName"

    # Create temp directory
    $tempDir = Join-Path $env:TEMP "gc-install"
    if (Test-Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
    New-Item -Path $tempDir -ItemType Directory | Out-Null

    try {
        # Download archive
        Write-ColorOutput Green "Downloading gc $version for Windows x64..."
        $archivePath = Join-Path $tempDir $archiveName
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath

        # Extract archive
        Write-ColorOutput Green "Extracting archive..."
        $extractDir = Join-Path $tempDir "gc_bin"
        New-Item -Path $extractDir -ItemType Directory | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractDir)

        # Install
        $installDir = Join-Path $env:LOCALAPPDATA "Programs\gc"
        if (-not (Test-Path $installDir)) { New-Item -Path $installDir -ItemType Directory | Out-Null }

        $binaryPath = Join-Path $extractDir "gc.exe"
        if (Test-Path $binaryPath) {
            Move-Item -Path $binaryPath -Destination (Join-Path $installDir "gc.exe") -Force
            Write-ColorOutput Green "Successfully installed gc to $installDir\gc.exe"
        }
        else {
            Write-ColorOutput Red "Error: Could not find gc.exe in archive"
            Get-ChildItem -Path $extractDir -Recurse
            return
        }

        # Add to PATH if not already there
        $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
        if ($userPath -notlike "*$installDir*") {
            [Environment]::SetEnvironmentVariable("Path", "$userPath;$installDir", "User")
            
            # Broadcast WM_SETTINGCHANGE to update Explorer and other apps
            if (-not ("Win32.NativeMethods" -as [type])) {
                Add-Type -Namespace Win32 -Name NativeMethods -MemberDefinition @"
                    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
                    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
"@
            }
            $HWND_BROADCAST = [IntPtr]0xffff
            $WM_SETTINGCHANGE = 0x001A
            $SMTO_ABORTIFHUNG = 0x0002
            $result = [UIntPtr]::Zero
            [Win32.NativeMethods]::SendMessageTimeout($HWND_BROADCAST, $WM_SETTINGCHANGE, [UIntPtr]::Zero, "Environment", $SMTO_ABORTIFHUNG, 5000, [ref]$result) | Out-Null

            Write-ColorOutput Yellow "Added $installDir to your user PATH."
            Write-ColorOutput Yellow "Please restart your terminal for changes to take effect."
        }

        Write-ColorOutput Green "Installation complete! Run 'gc --help' to get started."
    }
    finally {
        # Cleanup
        if (Test-Path $tempDir) { Remove-Item -Path $tempDir -Recurse -Force }
    }
}
catch {
    Write-ColorOutput Red "Error during installation: $_"
    exit 1
}
