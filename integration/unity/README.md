# Unity Integration for Git Copy (gc)

Unity Editor package that integrates the `gc` (Git Copy) tool with Unity's Project Window context menu.

## Quick Start

### Installation

#### Linux / macOS
```bash
./integration/unity/install-to-unity.sh /path/to/your/unity/project
```

#### Windows (PowerShell)
```powershell
.\integration\unity\install-to-unity.ps1 C:\Path\To\Your\Unity\Project
```

#### Manual Installation
1. Copy `integration/unity/Packages/com.gitcopy.gc` to your Unity project's `Packages/` folder
2. Unity will automatically detect and import the package

## Usage

After installation:

1. Open Unity Editor
2. Right-click anywhere in the **Project Window**
3. Select **Git Copy** from the context menu
4. Choose:
   - **Copy to Clipboard**: Copies formatted Markdown to clipboard
   - **Save to File**: Saves to `project_context.md` in project root

## Features

- ✅ Right-click integration in Unity's Project Window
- ✅ Automatic Unity-specific file filtering (meta, Library, Temp, etc.)
- ✅ Clipboard export for instant AI context
- ✅ File export for documentation
- ✅ Configurable via `gc.json`
- ✅ Cross-platform support (Windows, macOS, Linux)

## Configuration

Create a `gc.json` file in your Unity project root to customize behavior:

```json
{
  "paths": ["Assets", "Packages"],
  "exclude": [
    "**/*.meta",
    "**/Library/**",
    "**/Temp/**"
  ],
  "extension": ["cs", "asmdef", "json", "unity"],
  "preset": "unity"
}
```

### Sample Configuration

A sample `gc.json` is available in `Samples~/GitCopy/gc.json` within the package.

## Requirements

- Unity 2022.3 or later
- `gc` CLI tool installed on your system

### Installing gc CLI

**Linux / macOS:**
```bash
curl -sSL https://raw.githubusercontent.com/IAFahim/gc/main/install.sh | bash
```

**Windows (PowerShell):**
```powershell
powershell -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/IAFahim/gc/main/install.ps1 | iex"
```

## Package Structure

```
com.gitcopy.gc/
├── package.json           # Unity package manifest
├── README.md              # Package documentation
├── CHANGELOG.md           # Version history
├── LICENSE.md             # MIT License
├── Editor/
│   ├── GitCopyMenuItem.cs        # Context menu implementation
│   └── GitCopy.Editor.asmdef     # Assembly definition
├── Runtime/
│   └── GitCopy.asmdef            # Runtime assembly definition
└── Samples~/GitCopy/
    ├── gc.json            # Sample configuration
    └── README.md          # Sample documentation
```

## Development

### Building from Source

The Unity integration is part of the `gc` repository. To contribute:

1. Fork the repository
2. Make your changes
3. Test with your Unity project
4. Submit a pull request

## Troubleshooting

### "gc CLI tool not found" Error

The package requires the `gc` CLI tool to be installed on your system and available in PATH. Install it using the commands above.

### Menu Items Not Appearing

1. Ensure the package is in `Packages/com.gitcopy.gc/`
2. Check Unity Console for compilation errors
3. Try restarting Unity Editor

### Permission Errors (Linux/macOS)

Make sure `gc` has execute permissions:
```bash
chmod +x ~/.local/bin/gc
```

## License

MIT License - see LICENSE.md for details

## Links

- [gc Main Repository](https://github.com/IAFahim/gc)
- [Unity Package Documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest)
- [Report Issues](https://github.com/IAFahim/gc/issues)
