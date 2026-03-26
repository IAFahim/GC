# Git Copy Sample Configuration

This directory contains sample configuration files for Git Copy in Unity projects.

## gc.json

Place this file in your Unity project root (next to Assets, Packages, ProjectSettings folders) to customize Git Copy behavior.

### Configuration Options

- **paths**: Directories to include in the output (default: ["Assets", "Packages"])
- **exclude**: Patterns to exclude (meta files, Library, Temp, etc.)
- **extension**: File extensions to include (C#, shaders, assets, etc.)
- **preset**: Use "unity" preset for Unity-specific optimizations
- **output.format**: Output format (markdown)
- **output.includeLineNumbers**: Add line numbers for better AI context
- **output.groupByDirectory**: Group files by directory structure
- **limits.maxFileSize**: Maximum file size to process

## Usage

1. Copy `gc.json` to your Unity project root
2. Customize settings based on your project needs
3. Right-click in Project Window > Git Copy > Copy to Clipboard
