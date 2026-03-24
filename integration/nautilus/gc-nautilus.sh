#!/usr/bin/env bash
#
# gc-nautilus.sh - Nautilus file manager integration for gc (Git Copy)
#
# This script integrates gc with Nautilus/GNOME Files, allowing users to
# right-click on directories and select "Copy to Clipboard" to generate
# AI-ready markdown from the selected directory.
#
# Installation: Run ./setup.sh in this directory
# Usage: Right-click a directory in Nautilus → Scripts → Copy to Clipboard
#

set -euo pipefail

# Colors for output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly NC='\033[0m' # No Color

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

# Check if gc is installed
check_gc_installed() {
    # Try to find gc in common locations
    local gc_path=""
    if [ -f "$HOME/.local/bin/gc" ]; then
        gc_path="$HOME/.local/bin/gc"
    elif command -v gc &> /dev/null; then
        gc_path=$(command -v gc)
    fi
    
    if [ -z "$gc_path" ]; then
        log_error "gc is not installed or not in PATH"
        log_info "Install gc from: https://github.com/yourusername/gc"
        notify-send --icon=error "gc Not Found" "gc is not installed. Please install gc first."
        exit 1
    fi
    
    # Export for use in main function
    GC_PATH="$gc_path"
}

# Main function - process selected directory
main() {
    check_gc_installed

    # Nautilus passes selected file paths as arguments
    if [ $# -eq 0 ]; then
        log_error "No directory selected"
        notify-send --icon=error "Error" "No directory selected"
        exit 1
    fi

    local target_dir="$1"

    # Validate the path
    if [ ! -d "$target_dir" ]; then
        log_error "Not a directory: $target_dir"
        notify-send --icon=error "Invalid Selection" "Selected path is not a directory"
        exit 1
    fi

    log_info "Processing directory: $target_dir"

    # Show a notification that processing is starting
    notify-send --icon=document-save "gc Processing" "Generating markdown from: $(basename "$target_dir")"

    # Run gc from the target directory using absolute path
    local output
    if cd "$target_dir" && output=$("$GC_PATH" 2>&1); then
        log_info "Successfully generated markdown"
        notify-send --icon=dialog-ok "gc Success" "Copied to clipboard: $(basename "$target_dir")"
    else
        local exit_code=$?
        log_error "gc failed with exit code $exit_code"
        log_error "Output: $output"
        notify-send --icon=dialog-error "gc Failed" "Failed to process: $(basename "$target_dir")\nError: $output"
        exit $exit_code
    fi
}

# Run main function
main "$@"
