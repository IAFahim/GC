#!/bin/bash

# gc - Nautilus Context Menu Script
# This script integrates 'gc' (Git Copy) with the Nautilus file explorer.
# It allows right-clicking on folders/files to copy their contents as markdown.

# Dependencies:
# - gc (must be in PATH)
# - libnotify-bin (for notify-send, optional)
# - xclip or wl-clipboard (for clipboard support)

# Log file for debugging
LOG_FILE="/tmp/gc-nautilus.log"
echo "--- $(date) ---" > "$LOG_FILE"

# Nautilus passes selected paths via NAUTILUS_SCRIPT_SELECTED_FILE_PATHS
# It is newline-delimited.
IFS=$'\n'
SELECTED_PATHS=($NAUTILUS_SCRIPT_SELECTED_FILE_PATHS)

if [ ${#SELECTED_PATHS[@]} -eq 0 ]; then
    notify-send "gc" "No files or folders selected." -i info
    echo "No paths selected" >> "$LOG_FILE"
    exit 0
fi

# Prepare paths array
TARGET_PATHS=()
for path in "${SELECTED_PATHS[@]}"; do
    # Convert relative path to absolute if needed
    if [[ "$path" != /* ]]; then
        path="$(pwd)/$path"
    fi
    TARGET_PATHS+=("$path")
    echo "Target path: $path" >> "$LOG_FILE"
done

# Set working directory to the parent of the first selected item
# This helps 'gc' find the git root if it exists
FIRST_PATH="${TARGET_PATHS[0]}"
if [ -d "$FIRST_PATH" ]; then
    WORKING_DIR="$FIRST_PATH"
else
    WORKING_DIR=$(dirname "$FIRST_PATH")
fi

echo "Working directory: $WORKING_DIR" >> "$LOG_FILE"
cd "$WORKING_DIR" || exit 1

# Run gc
echo "Running: gc --paths \"${TARGET_PATHS[@]}\"" >> "$LOG_FILE"

# Capture output and exit code
# Pass the array to the command to preserve quoting
OUTPUT=$(gc --paths "${TARGET_PATHS[@]}" 2>&1)
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
    notify-send "gc" "$STATS" -i checkbox-checked-symbolic
else
    # Show error message
    # Remove [ERROR] prefix and ANSI color codes
    ERROR_MSG=$(echo "$OUTPUT" | grep "\[ERROR\]" | head -n 1 | sed 's/\[ERROR\] //g' | sed 's/\x1b\[[0-9;]*m//g')
    if [ -z "$ERROR_MSG" ]; then
        ERROR_MSG=$(echo "$OUTPUT" | head -n 1)
    fi
    notify-send "gc" "Error: $ERROR_MSG" -i error
fi
