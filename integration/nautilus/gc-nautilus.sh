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

# Run gc
echo "Running: gc --paths ${TARGET_PATHS[@]}" >> "$LOG_FILE"

# Capture output and exit code
# Pass the array to the command to preserve quoting
OUTPUT=$(gc --paths "${TARGET_PATHS[@]}" 2>&1)
EXIT_CODE=$?

echo "Exit code: $EXIT_CODE" >> "$LOG_FILE"
echo "Output: $OUTPUT" >> "$LOG_FILE"

if [ $EXIT_CODE -eq 0 ]; then
    # Extract stats from output if possible
    STATS=$(echo "$OUTPUT" | grep "Exported to")
    if [ -z "$STATS" ]; then
        STATS="Contents copied to clipboard successfully."
    fi
    notify-send "gc - Success" "$STATS" -i checkbox-checked-symbolic
else
    # Show error message
    ERROR_MSG=$(echo "$OUTPUT" | head -n 3)
    notify-send "gc - Error" "Failed to copy: $ERROR_MSG" -i error
fi
