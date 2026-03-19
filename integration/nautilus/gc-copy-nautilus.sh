#!/bin/bash

# gc - Nautilus Context Menu Script
# This script integrates 'gc' with the Nautilus file explorer.
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

# We'll use the first selected path as the primary target
# gc handles multiple paths if passed via --paths
TARGET_PATHS=""
for path in "${SELECTED_PATHS[@]}"; do
    # Convert relative path to absolute if needed
    if [[ "$path" != /* ]]; then
        # NAUTILUS_SCRIPT_CURRENT_URI can help get the base dir
        # but usually paths are relative to the current dir where nautilus is
        path="$(pwd)/$path"
    fi
    TARGET_PATHS="$TARGET_PATHS \"$path\""
    echo "Target path: $path" >> "$LOG_FILE"
done

# Run gc
# We use eval because TARGET_PATHS has quoted paths
CMD="gc --paths $TARGET_PATHS"
echo "Running: $CMD" >> "$LOG_FILE"

# Capture output and exit code
OUTPUT=$(eval "$CMD" 2>&1)
EXIT_CODE=$?

echo "Exit code: $EXIT_CODE" >> "$LOG_FILE"
echo "Output: $OUTPUT" >> "$LOG_FILE"

if [ $EXIT_CODE -eq 0 ]; then
    # Extract stats from output if possible (e.g. "[OK] Exported to Clipboard: 10 files")
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
