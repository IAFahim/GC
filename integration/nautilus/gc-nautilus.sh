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

# Nautilus passes selected paths via environment variable
if [ -z "$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS" ]; then
    notify-send "gc" "No files or folders selected." -i info
    echo "No paths selected" >> "$LOG_FILE"
    exit 0
fi

# Prepare paths array
TARGET_PATHS=()
# Process NAUTILUS_SCRIPT_SELECTED_FILE_PATHS line by line
while IFS= read -r path; do
    if [ -n "$path" ]; then
        # The paths might be absolute or relative depending on Nautilus version/context
        if [[ "$path" != /* ]]; then
            path="$(pwd)/$path"
        fi
        TARGET_PATHS+=("$path")
        echo "Target path: $path" >> "$LOG_FILE"
    fi
done <<< "$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS"

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
