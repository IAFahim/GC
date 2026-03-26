#!/bin/bash

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE_PATH="$SCRIPT_DIR/Packages/com.gitcopy.gc"

if [ $# -eq 0 ]; then
    echo "Usage: $0 <path-to-unity-project>"
    echo ""
    echo "Example:"
    echo "  $0 ../MyUnityProject"
    echo "  $0 /home/user/Projects/MyGame"
    exit 1
fi

UNITY_PROJECT="$1"
TARGET_DIR="$UNITY_PROJECT/Packages/com.gitcopy.gc"

if [ ! -d "$UNITY_PROJECT/Assets" ]; then
    echo "Error: Not a valid Unity project (Assets folder not found)"
    exit 1
fi

if [ -d "$TARGET_DIR" ]; then
    echo "Removing existing GitCopy package..."
    rm -rf "$TARGET_DIR"
fi

echo "Installing GitCopy package to $TARGET_DIR..."
mkdir -p "$UNITY_PROJECT/Packages"
cp -r "$PACKAGE_PATH" "$TARGET_DIR"

echo ""
echo "✓ GitCopy package installed successfully!"
echo ""
echo "Next steps:"
echo "1. Open Unity Editor"
echo "2. The package will be automatically imported"
echo "3. Right-click in Project Window > Git Copy > Copy to Clipboard"
echo ""
echo "Note: Make sure 'gc' CLI tool is installed on your system"
echo "Install from: https://github.com/IAFahim/gc"
