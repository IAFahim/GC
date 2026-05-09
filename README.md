# gc (Git Copy)

`gc` is a high-performance CLI tool designed to consolidate your project's source code into a single, well-formatted Markdown document. It's built with .NET 10 and compiled to a native binary for near-instant startup and maximum efficiency.

## Why use `gc`?

- **AI-Ready Context**: Quickly copy your entire project (or specific parts) into a format that LLMs (Claude, ChatGPT, etc.) can easily parse.
- **Fast & Lightweight**: Processes thousands of files in milliseconds with minimal memory overhead.
- **Smart Filtering**: Built-in support for presets (web, dotnet, python, etc.) and custom glob patterns.
- **Native Execution**: Distributed as a standalone binary—no runtime dependencies required.

## Installation

### Quick Install (Linux & macOS)

```bash
curl -sSL https://raw.githubusercontent.com/IAFahim/gc/main/install.sh | bash
```

### Windows (PowerShell)

```powershell
powershell -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/IAFahim/gc/main/install.ps1 | iex"
```

**Note**: The `-ExecutionPolicy Bypass` flag ensures the script runs even if your default execution policy restricts script execution.

### Nautilus Integration (GNOME)

To add `gc` to your right-click "Scripts" menu in Nautilus:

```bash
chmod +x integration/nautilus/setup.sh
./integration/nautilus/setup.sh
```

## Usage

Run `gc` from any Git repository root. By default, it copies the formatted Markdown to your clipboard.

```bash
# Copy all tracked files to clipboard
gc

# Copy only C# and Markdown files from specific folders
gc --paths src libs --extension cs md

# Exclude specific directories or patterns
gc --exclude node_modules "tests/*"

# Use a preset for common project types
gc --preset web

# Save to a file instead of the clipboard
gc --output project_context.md
```

### Common Options

| Option | Description |
|--------|-------------|
| `-p, --paths` | Folders to include (e.g., `src libs`) |
| `-e, --extension` | File extensions to include (e.g., `js ts`) |
| `-x, --exclude` | Paths or patterns to skip |
| `--exclude-line-if-start` | Filter out specific lines that start with these strings (e.g., `//`, `\n`) |
| `--preset` | Use predefined configurations (`dotnet`, `web`, `python`, etc.) |
| `-o, --output` | Write to a file instead of the clipboard |
| `--no-append` | Do not append to the current clipboard/file content (appends by default) |
| `-d, --depth` | Maximum directory depth to penetrate |
| `-f, --force` | Force filesystem discovery (ignore git) |
| `-v, --verbose` | Enable verbose logging |
| `--cluster` | Enable cluster mode for multi-repo processing |
| `--cluster-dir` | Directory containing multiple git repos (default: current dir) |
| `--cluster-depth` | Max depth to search for git repos (default: 5) |

## Cluster Mode

Cluster mode lets you process multiple Git repositories at once by pointing `gc` at a parent directory containing several repos. It discovers each `.git`-backed subdirectory and consolidates all their source files into a single Markdown document.

```bash
# Process all repos in ~/projects and copy to clipboard
gc --cluster --cluster-dir ~/projects

# Process all repos, filtering to C# files only, and save to a file
gc --cluster --cluster-dir ~/projects --extension cs --output all-code.md

# Limit how deep gc searches for git repos
gc --cluster --cluster-dir ~/projects --cluster-depth 3 --preset dotnet
```

### How it works

1. `gc` scans `--cluster-dir` (or the current directory) for subdirectories containing a `.git` folder, up to `--cluster-depth` levels deep (default: 5).
2. Each discovered repo is processed independently, respecting its own `.gitignore`.
3. All results are merged into a single output (clipboard or file).

All existing filters work with cluster mode -- including `--extension`, `--exclude`, `--preset`, `--paths`, `--depth`, and `--exclude-line-if-start`.

## Brain Mode

> **Deprecated** -- use `--compress` instead. Brain Mode will be removed in a future release.

Brain Mode compresses source code to reduce LLM token usage. It replaces long, repeated identifiers (not short keywords) with short symbols, and prepends a dictionary header so any LLM can decode the output.

**Example:**
```
# DICT
_A=ConfigurationValidator
_B=IFileDiscoveryService

_A _B _service = new _A(_B);
```

Brain Mode v2 uses dynamic analysis -- it scans your project to find the identifiers that save the most tokens, rather than relying on hardcoded keyword maps. See [docs/brain-mode-v2-dynamic-compression.md](docs/brain-mode-v2-dynamic-compression.md) for architecture details.

## Compression with sqz (replaces Brain Mode)

`gc --compress` pipes output through [sqz](https://github.com/ojuschugh1/sqz) before copying to your clipboard or writing to a file. sqz provides structural compression and session-aware deduplication.

Install sqz first:
```bash
curl -fsSL https://raw.githubusercontent.com/ojuschugh1/sqz/main/install.sh | sh
```

Then:
```bash
gc --compress                  # structural compression + session dedup
gc --compress --no-cache       # compress without dedup (fresh output)
gc src/MyService.cs --compress # compress a single file
gc --paths src --extension cs --compress --output context.md
```

**Why sqz instead of Brain Mode?**
- sqz understands *content type* -- it compresses JSON differently from logs, differently from code
- Session deduplication: if you run `gc --compress` twice, the second run sends ~13-token references for any file that hasn't changed
- Reversible via `sqz expand`
- Safe mode with entropy detection for secrets
- Improves independently as a separate project

You can combine `--compress` with `--brain` for maximum compression (Brain Mode runs first, then sqz).

## Performance

`gc` is designed for speed. It uses parallel processing and streaming to handle even the largest repositories without breaking a sweat.

View our [latest automated benchmarks](BENCHMARK.md).

## Development

### Prerequisites
- .NET 10.0 SDK
- Git

### Build from source
```bash
# Build native AOT binary
dotnet publish src/gc.CLI/gc.CLI.csproj -c Release -r <your-platform-id> --self-contained -p:PublishAot=true
```

## License
MIT
