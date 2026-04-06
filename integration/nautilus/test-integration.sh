#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# test-integration.sh  —  Verify the gc Nautilus integration end-to-end
#
# Simulates what Nautilus does: sets NAUTILUS_SCRIPT_SELECTED_FILE_PATHS and
# calls the script, then checks the clipboard for output.
# ─────────────────────────────────────────────────────────────────────────────

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="$SCRIPT_DIR/gc-nautilus.sh"
PASS=0
FAIL=0
SKIP=0

# ── colour helpers ────────────────────────────────────────────────────────────
GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; RESET='\033[0m'
pass() { echo -e "  ${GREEN}PASS${RESET}  $*"; (( PASS++ )); }
fail() { echo -e "  ${RED}FAIL${RESET}  $*"; (( FAIL++ )); }
skip() { echo -e "  ${YELLOW}SKIP${RESET}  $*"; (( SKIP++ )); }

# ── clipboard read helper ─────────────────────────────────────────────────────
read_clipboard() {
    if command -v wl-paste &>/dev/null;  then wl-paste 2>/dev/null
    elif command -v xclip &>/dev/null;   then xclip -selection clipboard -o 2>/dev/null
    elif command -v xsel &>/dev/null;    then xsel --clipboard --output 2>/dev/null
    else echo ""; fi
}

save_clipboard() {
    # Save current clipboard state so we can restore it after tests
    SAVED_CLIPBOARD=$(read_clipboard) || true
}

restore_clipboard() {
    if [[ -n "${SAVED_CLIPBOARD:-}" ]]; then
        if command -v wl-copy &>/dev/null; then
            printf '%s' "$SAVED_CLIPBOARD" | wl-copy 2>/dev/null || true
        elif command -v xclip &>/dev/null; then
            printf '%s' "$SAVED_CLIPBOARD" | xclip -selection clipboard 2>/dev/null || true
        fi
    fi
}

echo "=== gc Nautilus Integration Test Suite ==="
echo

# ── Test 0: pre-flight ────────────────────────────────────────────────────────
echo "── Pre-flight checks ──"

if [[ -f "$SCRIPT" ]]; then
    pass "gc-nautilus.sh exists"
else
    fail "gc-nautilus.sh not found at $SCRIPT"
    exit 1
fi

if [[ -x "$SCRIPT" ]]; then
    pass "gc-nautilus.sh is executable"
else
    chmod +x "$SCRIPT"
    pass "gc-nautilus.sh made executable"
fi

if command -v gc &>/dev/null; then
    pass "gc CLI is in PATH ($(command -v gc))"
else
    skip "gc CLI not found in PATH — remaining tests will be skipped"
    echo
    echo "  Install gc first:"
    echo "    dotnet build -c Release"
    echo "    cp src/gc.CLI/bin/Release/net10.0/gc ~/.local/bin/gc"
    echo
    echo "══════════════════════════════════"
    echo "  Results: ${PASS} passed, ${FAIL} failed, ${SKIP} skipped"
    echo "══════════════════════════════════"
    exit 0
fi

# Check clipboard tool
HAS_CLIPBOARD=false
for tool in wl-copy xclip xsel; do
    if command -v "$tool" &>/dev/null; then
        HAS_CLIPBOARD=true
        pass "Clipboard tool found: $tool"
        break
    fi
done

if ! $HAS_CLIPBOARD; then
    skip "No clipboard tool — clipboard tests will be limited"
fi

echo

# ── Set up a temp workspace ───────────────────────────────────────────────────
TMPWORK=$(mktemp -d --suffix="-gc-test")
trap 'rm -rf "$TMPWORK"; restore_clipboard' EXIT

# Create sample files
mkdir -p "$TMPWORK/project-a/src"
mkdir -p "$TMPWORK/project-b/lib"
mkdir -p "$TMPWORK/standalone"

cat > "$TMPWORK/project-a/src/hello.cs" <<'EOF'
public class Hello
{
    public static void Main() => System.Console.WriteLine("Hello, world!");
}
EOF

cat > "$TMPWORK/project-a/src/util.cs" <<'EOF'
public static class Util
{
    public static string Greet(string name) => $"Hello, {name}!";
}
EOF

cat > "$TMPWORK/project-a/README.md" <<'EOF'
# Project A
A test project.
EOF

cat > "$TMPWORK/project-b/lib/core.rs" <<'EOF'
fn main() {
    println!("Hello from Rust!");
}
EOF

cat > "$TMPWORK/standalone/config.yaml" <<'EOF'
name: standalone
version: 1.0
EOF

# Initialize git repos (gc auto-discovers via git)
(cd "$TMPWORK/project-a" && git init && git add -A && git commit -m "init" --author="test <test@test.com>" 2>/dev/null)
(cd "$TMPWORK/project-b" && git init && git add -A && git commit -m "init" --author="test <test@test.com>" 2>/dev/null)

# Save clipboard before tests
save_clipboard

# ── Test 1: single file ───────────────────────────────────────────────────────
echo "── Test 1: single file selection ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="$TMPWORK/project-a/src/hello.cs"
unset GC_NAUTILUS_DEBUG
if "$SCRIPT" 2>/dev/null; then
    if $HAS_CLIPBOARD; then
        CLIP=$(read_clipboard)
        if echo "$CLIP" | grep -q "hello.cs"; then
            pass "Single file: clipboard contains 'hello.cs'"
        else
            fail "Single file: 'hello.cs' not found in clipboard output"
        fi
    else
        pass "Single file: script exited successfully (clipboard not verified)"
    fi
