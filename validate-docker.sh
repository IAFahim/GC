#!/bin/bash

# Docker Setup Validation Script
# This script validates the Docker configuration without requiring Docker to be installed

echo "================================"
echo "GC Docker Setup Validation"
echo "================================"
echo ""

PASS=0
FAIL=0

# Function to check file exists
check_file() {
    if [ -f "$1" ]; then
        echo "✓ Found: $1"
        ((PASS++))
        return 0
    else
        echo "✗ Missing: $1"
        ((FAIL++))
        return 1
    fi
}

# Function to check file is executable
check_executable() {
    if [ -x "$1" ]; then
        echo "✓ Executable: $1"
        ((PASS++))
        return 0
    else
        echo "✗ Not executable: $1"
        ((FAIL++))
        return 1
    fi
}

# Function to check file content
check_content() {
    if grep -q "$2" "$1" 2>/dev/null; then
        echo "✓ $1 contains: $2"
        ((PASS++))
        return 0
    else
        echo "✗ $1 missing: $2"
        ((FAIL++))
        return 1
    fi
}

echo "Checking Docker configuration files..."
echo ""

# Check Dockerfiles
check_file "Dockerfile"
check_file "Dockerfile.simple"
check_content "Dockerfile.simple" "FROM mcr.microsoft.com/dotnet/sdk"
check_content "Dockerfile.simple" "dotnet build"

echo ""
echo "Checking Docker Compose configuration..."
check_file "docker-compose.yml"
check_content "docker-compose.yml" "version:"
check_content "docker-compose.yml" "services:"
check_content "docker-compose.yml" "gc:"

echo ""
echo "Checking .dockerignore..."
check_file ".dockerignore"
check_content ".dockerignore" "bin/"
check_content ".dockerignore" "obj/"

echo ""
echo "Checking Windows PowerShell scripts..."
check_file "docker-build.ps1"
check_file "docker-run.ps1"
check_file "docker-test.ps1"
check_file "docker-shell.ps1"

echo ""
echo "Checking Unix shell scripts..."
check_file "docker-build.sh"
check_file "docker-run.sh"
check_file "docker-test.sh"
check_file "docker-shell.sh"

echo ""
echo "Checking script permissions..."
check_executable "docker-build.sh"
check_executable "docker-run.sh"
check_executable "docker-test.sh"
check_executable "docker-shell.sh"

echo ""
echo "Checking documentation..."
check_file "DOCKER.md"
check_content "DOCKER.md" "Windows"
check_content "DOCKER.md" "Mac"
check_content "DOCKER.md" "Linux"

echo ""
echo "Checking project files..."
check_file "GC/GC.csproj"
check_file "GC/Program.cs"
check_content "GC/Program.cs" "Main"

echo ""
echo "================================"
echo "Validation Summary"
echo "================================"
echo "Passed: $PASS"
echo "Failed: $FAIL"
echo ""

if [ $FAIL -eq 0 ]; then
    echo "✓ All validation checks passed!"
    echo ""
    echo "Docker Setup Validation: PASSED"
    echo ""
    echo "Next steps when Docker is available:"
    echo "  1. Run: ./docker-build.sh (or .\\docker-build.ps1 on Windows)"
    echo "  2. Test: ./docker-test.sh"
    echo "  3. Run: ./docker-run.sh --help"
    exit 0
else
    echo "✗ Some validation checks failed!"
    echo ""
    echo "Please fix the above issues before testing with Docker."
    exit 1
fi
