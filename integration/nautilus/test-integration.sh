#!/usr/bin/env bash
#
# test-integration.sh - Test the Nautilus integration for gc
#
# This script tests the Nautilus integration by simulating what happens
# when you right-click a directory and select the gc script.
#
# Usage: ./test-integration.sh
#

set -euo pipefail

# Colors for output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
readonly NC='\033[0m' # No Color

# Test counters
TESTS_PASSED=0
TESTS_FAILED=0

# Logging functions
log_info() {
    echo -e "${GREEN}[INFO]${NC} $*" >&2
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $*" >&2
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $*" >&2
}

log_test() {
    echo -e "${BLUE}[TEST]${NC} $*" >&2
}

log_pass() {
    echo -e "${GREEN}[PASS]${NC} $*" >&2
    ((TESTS_PASSED++))
}

log_fail() {
    echo -e "${RED}[FAIL]${NC} $*" >&2
    ((TESTS_FAILED++))
}

# Test functions
test_gc_installed() {
    log_test "Checking if gc is installed..."

    if command -v gc &> /dev/null; then
        local gc_version
        gc_version=$(gc --version 2>&1 || echo "unknown")
        log_pass "gc is installed (version: $gc_version)"
        return 0
    else
        log_fail "gc is not installed or not in PATH"
        return 1
    fi
}

test_script_exists() {
    log_test "Checking if gc-nautilus.sh exists..."

    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local script_path="$script_dir/gc-nautilus.sh"

    if [ -f "$script_path" ]; then
        log_pass "gc-nautilus.sh exists: $script_path"
        return 0
    else
        log_fail "gc-nautilus.sh not found: $script_path"
        return 1
    fi
}

test_script_executable() {
    log_test "Checking if gc-nautilus.sh is executable..."

    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local script_path="$script_dir/gc-nautilus.sh"

    if [ -x "$script_path" ]; then
        log_pass "gc-nautilus.sh is executable"
        return 0
    else
        log_fail "gc-nautilus.sh is not executable"
        log_info "Run: chmod +x gc-nautilus.sh"
        return 1
    fi
}

test_script_syntax() {
    log_test "Checking gc-nautilus.sh syntax..."

    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local script_path="$script_dir/gc-nautilus.sh"

    if bash -n "$script_path" 2>&1; then
        log_pass "gc-nautilus.sh has valid syntax"
        return 0
    else
        log_fail "gc-nautilus.sh has syntax errors"
        return 1
    fi
}

test_nautilus_scripts_dir() {
    log_test "Checking Nautilus scripts directory..."

    local scripts_dir="$HOME/.local/share/nautilus/scripts"

    if [ -d "$scripts_dir" ]; then
        log_pass "Nautilus scripts directory exists: $scripts_dir"
        return 0
    else
        log_fail "Nautilus scripts directory not found: $scripts_dir"
        log_info "Run: ./setup.sh to create it"
        return 1
    fi
}

test_script_installed() {
    log_test "Checking if gc-nautilus.sh is installed..."

    local installed_script="$HOME/.local/share/nautilus/scripts/gc-nautilus.sh"

    if [ -f "$installed_script" ]; then
        if [ -x "$installed_script" ]; then
            log_pass "gc-nautilus.sh is installed and executable"
            return 0
        else
            log_fail "gc-nautilus.sh is installed but not executable"
            return 1
        fi
    else
        log_fail "gc-nautilus.sh is not installed"
        log_info "Run: ./setup.sh"
        return 1
    fi
}

test_integration_with_test_directory() {
    log_test "Testing gc integration with a test directory..."

    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    local script_path="$script_dir/gc-nautilus.sh"

    # Create a temporary test directory
    local test_dir
    test_dir=$(mktemp -d)

    # Create some test files
    echo "// Test file 1" > "$test_dir/test1.cs"
    echo "// Test file 2" > "$test_dir/test2.cs"
    echo "README content" > "$test_dir/README.md"

    # Run the script
    if "$script_path" "$test_dir" 2>&1 | grep -q "Successfully"; then
        log_pass "gc integration works with test directory"
        rm -rf "$test_dir"
        return 0
    else
        log_fail "gc integration failed with test directory"
        rm -rf "$test_dir"
        return 1
    fi
}

# Print test summary
print_summary() {
    cat << "EOF"

╔════════════════════════════════════════════════════════════╗
║              Test Summary                                  ║
╚════════════════════════════════════════════════════════════╝

EOF

    local total_tests=$((TESTS_PASSED + TESTS_FAILED))
    echo -e "Total tests: ${BLUE}$total_tests${NC}"
    echo -e "Passed: ${GREEN}$TESTS_PASSED${NC}"
    echo -e "Failed: ${RED}$TESTS_FAILED${NC}"

    if [ $TESTS_FAILED -eq 0 ]; then
        echo -e "\n${GREEN}All tests passed!${NC}\n"
        return 0
    else
        echo -e "\n${RED}Some tests failed. Please fix the issues above.${NC}\n"
        return 1
    fi
}

# Main test runner
main() {
    echo -e "${BLUE}"
    cat << 'EOF'
╔════════════════════════════════════════════════════════════╗
║         gc Nautilus Integration - Test Suite              ║
╚════════════════════════════════════════════════════════════╝
EOF
    echo -e "${NC}"

    # Run all tests
    test_gc_installed || true
    test_script_exists || true
    test_script_executable || true
    test_script_syntax || true
    test_nautilus_scripts_dir || true
    test_script_installed || true
    test_integration_with_test_directory || true

    # Print summary
    print_summary
}

# Run main function
main "$@"
