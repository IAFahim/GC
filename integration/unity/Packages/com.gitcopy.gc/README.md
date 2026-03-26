# Git Copy (gc) - Unity Integration

Unity Editor integration for the `gc` (Git Copy) tool. Copy your entire Unity project's source code to clipboard in a format optimized for AI/LLM consumption.

## Features

- **Right-click Integration**: Access "Git Copy" directly from Unity's Project Window context menu
- **Smart Filtering**: Automatically filters Unity-specific files (meta, temp, Library, etc.)
- **Clipboard Export**: Instantly copy formatted Markdown to clipboard
- **File Export**: Save to .md file for later use

## Installation

### Method 1: Manual Installation

1. Navigate to your Unity project's `Packages` folder
2. Copy the `com.gitcopy.gc` folder into `Packages/`
3. Unity will automatically detect and import the package

### Method 2: Git Submodule (Recommended)

```bash
cd your-unity-project/Packages
git submodule add https://github.com/IAFahim/gc.git com.gitcopy.gc
cd com.gitcopy.gc
git checkout unity-package
```

### Method 3: Package Manager

1. Open Unity Package Manager (Window > Package Manager)
2. Click the "+" button
3. Select "Add package from disk..."
4. Navigate to and select the `com.gitcopy.gc` folder

## Usage

### Right-Click in Project Window

1. Open Unity Editor
2. Right-click anywhere in the Project Window
3. Select `Git Copy > Copy to Clipboard` or `Git Copy > Save to File`

### Configuration

Create a `gc.json` file in your project root to customize behavior:

```json
{
  "paths": ["Assets", "Packages"],
  "exclude": ["**/*.meta", "**/Library/**", "**/Temp/**"],
  "extension": ["cs", "ushl", "hlsl"],
  "preset": "unity"
}
```

## Requirements

- Unity 2022.3 or later
- `gc` CLI tool installed on your system (install from https://github.com/IAFahim/gc)

## License

MIT License - see LICENSE file for details
