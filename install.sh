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
INSTALL_DIR="$HOME/.local/bin"
mkdir -p "$INSTALL_DIR"

# Try extraction with --strip-components=1 first
EXTRACT_SUCCESS=false
EXTRACT_TEMP_DIR=$(mktemp -d "$TEMP_DIR/gc_extract_XXXXXX")

if tar -xzf "$TEMP_DIR/${ARCHIVE_NAME}" -C "$EXTRACT_TEMP_DIR" --strip-components=1 2>/dev/null; then
    # Check if binary exists at root level
    if [ -f "$EXTRACT_TEMP_DIR/gc" ] || [ -f "$EXTRACT_TEMP_DIR/gc.exe" ]; then
        EXTRACT_SUCCESS=true
    fi
fi

# If strip-components failed or binary not found, try without it
if [ "$EXTRACT_SUCCESS" = false ]; then
    # Clean and retry without strip-components
    rm -rf "$EXTRACT_TEMP_DIR"
    EXTRACT_TEMP_DIR=$(mktemp -d "$TEMP_DIR/gc_extract_XXXXXX")

    if tar -xzf "$TEMP_DIR/${ARCHIVE_NAME}" -C "$EXTRACT_TEMP_DIR" 2>/dev/null; then
        # Find the actual gc binary (not text files like README_gc.txt)
        # Search for exact filename match and validate it's an executable binary
        GC_BINARY=""
        for candidate in $(find "$EXTRACT_TEMP_DIR" -type f \( -name "gc" -o -name "gc.exe" \)); do
            # Check if file is executable binary (ELF for Linux, Mach-O for macOS, PE for Windows)
            if file "$candidate" | grep -qE '(ELF|Mach-O|PE32|PE32\+|executable)'; then
                # Verify it's not a text file
                if ! file "$candidate" | grep -q 'text'; then
                    GC_BINARY="$candidate"
                    break
                fi
            fi
        done

        if [ -n "$GC_BINARY" ]; then
            # Move binary to a known location
            mkdir -p "$EXTRACT_TEMP_DIR/final"
            cp "$GC_BINARY" "$EXTRACT_TEMP_DIR/final/gc"
            EXTRACT_SUCCESS=true
        fi
    fi
fi

# Install binary
if [ "$EXTRACT_SUCCESS" = true ]; then
    # Find the actual binary location
    if [ -f "$EXTRACT_TEMP_DIR/gc" ]; then
        BINARY_PATH="$EXTRACT_TEMP_DIR/gc"
    elif [ -f "$EXTRACT_TEMP_DIR/gc.exe" ]; then
        BINARY_PATH="$EXTRACT_TEMP_DIR/gc.exe"
    elif [ -f "$EXTRACT_TEMP_DIR/final/gc" ]; then
        BINARY_PATH="$EXTRACT_TEMP_DIR/final/gc"
    else
        echo -e "${RED}Error: Could not find 'gc' binary after extraction${NC}"
        ls -R "$EXTRACT_TEMP_DIR"
        exit 1
    fi

    # Move and set permissions
    mv "$BINARY_PATH" "$INSTALL_DIR/gc"
    chmod +x "$INSTALL_DIR/gc"
    echo -e "${GREEN}Successfully installed gc to $INSTALL_DIR/gc${NC}"
else
    echo -e "${RED}Error: Failed to extract archive${NC}"
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
