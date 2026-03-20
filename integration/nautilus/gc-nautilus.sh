#!/bin/bash

# gc - Nautilus Context Menu Script
# This script integrates 'gc' (Git Copy) with the Nautilus file explorer.
# It allows right-clicking on folders/files to copy their contents as markdown.

# Dependencies:
# - gc (must be in PATH or absolute path)
# - libnotify-bin (for notify-send, optional)
# - xclip or wl-clipboard (for clipboard support)

# Notification function with fallbacks
send_notification() {
    local title="$1"
    local message="$2"
    local icon="${3:-info}"
    
    if command -v notify-send &> /dev/null; then
        notify-send "$title" "$message" -i "$icon"
    elif command -v zenity &> /dev/null; then
        zenity --notification --text="$title: $message" 2>/dev/null || true
    elif command -v kdialog &> /dev/null; then
        kdialog --passivepopup "$title: $message" 5 2>/dev/null || true
    fi
}

# URL decode function (safe from format string vulnerabilities)
url_decode() {
    local url_encoded="${1//+/ }"
    python3 -c "import sys, urllib.parse; print(urllib.parse.unquote(sys.argv[1]))" "$url_encoded" 2>/dev/null || \
    perl -pe 's/%([0-9a-f]{2})/chr(hex($1))/eig' <<< "$url_encoded"
}


# Log file for debugging
LOG_FILE="/tmp/gc-nautilus.log"
echo "--- $(date) ---" > "$LOG_FILE"

# Nautilus passes selected paths via environment variable
if [ -z "$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS" ]; then
    send_notification "gc" "No files or folders selected." "info"
    echo "No paths selected" >> "$LOG_FILE"
    exit 0
fi

# Prepare paths array
TARGET_PATHS=()
# Process NAUTILUS_SCRIPT_SELECTED_FILE_PATHS line by line
while IFS= read -r path; do
    if [ -n "$path" ]; then
        # Handle file:// URIs and URL-encoded paths
        if [[ "$path" == file://* ]]; then
            path="${path#file://}"
        fi
        
        # URL decode the path (handles spaces and special characters)
        path=$(url_decode "$path")
        
        # Make absolute if relative
        if [[ "$path" != /* ]]; then
            path="$(pwd)/$path"
        fi
        TARGET_PATHS+=("$path")
        echo "Target path: $path" >> "$LOG_FILE"
    fi
done <<< "$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS"

# Helper function to find git root for a given path
find_git_root() {
    local path="$1"
    local search_dir
    
    if [ -f "$path" ]; then
        search_dir=$(dirname "$path")
    else
        search_dir="$path"
    fi
    
    local current_dir="$search_dir"
    while [ "$current_dir" != "/" ]; do
        if [ -d "$current_dir/.git" ]; then
            echo "$current_dir"
            return 0
        fi
        current_dir=$(dirname "$current_dir")
    done
    
    # No git root found, return the search dir itself
    echo "$search_dir"
    return 1
}

# Group paths by git repository
declare -A REPO_PATHS
for path in "${TARGET_PATHS[@]}"; do
    git_root=$(find_git_root "$path")
    if [ -z "${REPO_PATHS[$git_root]}" ]; then
        REPO_PATHS[$git_root]="$path"
    else
        REPO_PATHS[$git_root]="${REPO_PATHS[$git_root]}"$'\n'"$path"
    fi
    echo "Path '$path' -> Repository: $git_root" >> "$LOG_FILE"
done

# Process each repository separately
COMBINED_OUTPUT=""
COMBINED_EXIT_CODE=0

for repo_root in "${!REPO_PATHS[@]}"; do
    echo "Processing repository: $repo_root" >> "$LOG_FILE"
    cd "$repo_root" || continue
    
    # Get paths for this repository
    mapfile -t repo_paths <<< "${REPO_PATHS[$repo_root]}"
    
    echo "Running: gc --paths -- \"${repo_paths[@]}\"" >> "$LOG_FILE"
    
    # Run gc for this repository
    OUTPUT=$(gc --paths -- "${repo_paths[@]}" 2>&1)
    EXIT_CODE=$?
    
    echo "Exit code: $EXIT_CODE" >> "$LOG_FILE"
    echo "Output: $OUTPUT" >> "$LOG_FILE"
    
    COMBINED_OUTPUT+="$OUTPUT"$'\n'
    if [ $EXIT_CODE -ne 0 ]; then
        COMBINED_EXIT_CODE=$EXIT_CODE
    fi
done

# Show notification based on combined results
if [ $COMBINED_EXIT_CODE -eq 0 ]; then
    # Extract stats from output if possible
    # Remove [OK] prefix and ANSI color codes if any
    STATS=$(echo "$COMBINED_OUTPUT" | grep "Exported to" | sed 's/\[OK\] //g' | sed 's/\x1b\[[0-9;]*m//g')
    if [ -z "$STATS" ]; then
        STATS="Contents copied to clipboard successfully."
    fi
    send_notification "gc" "${STATS}" "checkbox-checked-symbolic"
else
    # Show error message
    # Remove [ERROR] prefix and ANSI color codes
    ERROR_MSG=$(echo "$COMBINED_OUTPUT" | grep "\[ERROR\]" | head -n 1 | sed 's/\[ERROR\] //g' | sed 's/\x1b\[[0-9;]*m//g')
    if [ -z "$ERROR_MSG" ]; then
        ERROR_MSG=$(echo "$COMBINED_OUTPUT" | head -n 1)
    fi
    send_notification "gc" "Error: ${ERROR_MSG}" "error"
fi
