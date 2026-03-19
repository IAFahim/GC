#!/bin/bash

# Test script for gc Nautilus integration
# This script simulates Nautilus environment to verify the integration script logic.

set -e

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

echo "🧪 Testing Nautilus Integration Script..."

# 1. Setup Mock Environment
TEST_DIR=$(mktemp -d)
MOCK_BIN_DIR="$TEST_DIR/bin"
mkdir -p "$MOCK_BIN_DIR"
export PATH="$MOCK_BIN_DIR:$PATH"

# Mock 'gc' command
cat > "$MOCK_BIN_DIR/gc" << 'EOF'
#!/bin/bash
echo "GC_CALLED_WITH: $@" > /tmp/gc-mock-output
echo "[OK] Exported to Clipboard: 5 files | Size: 10 KB | Tokens: ~2500"
exit 0
EOF
chmod +x "$MOCK_BIN_DIR/gc"

# Mock 'notify-send' command
cat > "$MOCK_BIN_DIR/notify-send" << 'EOF'
#!/bin/bash
echo "NOTIFY_CALLED_WITH: $@" >> /tmp/gc-mock-output
exit 0
EOF
chmod +x "$MOCK_BIN_DIR/notify-send"

# 2. Prepare Test Data
TEST_FILE_1="$TEST_DIR/file1.txt"
TEST_FOLDER_1="$TEST_DIR/folder1"
touch "$TEST_FILE_1"
mkdir -p "$TEST_FOLDER_1"

# 3. Simulate Nautilus Call
export NAUTILUS_SCRIPT_SELECTED_FILE_PATHS="$TEST_FILE_1
$TEST_FOLDER_1"

# Clear previous mock output
rm -f /tmp/gc-mock-output

# Run the actual integration script
bash ./integration/nautilus/gc-nautilus.sh

# 4. Verify Results
echo "🔍 Verifying results..."

if grep -F "GC_CALLED_WITH: --paths -- $TEST_FILE_1 $TEST_FOLDER_1" /tmp/gc-mock-output; then
    echo -e "${GREEN}✅ Success: gc was called with correct paths.${NC}"
else
    echo -e "${RED}❌ Error: gc was not called with expected paths.${NC}"
    cat /tmp/gc-mock-output
    exit 1
fi

if grep -q "NOTIFY_CALLED_WITH: gc Exported to Clipboard: 5 files | Size: 10 KB | Tokens: ~2500 -i checkbox-checked-symbolic" /tmp/gc-mock-output; then
    echo -e "${GREEN}✅ Success: notify-send was called on success.${NC}"
else
    echo -e "${RED}❌ Error: notify-send was not called.${NC}"
    cat /tmp/gc-mock-output
    exit 1
fi

# 5. Test Failure Scenario
echo "🧪 Testing Error Handling..."
cat > "$MOCK_BIN_DIR/gc" << 'EOF'
#!/bin/bash
echo "Something went wrong" >&2
exit 1
EOF

rm -f /tmp/gc-mock-output
bash ./integration/nautilus/gc-nautilus.sh

if grep -q "NOTIFY_CALLED_WITH: gc Error: Something went wrong -i error" /tmp/gc-mock-output; then
    echo -e "${GREEN}✅ Success: notify-send was called on error.${NC}"
else
    echo -e "${RED}❌ Error: notify-send was not called on error.${NC}"
    cat /tmp/gc-mock-output
    exit 1
fi

# Cleanup
rm -rf "$TEST_DIR"
rm -f /tmp/gc-mock-output

echo -e "\n${GREEN}🎉 All Nautilus integration tests passed!${NC}"
