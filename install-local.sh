#!/bin/bash

# gc (Git Copy) Local Installation Script
# This script installs the locally built version of 'gc'

set -e

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}🔧 Installing locally built gc (Git Copy)...${NC}"

# Install directory
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"

# Publish as a single-file executable
echo -e "${GREEN}Publishing gc as single-file executable...${NC}"
dotnet publish ./src/gc.CLI/gc.CLI.csproj -c Release -o "$INSTALL_DIR" \
    -p:PublishSingleFile=true \
    --runtime linux-x64 \
    --self-contained true

if [ ! -f "$INSTALL_DIR/gc" ]; then
    echo -e "${RED}Error: Failed to publish gc binary${NC}"
    exit 1
fi

chmod +x "$INSTALL_DIR/gc"

echo -e "${GREEN}Successfully installed locally built gc to $INSTALL_DIR/gc${NC}"

# Check if installation directory is in PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo -e "${YELLOW}Warning: $INSTALL_DIR is not in your PATH.${NC}"
    echo -e "${YELLOW}Please add the following to your ~/.bashrc or ~/.zshrc:${NC}"
    echo -e "${YELLOW}export PATH=\"\$PATH:$INSTALL_DIR\"${NC}"
fi

echo -e "\n${GREEN}✅ Local installation complete! Run 'gc --help' to get started.${NC}"
