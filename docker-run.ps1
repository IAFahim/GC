# Docker run script for Windows
# Run this in PowerShell

$args = $Args -join ' '

Write-Host "Running GC in Docker..." -ForegroundColor Green

if ([string]::IsNullOrEmpty($args)) {
    docker-compose run gc
} else {
    docker-compose run gc $args
}
