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
| `--preset` | Use predefined configurations (`dotnet`, `web`, `python`, etc.) |
| `-o, --output` | Write to a file instead of the clipboard |
| `--discovery` | Force discovery mode (`git`, `filesystem`, or `auto`) |
| `-v, --verbose` | Show file-by-file progress |

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
dotnet publish gc/gc.csproj -c Release -r <your-platform-id> --self-contained -p:PublishAot=true
```

## License
MIT
