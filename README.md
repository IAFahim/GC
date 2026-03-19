# GC (Git Copy)

A high-performance, cross-platform CLI tool for exporting git repository contents to clipboard or file. Written in C# for maximum speed and efficiency.

## What is GC?

**GC** (Git Copy) is a native C# implementation of the popular `git-copy` tool. It scans your git repository, filters files by extension/path, and exports them as a formatted markdown document. Perfect for:

- Sharing code with AI assistants (Claude, ChatGPT, etc.)
- Code review summaries
- Documentation generation
- Project backups
- Context preservation

## Why C#?

While shell scripts work, GC's C# implementation offers significant advantages:

| Feature | Shell Script | GC (C#) |
|---------|--------------|---------|
| **Speed** | Slow (forks processes) | Fast (native execution) |
| **Memory** | High (multiple shells) | Low (efficient streaming) |
| **Cross-platform** | Inconsistent | Consistent behavior |
| **Type Safety** | None | Full type safety |
| **Error Handling** | Limited | Comprehensive exceptions |
| **Maintenance** | Hard to debug | Easy to maintain |
| **Distribution** | Complex | Single binary |

### Performance

GC can process **50,000+ files** in under 5 seconds, with memory usage limited to configurable thresholds (default: 100MB). The AOT-compiled binary starts instantly and has minimal overhead.

## Quick Start

### Installation

#### Docker (Windows, Mac, Linux)

```bash
# Using Docker Compose (recommended)
docker compose build gc
docker compose run gc --help

# Or use convenience scripts
# Windows:
.\docker-build.ps1
.\docker-run.ps1

# Mac/Linux:
./docker-build.sh
./docker-run.sh
```

See [DOCKER.md](DOCKER.md) for detailed Docker instructions.

#### Native Installation

##### Linux / macOS

```bash
# Download and install latest version
curl -sSL https://raw.githubusercontent.com/your-org/GC/main/install.sh | bash

# Or manually
wget https://github.com/your-org/GC/releases/latest/download/gc-linux-x64.tar.gz
tar -xzf gc-linux-x64.tar.gz
mv GC ~/.local/bin/git-copy
chmod +x ~/.local/bin/git-copy
```

#### Windows

```powershell
# Download and install latest version
Invoke-WebRequest -Uri https://raw.githubusercontent.com/your-org/GC/main/install.ps1 | Invoke-Expression

# Or manually download from releases
# https://github.com/your-org/GC/releases/latest
```

#### Build from Source

```bash
git clone https://github.com/your-org/GC.git
cd GC
dotnet build -c Release -r <runtime-id> --self-contained
```

### Basic Usage

```bash
# Export entire repo to clipboard
git-copy

# Export only C# files from src/
git-copy --extension cs --paths src

# Save to file instead of clipboard
git-copy --output backup.md

# Use verbose logging
git-copy --verbose
```

## Options

```
OPTIONS:
    -p, --paths <paths>        Filter by starting paths (e.g. -p src libs)
    -e, --extension <ext>      Filter by extension (e.g. -e js ts)
    -x, --exclude <path>       Exclude folder, path or pattern (e.g. -x node_modules *.md)
    --preset <name>            Use predefined preset (web, backend, dotnet, unity, etc)
    -o, --output <file>        Save output to file instead of clipboard
    --max-memory <size>        Maximum memory limit (default: 100MB, e.g., 500MB, 1GB)
    -v, --verbose              Enable verbose logging (show file-by-file progress)
    --debug                    Enable debug logging (show git commands, timing, errors)
    --test                     Run built-in test suite
    -h, --help                 Show this help message
```

## Development Setup

### Prerequisites

- .NET 10.0 SDK or later
- Git

### Building

```bash
# Build for current platform
dotnet build

# Build for specific platform
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishAot=true

# Run tests
dotnet test
```

### Project Structure

```
GC/
├── Data/                    # Data structures
│   ├── CliArguments.cs     # CLI parsing results
│   ├── FileEntry.cs        # File metadata
│   └── FileContent.cs      # File content wrapper
├── Utilities/               # Core functionality
│   ├── GitDiscoveryExtensions.cs   # Git file discovery
│   ├── FileReaderExtensions.cs     # File reading with memory limits
│   ├── FileFilterExtensions.cs     # File filtering logic
│   ├── ClipboardExtensions.cs      # Clipboard operations
│   ├── MarkdownGeneratorExtensions.cs  # Markdown generation
│   └── Logger.cs           # Structured logging
├── Program.cs              # Entry point
└── GC.csproj              # Project configuration
```

### Architecture

```
┌─────────────────────────────────────────────────────────┐
│                      CLI Input                          │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Git File Discovery                         │
│  • Uses `git ls-files` for tracked files                │
│  • Validates git installation                           │
│  • Checks repository status                             │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              File Filtering                             │
│  • Filter by extension, path, exclude patterns         │
│  • Apply presets (web, backend, etc.)                   │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              File Reading                               │
│  • Parallel file reading                                │
│  • Memory limit checking                                │
│  • Error handling & logging                             │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Markdown Generation                        │
│  • Language detection                                   │
│  • File sorting                                         │
│  • Code block formatting                                │
└────────────────────┬────────────────────────────────────┘
                     │
                     ▼
┌─────────────────────────────────────────────────────────┐
│              Output Handling                            │
│  • Clipboard (with size limits)                         │
│  • File output                                          │
│  • Statistics display                                   │
└─────────────────────────────────────────────────────────┘
```

## Testing

GC uses xUnit for unit testing. Tests cover:

- CLI parsing
- File discovery and filtering
- Markdown generation
- Error handling
- Edge cases

```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Error Handling

GC provides clear error messages for common issues:

| Error | Cause | Solution |
|-------|-------|----------|
| `Git not found` | Git not installed | Install from https://git-scm.com |
| `Not a git repository` | Run outside git repo | Run from inside a git repo |
| `No tracked files found` | Empty repository | Commit some files |
| `Memory limit exceeded` | Files too large | Use `--max-memory` to increase limit |
| `Clipboard copy failed` | No clipboard tools | Install xclip/wl-clipboard |

## Contributing

Contributions are welcome! Please follow these guidelines:

1. **Code Style**: Follow C# conventions
2. **Testing**: Add tests for new features
3. **Documentation**: Update README and comments
4. **Pull Requests**: Describe changes clearly

### Development Workflow

```bash
# 1. Fork and clone
git clone https://github.com/your-username/GC.git

# 2. Create feature branch
git checkout -b feature/my-feature

# 3. Make changes and test
dotnet build
dotnet test

# 4. Commit changes
git commit -m "Add my feature"

# 5. Push and create PR
git push origin feature/my-feature
```

## Performance Tips

- **Use filters**: `--extension` and `--paths` reduce processing time
- **Memory limits**: `--max-memory` prevents OOM on large repos
- **Verbose logging**: `--verbose` shows progress for large operations
- **File output**: Use `-o file.md` for very large outputs

## Troubleshooting

### Large Repositories

For repos with 50K+ files:

```bash
# Increase memory limit
git-copy --max-memory 1GB

# Filter to specific paths
git-copy --paths src lib

# Save to file instead of clipboard
git-copy --output repo.md
```

### Clipboard Issues

**Linux**: Install clipboard tools
```bash
# Ubuntu/Debian
sudo apt install wl-clipboard xclip

# Fedora/RHEL
sudo dnf install wl-clipboard xclip

# Arch
sudo pacman -S wl-clipboard xclip
```

**macOS**: pbcopy is pre-installed

**Windows**: PowerShell is pre-installed

## License

MIT License - see LICENSE file for details

## Acknowledgments

- Inspired by the original `git-copy` shell script
- Built with .NET 10 and AOT compilation
- Cross-platform clipboard handling

## Roadmap

- [ ] Plugin architecture
- [ ] Configuration file support
- [ ] Custom output formats
- [ ] Git submodules support
- [ ] Language server integration
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | tie: ms |
| File Read Time | tie: ms |
| Total Time | tie: ms |

*Last updated: 2026-03-19 03:08:56 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 14 ms |
| File Read Time | 22 ms |
| Total Time | 38 ms |

*Last updated: 2026-03-19 03:12:04 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 19 ms |
| File Read Time | 22 ms |
| Total Time | 44 ms |

*Last updated: 2026-03-19 04:45:11 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 18 ms |
| File Read Time | 21 ms |
| Total Time | 42 ms |

*Last updated: 2026-03-19 05:11:23 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 18 ms |
| File Read Time | 28 ms |
| Total Time | 49 ms |

*Last updated: 2026-03-19 05:25:09 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 48 ms |
| File Read Time | 25 ms |
| Total Time | 78 ms |

*Last updated: 2026-03-19 06:35:18 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 19 ms |
| File Read Time | 24 ms |
| Total Time | 46 ms |

*Last updated: 2026-03-19 07:50:08 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 18 ms |
| File Read Time | 22 ms |
| Total Time | 43 ms |

*Last updated: 2026-03-19 08:50:18 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 15 ms |
| File Read Time | 22 ms |
| Total Time | 40 ms |

*Last updated: 2026-03-19 09:51:29 UTC*
<!-- BENCHMARK_END -->
<!-- BENCHMARK_START -->
## 📊 Real Performance Data

Latest benchmark results from automated testing:

| Metric | Value |
|--------|-------|
| Discovery Time | 15 ms |
| File Read Time | 23 ms |
| Total Time | 41 ms |

*Last updated: 2026-03-19 10:45:54 UTC*
<!-- BENCHMARK_END -->
