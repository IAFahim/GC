#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# gc-nautilus.sh  —  Nautilus (GNOME Files) integration for gc (Git Copy)
#
# Installed to: ~/.local/share/nautilus/scripts/gc-nautilus
#
# Nautilus passes selected items via environment variables:
#   NAUTILUS_SCRIPT_SELECTED_FILE_PATHS  — newline-separated absolute paths
#   NAUTILUS_SCRIPT_SELECTED_URIS        — newline-separated file:// URIs
#   NAUTILUS_SCRIPT_CURRENT_URI          — URI of the open directory
#
# Handles all selection types:
#   * Single directory   → gc runs on that directory
#   * Multiple dirs      → gc --cluster runs on the parent (batch mode)
#   * Single file        → gc processes that one file via temp-dir symlink
#   * Multiple files     → gc processes all files via temp-dir symlinks
#   * Mixed              → files symlinked; dirs batched via cluster mode
# ─────────────────────────────────────────────────────────────────────────────

set -euo pipefail

readonly SCRIPT_NAME="gc-nautilus"
readonly SCRIPT_DISPLAY_NAME="Git Copy (gc)"

# ── logging helpers ──────────────────────────────────────────────────────────

log_debug()  { [[ "${GC_NAUTILUS_DEBUG:-}" == "1" ]] && echo "[DEBUG] $*" >&2 || true; }
log_info()   { echo "[INFO]  $*" >&2; }
log_error()  { echo "[ERROR] $*" >&2; }

notify() {
    local icon="$1"; shift
    local body="$*"
    if command -v notify-send &>/dev/null; then
        notify-send -i "$icon" "$SCRIPT_DISPLAY_NAME" "$body" 2>/dev/null || true
    elif command -v zenity &>/dev/null; then
        zenity --notification --text="$SCRIPT_DISPLAY_NAME: $body" 2>/dev/null &
    fi
}

die() {
    log_error "$*"
    notify "dialog-error" "$*"
    exit 1
}

# ── clipboard ────────────────────────────────────────────────────────────────

copy_file_to_clipboard() {
    local src="$1"
    if command -v wl-copy &>/dev/null; then
        wl-copy < "$src"
    elif command -v xclip &>/dev/null; then
        xclip -selection clipboard < "$src"
    elif command -v xsel &>/dev/null; then
        xsel --clipboard --input < "$src"
    else
        die "No clipboard tool found. Install wl-clipboard, xclip, or xsel."
    fi
}

# ── pre-flight ───────────────────────────────────────────────────────────────

GC_BIN=""
if command -v gc &>/dev/null; then
    GC_BIN="$(command -v gc)"
elif [[ -x "$HOME/.local/bin/gc" ]]; then
    GC_BIN="$HOME/.local/bin/gc"
else
    die "'gc' CLI not found in PATH. Install it first."
fi
log_debug "Using gc at: $GC_BIN"

# ── collect selected paths ───────────────────────────────────────────────────

# NAUTILUS_SCRIPT_SELECTED_FILE_PATHS is newline-separated with a trailing newline.
# mapfile -t drops the trailing newline naturally.
mapfile -t RAW_SELECTED < <(printf '%s' "${NAUTILUS_SCRIPT_SELECTED_FILE_PATHS:-}")

