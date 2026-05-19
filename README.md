# gc (Git Copy)

`gc` is a high-performance CLI tool that gathers your project's source code into a single, well-formatted Markdown document — optimized for LLM context windows. Built with .NET 10, compiled to a native binary for near-instant startup.

## Why gc?

- **LLM-Optimized Output**: Copy your entire codebase into a format Claude, ChatGPT, Gemini, and other LLMs can parse perfectly
- **68% Token Compression**: Combine `--brain --compress` to slash context usage by 2/3
- **99.99% Dedup**: Run `gc` twice in a session — unchanged files become 13-token references
- **Fast**: Processes thousands of files in milliseconds with parallel processing and streaming
- **Smart Filtering**: Built-in presets (web, dotnet, python, etc.) and custom glob patterns
- **Native Binary**: No runtime dependencies — one file, zero setup

## Installation

### Quick Install (Linux & macOS)

```bash
curl -sSL https://raw.githubusercontent.com/IAFahim/gc/main/install.sh | bash
```

### Windows (PowerShell)

```powershell
powershell -ExecutionPolicy Bypass -Command "iwr https://raw.githubusercontent.com/IAFahim/gc/main/install.ps1 | iex"
```

### Nautilus Integration (GNOME)

```bash
chmod +x integration/nautilus/setup.sh
./integration/nautilus/setup.sh
```

## Quick Start

```bash
# Copy all tracked files to clipboard
gc

# Copy only C# files from src/
gc --paths src --extension cs

# Exclude node_modules and tests
gc --exclude node_modules "tests/*"

# Save to a file
gc --output context.md

# Use a preset
gc --preset web
```

### Fun Keywords

`gc` supports natural-language shortcuts. These can be used interchangeably with their standard flags:

| Keyword | Flag | Purpose |
|---|---|---|
| `grab` | `--paths` | Folders to include |
| `type` | `--extension` | File extensions to include |
| `yeet` | `--exclude` | Paths or patterns to skip |
| `zap` | `--exclude-line-if-start` | Filter out specific line starts |
| `brain` | `--brain` | Activate Brain Mode |
| `compress` | `--compress` | Activate sqz compression |
| `spit` | `--output` | Save to a file |
| `horde` | `--cluster` | Enable Cluster Mode |

**Example:**
```bash
gc grab src yeet bin obj type cs brain spit context.md
```

## Compression

gc offers three levels of compression, designed to make your code as "digestible" as possible for LLMs:

### Level 1: `--brain` (Universal Minification + Dynamic BPE)

The foundation of gc's compression. It is **language-agnostic** and safe for all file types (preserves indentation for YAML/Python/etc.).

1. **Universal Minification**: Strips comments and collapses internal whitespace.
2. **Dynamic BPE Fallback**: If `sqz` is not installed, it automatically identifies high-ROI project identifiers and replaces them with single-token Unicode symbols.
3. **Structure Protection**: Never compresses file paths, headers, or project navigation.

```bash
gc --brain
```

### Level 2: `--compress` (Structural Compression via `sqz`)

Pipes output through [sqz](https://github.com/ojuschugh1/sqz) for advanced structural deduplication.

- **Session Dedup**: If you run `gc` twice, unchanged files become tiny ~13-token references.
- **Structural Awareness**: Compresses JSON, logs, and code using patterns learned from millions of lines of data.

```bash
gc --compress
```

### Level 3: `--brain --compress` (The Gold Standard)

Combines minification with structural compression for a **68%+ reduction** in token count. This is the recommended way to use `gc` for large codebases.

```bash
gc --brain --compress
```

### Compression Benchmarks

Tested on the gc codebase itself (53 C# files, 221 KB raw):

| Command | Output Size | Token Est. | Reduction |
|---|---|---|---|
| `gc` (raw) | 221 KB | ~56,500 | baseline |
| `gc --compress` | 112 KB | ~28,600 | **49%** |
| `gc --brain` | 135 KB | ~34,500 | 39% |
| `gc --brain --compress` | **72 KB** | **~18,400** | **68%** |
| `gc --brain --compress` (2nd run) | **24 B** | **~6** | **99.99%** |

> The dedup story is insane — 221 KB down to 24 bytes on repeat. Same files, same session, 6 tokens.

### LLM Comprehension

Tested with Gemini 2.0 Flash — the compressed output is fully understood:

```
[Context compressed by gc+sqz for efficiency. This contains the full source
code — references like [→L], [×N], «A» are structural markers. IMPORTANT:
When writing code or answering, use the ORIGINAL full identifiers and
patterns shown here. Do NOT use abbreviated symbols or short-form in your
output. Respond as if you received uncompressed source.]
```

This header is automatically prepended to all compressed output so LLMs respond with real code, not symbols.

## All Options

### Discovery & Filtering

| Option | Keyword | Description |
|---|---|---|
| `-p, --paths` | `grab` | Folders to include (e.g., `src libs`) |
| `-e, --extension` | `type` | File extensions to include (e.g., `js,ts`) |
| `-x, --exclude` | `yeet` | Paths or patterns to skip |
| `-z, --exclude-line-if-start` | `zap` | Filter out lines starting with these strings |
| `--preset` | - | Use predefined configurations (`dotnet`, `web`, `python`, etc.) |
| `-d, --depth` | - | Maximum directory depth |
| `-f, --force` | - | Force filesystem discovery (ignore git) |

### Output

| Option | Keyword | Description |
|---|---|---|
| `-o, --output` | `spit` | Write to a file instead of clipboard |
| `--append` | - | Append to existing clipboard/file content |
| `--no-append` | - | Don't append (default) |
| `--no-sort` | - | Disable alphabetical sorting of files |
| `-v, --verbose` | - | Enable verbose logging |
| `--debug` | - | Enable debug logging |

### Compression

| Option | Keyword | Description |
|---|---|---|
| `-b, --brain` | `brain` | Universal Minification + Dynamic BPE |
| `-c, --compress` | `compress` | Structural compression via `sqz` |
| `--no-cache` | - | Disable `sqz` session cache for a fresh run |

### Cluster Mode (Multi-Repo)

| Option | Keyword | Description |
|---|---|---|
| `--cluster` | `horde` | Enable cluster mode |
| `--cluster-dir` | - | Directory to scan for repos (default: CWD) |
| `--cluster-depth` | - | Max depth to search for git repos (default: 5) |

### Configuration & History

| Option | Description |
|---|---|
| `--history [N]` | Show history or re-run session N |
| `--init-config` | Generate a default `gc.json` |
| `--validate-config` | Check your config for errors |
| `--dump-config` | Print the current active configuration |
| `--max-memory` | Limit output size (e.g., `100MB`, `500KB`) |

## Cluster Mode

Process multiple Git repositories at once:

```bash
gc horde --cluster-dir ~/projects
gc horde --cluster-dir ~/projects type cs spit all-code.md
gc horde --cluster-dir ~/projects --cluster-depth 3 --preset dotnet
```

All filters work with cluster mode — `--extension`, `--exclude`, `--preset`, `--paths`, `--depth`, `--compress`, `--brain`.

## Performance

gc uses parallel processing and streaming to handle large repos without breaking a sweat.

View [automated benchmarks](BENCHMARK.md).

## Development

### Prerequisites
- .NET 10.0 SDK
- Git

### Build from source
```bash
dotnet publish src/gc.CLI/gc.CLI.csproj -c Release -r <your-platform-id> --self-contained -p:PublishAot=true
```

### Run tests
```bash
dotnet test tests/gc.Tests/gc.Tests.csproj --filter "FullyQualifiedName!~ReleaseBinary"
```

## License

MIT
