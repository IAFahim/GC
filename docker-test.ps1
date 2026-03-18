# Docker test script for Windows
# Run this in PowerShell

Write-Host "Running GC tests in Docker..." -ForegroundColor Green

docker-compose run --rm gc-test

if ($LASTEXITCODE -eq 0) {
    Write-Host "All tests passed!" -ForegroundColor Green
} else {
    Write-Host "Tests failed!" -ForegroundColor Red
    exit 1
}
