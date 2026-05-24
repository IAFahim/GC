#!/usr/bin/env bash
#
# setup.sh - Install Nautilus file manager integration for gc
#
# This script installs the Nautilus integration script to the appropriate
# location in the user's home directory and makes it executable.
#
# Usage: ./setup.sh
#

set -euo pipefail

# Colors for output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly BLUE='\033[0;34m'
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

log_step() {
    echo -e "${BLUE}[STEP]${NC} $*" >&2
}

# Check if running on a supported system
check_system() {
    if [ ! -d "$HOME/.local/share/nautilus/scripts" ] && ! command -v nautilus &> /dev/null; then
        log_error "Nautilus file manager not found"
        log_info "This integration requires Nautilus (GNOME Files)"
        log_info "Install it with: sudo apt install nautilus"
        return 1
    fi
    return 0
}

# Create the Nautilus scripts directory
create_scripts_directory() {
    local scripts_dir="$HOME/.local/share/nautilus/scripts"

    if [ ! -d "$scripts_dir" ]; then
        log_step "Creating Nautilus scripts directory..."
        mkdir -p "$scripts_dir"
        log_info "Created: $scripts_dir"
    else
        log_info "Nautilus scripts directory exists: $scripts_dir"
    fi

    echo "$scripts_dir"
}

# Install the gc integration scripts
install_scripts() {
    local scripts_dir="$1"
    local script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    
    # 1. General purpose script
    local source_gen="$script_dir/gc-nautilus.sh"
    local target_gen="$scripts_dir/Copy to Clipboard (gc)"
    
    log_step "Installing general gc integration script..."
    cp "$source_gen" "$target_gen"
    chmod +x "$target_gen"
    log_info "Installed: $target_gen"

    # 2. C# specific script
    local source_cs="$script_dir/gc-nautilus-cs.sh"
    local target_cs="$scripts_dir/Copy C# Code (gc)"
    
    if [ -f "$source_cs" ]; then
        log_step "Installing C# specific gc integration script..."
        cp "$source_cs" "$target_cs"
        chmod +x "$target_cs"
        log_info "Installed: $target_cs"
    fi
}

# Test the installation
test_installation() {
    local scripts_dir="$1"
    local target_script="$scripts_dir/Copy to Clipboard (gc)"

    if [ ! -f "$target_script" ]; then
        log_error "Installation failed: script not found"
        return 1
    fi

    if [ ! -x "$target_script" ]; then
        log_error "Installation failed: script not executable"
        return 1
    fi

    log_info "Installation test passed"
    return 0
}

# Restart Nautilus to pick up the new script
restart_nautilus() {
    log_step "Restarting Nautilus..."
    if pgrep -x nautilus > /dev/null; then
        log_info "Stopping Nautilus..."
        nautilus -q &> /dev/null || true
        sleep 2
        log_info "Starting Nautilus..."
        nautilus &> /dev/null &
        sleep 2
        log_info "Nautilus restarted"
    else
        log_info "Nautilus is not running, no restart needed"
    fi
}

# Print usage instructions
print_usage() {
    cat << 'EOF'

╔════════════════════════════════════════════════════════════╗
║         gc Nautilus Integration - Installation Complete   ║
╚════════════════════════════════════════════════════════════╝

The gc integration has been installed successfully!

USAGE:
  1. Open Nautilus file manager
  2. Right-click on any directory OR empty space inside a directory
  3. Navigate to: Scripts → Copy to Clipboard (gc)
  4. Click to run gc on the selection (or current directory)

The script will:
  • Generate AI-ready markdown from the selection
  • Copy it to your clipboard
  • Show notifications for progress and completion

TROUBLESHOOTING:
  • If the script doesn't appear: Restart Nautilus (nautilus -q)
  • If it doesn't work: Check that gc is installed in ~/.local/bin or /usr/local/bin
  • PATH is automatically hardened in the script to find common install locations

UNINSTALL:
  Remove the scripts:
    rm "$HOME/.local/share/nautilus/scripts/Copy to Clipboard (gc)"
    rm "$HOME/.local/share/nautilus/scripts/Copy C# Code (gc)"

For more information, visit: https://github.com/IAFahim/gc

EOF
}

# Main installation function
main() {
    echo -e "${BLUE}"
    cat << 'EOF'
╔════════════════════════════════════════════════════════════╗
║         gc Nautilus Integration - Installer              ║
╚════════════════════════════════════════════════════════════╝
EOF
    echo -e "${NC}"

    # Check system
    if ! check_system; then
        log_error "System check failed. Aborting installation."
        exit 1
    fi

    # Create scripts directory
    local scripts_dir
    scripts_dir=$(create_scripts_directory)

    # Install scripts
    if ! install_scripts "$scripts_dir"; then
        log_error "Installation failed. Aborting."
        exit 1
    fi

    # Test installation
    if ! test_installation "$scripts_dir"; then
        log_error "Installation test failed. Aborting."
        exit 1
    fi

    # Restart Nautilus
    restart_nautilus

    # Print success and usage
    log_info "Installation completed successfully!"
    print_usage
}

# Run main function
main "$@"
