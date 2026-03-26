param(
    [Parameter(Mandatory=$true)]
    [string]$UnityProjectPath
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$PackagePath = Join-Path $ScriptDir "Packages\com.gitcopy.gc"
$TargetDir = Join-Path $UnityProjectPath "Packages\com.gitcopy.gc"

if (-not (Test-Path (Join-Path $UnityProjectPath "Assets"))) {
    Write-Error "Error: Not a valid Unity project (Assets folder not found)"
    exit 1
}

if (Test-Path $TargetDir) {
    Write-Host "Removing existing GitCopy package..."
    Remove-Item -Recurse -Force $TargetDir
}

Write-Host "Installing GitCopy package to $TargetDir..."
New-Item -ItemType Directory -Path (Join-Path $UnityProjectPath "Packages") -Force | Out-Null
Copy-Item -Recurse -Path $PackagePath -Destination $TargetDir

Write-Host ""
Write-Host "✓ GitCopy package installed successfully!"
Write-Host ""
Write-Host "Next steps:"
Write-Host "1. Open Unity Editor"
Write-Host "2. The package will be automatically imported"
Write-Host "3. Right-click in Project Window > Git Copy > Copy to Clipboard"
Write-Host ""
Write-Host "Note: Make sure 'gc' CLI tool is installed on your system"
Write-Host "Install from: https://github.com/IAFahim/gc"