else
    fail "Single file: script exited with error (rc=$?)"
fi
echo

# ── Test 2: multiple files ────────────────────────────────────────────────────
echo "── Test 2: multiple file selection ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="$TMPWORK/project-a/src/hello.cs
$TMPWORK/project-a/src/util.cs"
if "$SCRIPT" 2>/dev/null; then
    if $HAS_CLIPBOARD; then
        CLIP=$(read_clipboard)
        FOUND_HELLO=false; FOUND_UTIL=false
        echo "$CLIP" | grep -q "hello.cs" && FOUND_HELLO=true
        echo "$CLIP" | grep -q "util.cs"  && FOUND_UTIL=true
        $FOUND_HELLO && $FOUND_UTIL \
            && pass "Multiple files: both hello.cs and util.cs in clipboard" \
            || fail "Multiple files: missing file(s) in clipboard (hello=$FOUND_HELLO util=$FOUND_UTIL)"
    else
        pass "Multiple files: script exited successfully (clipboard not verified)"
    fi
else
    fail "Multiple files: script exited with error"
fi
echo

# ── Test 3: single directory ──────────────────────────────────────────────────
echo "── Test 3: single directory selection ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="$TMPWORK/project-a/src"
if "$SCRIPT" 2>/dev/null; then
    if $HAS_CLIPBOARD; then
        CLIP=$(read_clipboard)
        if echo "$CLIP" | grep -q "\.cs"; then
            pass "Single directory: .cs files found in clipboard"
        else
            fail "Single directory: no .cs files found in clipboard"
        fi
    else
        pass "Single directory: script exited successfully"
    fi
else
    fail "Single directory: script exited with error"
fi
echo

# ── Test 4: mixed files and directory ────────────────────────────────────────
echo "── Test 4: mixed file + directory selection ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="$TMPWORK/project-a/README.md
$TMPWORK/project-a/src"
if "$SCRIPT" 2>/dev/null; then
    if $HAS_CLIPBOARD; then
        CLIP=$(read_clipboard)
        FOUND_MD=false; FOUND_CS=false
        echo "$CLIP" | grep -q "README" && FOUND_MD=true
        echo "$CLIP" | grep -q "\.cs"   && FOUND_CS=true
        $FOUND_MD && $FOUND_CS \
            && pass "Mixed: both README and .cs files in clipboard" \
            || fail "Mixed: clipboard missing content (md=$FOUND_MD cs=$FOUND_CS)"
    else
        pass "Mixed: script exited successfully"
    fi
else
    fail "Mixed: script exited with error"
fi
echo

# ── Test 5: empty selection ───────────────────────────────────────────────────
echo "── Test 5: empty selection (should fail gracefully) ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS=""
if "$SCRIPT" 2>/dev/null; then
    fail "Empty selection: should have exited with error, but exited 0"
else
    pass "Empty selection: script correctly reported an error"
fi
echo

# ── Test 6: non-existent path ─────────────────────────────────────────────────
echo "── Test 6: non-existent path ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="/nonexistent/path/that/does/not/exist"
if "$SCRIPT" 2>/dev/null; then
    fail "Non-existent path: should have exited with error"
else
    pass "Non-existent path: script correctly reported an error"
fi
echo

# ── Test 7: multiple directories (cluster mode) ──────────────────────────────
echo "── Test 7: multiple directories (cluster mode) ──"
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="$TMPWORK/project-a
$TMPWORK/project-b"
if "$SCRIPT" 2>/dev/null; then
    if $HAS_CLIPBOARD; then
        CLIP=$(read_clipboard)
        FOUND_CS=false; FOUND_RS=false
        echo "$CLIP" | grep -q "\.cs" && FOUND_CS=true
        echo "$CLIP" | grep -q "\.rs\|core" && FOUND_RS=true
        $FOUND_CS && $FOUND_RS \
            && pass "Multiple dirs: both C# and Rust files in clipboard" \
            || fail "Multiple dirs: missing content (cs=$FOUND_CS rs=$FOUND_RS)"
    else
        pass "Multiple dirs: script exited successfully"
    fi
else
    # This may fail if cluster mode isn't supported yet - that's a soft fail
    fail "Multiple dirs: script exited with error"
fi
echo

# ── Test 8: args-based invocation (manual testing mode) ──────────────────────
echo "── Test 8: args-based invocation ──"
unset NAUTILUS_SCRIPT_SELECTED_FILE_PATHS
if "$SCRIPT" "$TMPWORK/project-a" 2>/dev/null; then
    if $HAS_CLIPBOARD; then
        CLIP=$(read_clipboard)
        if echo "$CLIP" | grep -q "\.cs"; then
            pass "Args mode: .cs files found in clipboard"
        else
            fail "Args mode: no .cs files found"
        fi
    else
        pass "Args mode: script exited successfully"
    fi
else
    fail "Args mode: script exited with error"
fi
echo

# ── Summary ───────────────────────────────────────────────────────────────────
echo "══════════════════════════════════"
echo "  Results: ${PASS} passed, ${FAIL} failed, ${SKIP} skipped"
echo "══════════════════════════════════"

# Restore clipboard
restore_clipboard

[[ $FAIL -eq 0 ]] && exit 0 || exit 1
