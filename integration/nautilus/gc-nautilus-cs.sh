#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gc-nautilus-cs.sh  —  Nautilus script for C# specific copying
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

# Harden PATH to ensure ~/.local/bin and other common locations are found by Nautilus
export PATH="$HOME/.local/bin:$HOME/bin:/usr/local/bin:$PATH"

announce() {
    if command -v notify-send >/dev/null; then
        notify-send "gc" "$1"
    else
        printf '%s\n' "$1"
    fi
}

target_directory() {
    local selection="${NAUTILUS_SCRIPT_SELECTED_FILE_PATHS:-}"
    # Take first selected item
    selection="${selection%%$'\n'*}"
    if [[ -d "$selection" ]]; then
        printf '%s' "$selection"
    else
        printf '%s' "$PWD"
    fi
}

main() {
    local directory
    directory="$(target_directory)"

    if ! command -v gc >/dev/null; then
        announce "gc not found on PATH. Install it to ~/.local/bin"
        exit 127
    fi

    cd "$directory" || { announce "cannot enter $directory"; exit 1; }

    # Run gc with C# specific flags
    # -e cs: Only C# files
    # -z \\: Exclude lines starting with backslash (common in some templates)
    if gc -e cs -z \\; then
        announce "Copied $(basename "$directory") (C# only) to clipboard"
    else
        announce "gc failed in $directory"
        exit 1
    fi
}

main
