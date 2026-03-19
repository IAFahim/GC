#!/bin/bash

# gc - Nautilus Integration Setup Script
# This script automates the installation of 'gc' in Nautilus.

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${YELLOW}🚀 Setting up 'gc' Nautilus integration...${NC}"

# Check if gc is installed
if ! command -v gc &> /dev/null; then
    echo -e "${RED}❌ Error: 'gc' is not installed or not in your PATH.${NC}"
    echo -e "Please install gc first (e.g., using the main install.sh script)."
    exit 1
fi

# Locate the integration script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_SCRIPT="$SCRIPT_DIR/gc-nautilus.sh"

if [ ! -f "$SOURCE_SCRIPT" ]; then
    echo -e "${RED}❌ Error: Could not find 'gc-nautilus.sh' at $SOURCE_SCRIPT.${NC}"
    exit 1
fi

# Ensure it's executable
chmod +x "$SOURCE_SCRIPT"

# Target location for Nautilus scripts
NAUTILUS_SCRIPTS_DIR="$HOME/.local/share/nautilus/scripts"
TARGET_LINK="$NAUTILUS_SCRIPTS_DIR/gc"

# Create directory if it doesn't exist
mkdir -p "$NAUTILUS_SCRIPTS_DIR"

# Create symbolic link
if [ -L "$TARGET_LINK" ] || [ -f "$TARGET_LINK" ]; then
    echo -e "${YELLOW}⚠️  Existing link/file found at $TARGET_LINK. Overwriting...${NC}"
    rm -f "$TARGET_LINK"
fi

ln -s "$SOURCE_SCRIPT" "$TARGET_LINK"

echo -e "${GREEN}✅ Success: 'gc' is now integrated with Nautilus!${NC}"
echo -e "\n${YELLOW}Usage:${NC}"
echo -e "1. Open Nautilus."
echo -e "2. Right-click on any folder or file."
echo -e "3. Select 'Scripts' -> 'gc'."
echo -e "\nIf the 'Scripts' menu doesn't appear immediately, try restarting Nautilus with 'nautilus -q'."
