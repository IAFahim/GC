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

# URL decode function
url_decode() {
    local url_encoded="${1//+/ }"
    printf '%b' "${url_encoded//%/\\x}"
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

# Resolve git root for the first selected item
# This is important when files are selected from search results or deep subdirectories
FIRST_PATH="${TARGET_PATHS[0]}"
GIT_ROOT=""

# Try to find git root starting from the file/directory
if [ -f "$FIRST_PATH" ]; then
    SEARCH_DIR=$(dirname "$FIRST_PATH")
else
    SEARCH_DIR="$FIRST_PATH"
fi

# Walk up the directory tree to find .git
CURRENT_DIR="$SEARCH_DIR"
while [ "$CURRENT_DIR" != "/" ]; do
    if [ -d "$CURRENT_DIR/.git" ]; then
        GIT_ROOT="$CURRENT_DIR"
        break
    fi
    CURRENT_DIR=$(dirname "$CURRENT_DIR")
done

# Set working directory
if [ -n "$GIT_ROOT" ]; then
    WORKING_DIR="$GIT_ROOT"
    echo "Git root found: $GIT_ROOT" >> "$LOG_FILE"
elif [ -d "$FIRST_PATH" ]; then
    WORKING_DIR="$FIRST_PATH"
else
    WORKING_DIR=$(dirname "$FIRST_PATH")
fi

echo "Working directory: $WORKING_DIR" >> "$LOG_FILE"
cd "$WORKING_DIR" || exit 1

# Run gc
echo "Running: gc --paths -- \"${TARGET_PATHS[@]}\"" >> "$LOG_FILE"

# Capture output and exit code
# Pass the array to the command to preserve quoting
OUTPUT=$(gc --paths -- "${TARGET_PATHS[@]}" 2>&1)
EXIT_CODE=$?

echo "Exit code: $EXIT_CODE" >> "$LOG_FILE"
echo "Output: $OUTPUT" >> "$LOG_FILE"

if [ $EXIT_CODE -eq 0 ]; then
    # Extract stats from output if possible
    # Remove [OK] prefix and ANSI color codes if any
    STATS=$(echo "$OUTPUT" | grep "Exported to" | sed 's/\[OK\] //g' | sed 's/\x1b\[[0-9;]*m//g')
    if [ -z "$STATS" ]; then
        STATS="Contents copied to clipboard successfully."
    fi
    send_notification "gc" "$STATS" "checkbox-checked-symbolic"
else
    # Show error message
    # Remove [ERROR] prefix and ANSI color codes
    ERROR_MSG=$(echo "$OUTPUT" | grep "\[ERROR\]" | head -n 1 | sed 's/\[ERROR\] //g' | sed 's/\x1b\[[0-9;]*m//g')
    if [ -z "$ERROR_MSG" ]; then
        ERROR_MSG=$(echo "$OUTPUT" | head -n 1)
    fi
    send_notification "gc" "Error: $ERROR_MSG" "error"
fi
