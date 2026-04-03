#!/bin/bash

# gc (Git Copy) Installation Script
# This script downloads and installs the latest version of 'gc'

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Print welcome banner
cat << 'EOF'

╔════════════════════════════════════════════════════════════╗
║         gc (Git Copy) - Installer                         ║
║         Generate AI-ready markdown from codebases          ║
╚════════════════════════════════════════════════════════════╝

🚀 Starting installation process...

EOF

echo -e "${GREEN}[1/7]${NC} Detecting system configuration..."

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
echo -e "${GREEN}[2/7]${NC} Fetching latest release version..."
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
echo -e "${GREEN}[3/7]${NC} Downloading gc ${LATEST_VERSION} for ${PLATFORM}..."
if ! curl -L -o "$TEMP_DIR/${ARCHIVE_NAME}" "$DOWNLOAD_URL"; then
    echo -e "${RED}Error: Failed to download ${ARCHIVE_NAME}${NC}"
    echo -e "${YELLOW}Please download manually from: ${DOWNLOAD_URL}${NC}"
    exit 1
fi

# Download and verify checksum
CHECKSUM_URL="https://github.com/${REPO_NAME}/releases/download/${LATEST_VERSION}/checksums.txt"
echo -e "${GREEN}[4/7]${NC} Downloading checksums for verification..."
if ! curl -fL -s -o "$TEMP_DIR/checksums.txt" "$CHECKSUM_URL"; then
    echo -e "${YELLOW}Warning: Checksums not available for version ${LATEST_VERSION}. Skipping verification.${NC}"
else
    # Verify checksum
    echo -e "${GREEN}[5/7]${NC} Verifying checksum integrity..."
    cd "$TEMP_DIR"
    
    CHECKSUM_SUCCESS=false
    if command -v sha256sum >/dev/null 2>&1; then
        if sha256sum -c checksums.txt --ignore-missing 2>/dev/null | grep -q "${ARCHIVE_NAME}: OK"; then
            CHECKSUM_SUCCESS=true
        fi
    elif command -v shasum >/dev/null 2>&1; then
        if shasum -a 256 -c checksums.txt --ignore-missing 2>/dev/null | grep -q "${ARCHIVE_NAME}: OK"; then
            CHECKSUM_SUCCESS=true
        fi
    else
        echo -e "${YELLOW}Warning: Neither sha256sum nor shasum found. Skipping checksum verification.${NC}"
        CHECKSUM_SUCCESS=true
    fi

    if [ "$CHECKSUM_SUCCESS" = false ]; then
        echo -e "${RED}Error: Checksum verification failed!${NC}"
        echo -e "${RED}Security: Binary integrity check failed. Aborting installation.${NC}"
        echo -e "${YELLOW}This could indicate a corrupted download or potential security issue.${NC}"
        exit 1
    fi
    echo -e "${GREEN}Checksum verified successfully${NC}"
    cd - > /dev/null
fi

# Extract archive
echo -e "${GREEN}[6/7]${NC} Extracting archive and installing binary..."
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
        # Search for exact filename match and validate it has execute permissions
        GC_BINARY=""
        for candidate in $(find "$EXTRACT_TEMP_DIR" -type f \( -name "gc" -o -name "gc.exe" \)); do
            # Check if file has execute permission
            if [ -x "$candidate" ]; then
                GC_BINARY="$candidate"
                break
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
echo -e "${GREEN}[7/7]${NC} Verifying installation..."

if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    cat << 'EOF'

⚠️  PATH Configuration Required

The installation directory is not in your PATH. To use gc from anywhere,
add the following to your shell configuration file:

For Bash (~/.bashrc):
    export PATH="$PATH:$HOME/.local/bin"

For Zsh (~/.zshrc):
    export PATH="$PATH:$HOME/.local/bin"

For Fish (~/.config/fish/config.fish):
    fish_add_path ~/.local/bin

After adding, restart your terminal or run: source ~/.bashrc (or ~/.zshrc)

EOF
else
    echo -e "${GREEN}✓${NC} Installation directory is in PATH"
fi

# Print success message
cat << 'EOF'

╔════════════════════════════════════════════════════════════╗
║              Installation Complete! ✅                      ║
╚════════════════════════════════════════════════════════════╝

🎉 gc (Git Copy) has been successfully installed!

QUICK START:
    gc                      # Copy current directory to clipboard
    gc --output repo.md     # Save to file instead of clipboard
    gc --help               # Show all options

EXAMPLES:
    gc src/                 # Only include src/ directory
    gc -e .cs,.ts           # Only include .cs and .ts files
    gc -x node_modules/     # Exclude node_modules/
    gc --compact            # Reduce token usage (compact mode)

FEATURES:
    • Clipboard integration (auto-detects supported platforms)
    • File filters (include/exclude patterns)
    • Git-aware file discovery
    • Compact mode for token optimization
    • Append mode for incremental updates

DOCUMENTATION:
    GitHub: https://github.com/IAFahim/gc
    Issues: https://github.com/IAFahim/gc/issues

EOF

echo -e "${GREEN}Installation successful! Run 'gc --help' to see all options.${NC}\n"