# Also accept args for manual testing (e.g. ./gc-nautilus.sh /some/dir)
if [[ ${#RAW_SELECTED[@]} -eq 0 && $# -gt 0 ]]; then
    RAW_SELECTED=("$@")
fi

# Clean: drop empty entries
PATHS=()
for p in "${RAW_SELECTED[@]+"${RAW_SELECTED[@]}"}"; do
    [[ -n "$p" ]] && PATHS+=("$p")
done

if [[ ${#PATHS[@]} -eq 0 ]]; then
    die "No items selected."
fi

log_debug "Selected items: ${#PATHS[@]}"
notify "dialog-information" "Processing ${#PATHS[@]} item(s)..."

# ── separate dirs and files ──────────────────────────────────────────────────

DIRS=()
FILES=()

for p in "${PATHS[@]}"; do
    if [[ -d "$p" ]]; then
        DIRS+=("$p")
    elif [[ -f "$p" ]]; then
        FILES+=("$p")
    else
        log_debug "Skipping non-existent path: $p"
    fi
done

log_debug "Dirs: ${#DIRS[@]}, Files: ${#FILES[@]}"

# ── temp dir for output accumulation ─────────────────────────────────────────

WORK_DIR=$(mktemp -d --suffix="-gc-nautilus")
trap 'rm -rf "$WORK_DIR"' EXIT

COMBINED="$WORK_DIR/combined.md"
touch "$COMBINED"

CHUNK_INDEX=0

# emit_chunk: run gc on a directory, write output to the next chunk file
emit_chunk() {
    local dir="$1"
    local outfile="$WORK_DIR/chunk_${CHUNK_INDEX}.md"
    if ("$GC_BIN" --output "$outfile" --no-append 2>"$WORK_DIR/chunk_${CHUNK_INDEX}.err"); then
        if [[ -s "$outfile" ]]; then
            cat "$outfile" >> "$COMBINED"
            (( CHUNK_INDEX++ )) || true
            return 0
        fi
    else
        local rc=$?
        log_error "gc failed (rc=$rc) on $dir"
        cat "$WORK_DIR/chunk_${CHUNK_INDEX}.err" >&2 || true
    fi
    return 1
}

# ── 1. Directories ───────────────────────────────────────────────────────────

if [[ ${#DIRS[@]} -gt 0 ]]; then

    if [[ ${#DIRS[@]} -eq 1 ]]; then
        # Single directory: run gc directly on it
        log_debug "Single directory mode: ${DIRS[0]}"
        ( cd "${DIRS[0]}" && emit_chunk "." )

    else
        # Multiple directories: use cluster mode if available,
        # otherwise fall back to sequential per-dir processing
        log_debug "Multiple directories: ${#DIRS[@]} dirs"

        # Find common parent
        COMMON_PARENT="$(dirname "${DIRS[0]}")"
        all_share_parent=true
        for d in "${DIRS[@]}"; do
            if [[ "$(dirname "$d")" != "$COMMON_PARENT" ]]; then
                all_share_parent=false
                break
            fi
        done

        if $all_share_parent && "$GC_BIN" --help 2>&1 | grep -q -- '--cluster'; then
            # Use cluster mode — gc handles all repos in one pass
            log_debug "Using cluster mode on parent: $COMMON_PARENT"
            outfile="$WORK_DIR/chunk_cluster.md"
            # Build --paths args pointing to each selected subdirectory
            path_args=()
            for d in "${DIRS[@]}"; do
                path_args+=(-p "$(basename "$d")")
            done
            if ("$GC_BIN" --cluster --cluster-dir "$COMMON_PARENT" \
                 "${path_args[@]}" \
                 --output "$outfile" --no-append \
                 2>"$WORK_DIR/chunk_cluster.err"); then
                if [[ -s "$outfile" ]]; then
                    cat "$outfile" >> "$COMBINED"
                    CHUNK_INDEX=$(( CHUNK_INDEX + 1 ))
                fi
            else
                log_error "Cluster mode failed, falling back to sequential"
                cat "$WORK_DIR/chunk_cluster.err" >&2 || true
                # Fallback: process each dir separately
                for d in "${DIRS[@]}"; do
                    ( cd "$d" && emit_chunk "." ) || true
                done
            fi
        else
            # No cluster mode available or dirs not in same parent: sequential
            log_debug "Sequential directory mode"
            for d in "${DIRS[@]}"; do
                ( cd "$d" && emit_chunk "." ) || true
            done
        fi
    fi
fi

# ── 2. Files — symlink into a temp dir ───────────────────────────────────────

if [[ ${#FILES[@]} -gt 0 ]]; then
    log_debug "Processing ${#FILES[@]} file(s) via symlink dir"
    FILE_DIR="$WORK_DIR/files"
    mkdir -p "$FILE_DIR"

    for f in "${FILES[@]}"; do
        base="$(basename "$f")"
        dest="$FILE_DIR/$base"
        counter=1
        # Handle duplicate filenames
        while [[ -e "$dest" ]]; do
            ext="${base##*.}"
            name="${base%.*}"
            if [[ "$ext" != "$base" ]]; then
                dest="$FILE_DIR/${name}_${counter}.${ext}"
            else
                dest="$FILE_DIR/${base}_${counter}"
            fi
            (( counter++ )) || true
        done
        ln -sf "$f" "$dest"
    done

    # -f forces filesystem discovery (no git ls-files needed for the temp dir)
    ( cd "$FILE_DIR" && emit_chunk "." )
fi

# ── copy combined output to clipboard ────────────────────────────────────────

if [[ ! -s "$COMBINED" ]]; then
    die "gc produced no output. Files may be empty, binary, or all ignored."
fi

copy_file_to_clipboard "$COMBINED"

# ── notification ─────────────────────────────────────────────────────────────

TOTAL=$(( ${#DIRS[@]} + ${#FILES[@]} ))
BYTES=$(wc -c < "$COMBINED" | tr -d ' ')
LINES=$(wc -l < "$COMBINED" | tr -d ' ')

if [[ ${#DIRS[@]} -gt 0 && ${#FILES[@]} -gt 0 ]]; then
    DETAIL="${#DIRS[@]} dir(s) + ${#FILES[@]} file(s)"
elif [[ ${#DIRS[@]} -gt 1 ]]; then
    DETAIL="${#DIRS[@]} directories"
elif [[ ${#DIRS[@]} -eq 1 ]]; then
    DETAIL="1 directory"
elif [[ ${#FILES[@]} -gt 1 ]]; then
    DETAIL="${#FILES[@]} files"
else
    DETAIL="1 file"
fi

notify "dialog-information" "$DETAIL copied to clipboard ($BYTES bytes, ~$((BYTES/4)) tokens)"
log_info "Done: $DETAIL | $BYTES bytes | ~$((BYTES/4)) tokens"
