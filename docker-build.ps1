# Docker build script for Windows
# Run this in PowerShell

Write-Host "Building GC Docker image for Windows..." -ForegroundColor Green

docker-compose build gc

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Run commands:" -ForegroundColor Yellow
    Write-Host "  .\docker-run.ps1            - Show help"
    Write-Host "  .\docker-run.ps1 -Args ...  - Run with arguments"
    Write-Host "  .\docker-test.ps1           - Run tests"
    Write-Host "  .\docker-shell.ps1          - Interactive shell"
} else {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
