#!/bin/bash

# gc (Git Copy) Installation Script
# This script downloads and installs the latest version of 'gc'

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}🚀 Installing gc (Git Copy)...${NC}"

# Detect OS
OS="$(uname -s)"
ARCH="$(uname -m)"

case "$OS" in
    Linux*)
        OS="linux"
        ;;
    Darwin*)
        OS="macos"
        ;;
    MINGW*|MSYS*|CYGWIN*)
        OS="windows"
        echo -e "${RED}Error: Please use the PowerShell install script for Windows${NC}"
        exit 1
        ;;
    *)
        echo -e "${RED}Error: Unsupported OS: $OS${NC}"
        exit 1
        ;;
esac

# Detect architecture
case "$ARCH" in
    x86_64|amd64)
        ARCH="x64"
        ;;
    aarch64|arm64)
        ARCH="arm64"
        ;;
    *)
        echo -e "${RED}Error: Unsupported architecture: $ARCH${NC}"
        exit 1
        ;;
esac

# Repository name
REPO_NAME="IAFahim/gc"

# Get latest release version
echo -e "${GREEN}Fetching latest release version...${NC}"
LATEST_VERSION=$(curl -s https://api.github.com/repos/${REPO_NAME}/releases/latest | grep '"tag_name"' | sed -E 's/.*"([^"]+)".*/\1/')

if [ -z "$LATEST_VERSION" ]; then
    echo -e "${YELLOW}Warning: Could not fetch latest version, using 'latest' tag...${NC}"
    LATEST_VERSION="latest"
fi

# Construct download URL
PLATFORM="${OS}-${ARCH}"
ARCHIVE_NAME="gc-${PLATFORM}.tar.gz"
DOWNLOAD_URL="https://github.com/${REPO_NAME}/releases/download/${LATEST_VERSION}/${ARCHIVE_NAME}"

# Create temp directory
TEMP_DIR=$(mktemp -d)
trap "rm -rf $TEMP_DIR" EXIT

# Download archive
echo -e "${GREEN}Downloading gc ${LATEST_VERSION} for ${PLATFORM}...${NC}"
if ! curl -L -o "$TEMP_DIR/${ARCHIVE_NAME}" "$DOWNLOAD_URL"; then
    echo -e "${RED}Error: Failed to download ${ARCHIVE_NAME}${NC}"
    echo -e "${YELLOW}Please download manually from: ${DOWNLOAD_URL}${NC}"
    exit 1
fi

# Extract archive
echo -e "${GREEN}Extracting archive...${NC}"
mkdir -p "$TEMP_DIR/gc_bin"
tar -xzf "$TEMP_DIR/${ARCHIVE_NAME}" -C "$TEMP_DIR/gc_bin"

# Install
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"

if [ -f "$TEMP_DIR/gc_bin/gc" ]; then
    mv "$TEMP_DIR/gc_bin/gc" "$INSTALL_DIR/gc"
    chmod +x "$INSTALL_DIR/gc"
    echo -e "${GREEN}Successfully installed gc to $INSTALL_DIR/gc${NC}"
elif [ -f "$TEMP_DIR/gc_bin/gc.exe" ]; then
    mv "$TEMP_DIR/gc_bin/gc.exe" "$INSTALL_DIR/gc"
    chmod +x "$INSTALL_DIR/gc"
    echo -e "${GREEN}Successfully installed gc to $INSTALL_DIR/gc${NC}"
else
    echo -e "${RED}Error: Could not find 'gc' binary in archive${NC}"
    ls -R "$TEMP_DIR/gc_bin"
    exit 1
fi

# Check if installation directory is in PATH
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo -e "${YELLOW}Warning: $INSTALL_DIR is not in your PATH.${NC}"
    echo -e "${YELLOW}Please add the following to your ~/.bashrc or ~/.zshrc:${NC}"
    echo -e "${YELLOW}export PATH=\"\$PATH:$INSTALL_DIR\"${NC}"
fi

echo -e "\n${GREEN}✅ Installation complete! Run 'gc --help' to get started.${NC}"
echo -e "${YELLOW}Git Copy (gc) is now ready to use.${NC}"
