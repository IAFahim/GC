# Docker shell script for Windows
# Run this in PowerShell

Write-Host "Starting interactive shell in Docker container..." -ForegroundColor Green
Write-Host "Type 'exit' to leave the shell" -ForegroundColor Yellow
Write-Host ""

docker-compose run --rm gc-bash
