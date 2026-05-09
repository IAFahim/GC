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

gc supports natural-language shortcuts:

```bash
gc grab src yeet bin obj yeet .git type cs spit context.md
#      │       │              │       │     │
#      paths   exclude        ext     lang  output
```

## Compression

gc offers three levels of compression, each building on the last:

### Level 1: `--compress` (sqz)

Pipes output through [sqz](https://github.com/ojuschugh1/sqz) for structural compression + session-aware dedup.

```bash
# Install sqz first (one time)
curl -fsSL https://raw.githubusercontent.com/ojuschugh1/sqz/main/install.sh | sh

# Then use it
gc --compress                        # ~50% reduction
gc --compress --no-cache             # compress without dedup
gc --paths src --extension cs --compress --output context.md
```

**What sqz does:**
- Understands content type — compresses JSON, logs, and code differently
- Session dedup: second run sends ~13-token references for unchanged files
- Reversible via `sqz expand`
- Entropy detection for secrets

### Level 2: `--brain` (comment stripping + whitespace collapse)

Strips comments (`//`, `/*`, `#`, `<!--`, `"""`, `--`) and collapses whitespace. No keyword substitution — just clean minification.

```bash
gc --brain                           # strips comments, collapses whitespace
```

When sqz is NOT installed, `--brain` also activates BPE-style dynamic compression using single-token Unicode symbols as a fallback.

### Level 3: `--brain --compress` (maximum compression)

Combines comment/whitespace stripping with sqz structural compression for the best results.

```bash
gc --brain --compress                # 68% reduction
gc --brain --compress --no-cache     # fresh compression, no dedup
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

### When sqz Is Not Installed

gc gracefully degrades — it warns you and falls back:

```
⚠ sqz not found. Install: curl -fsSL https://raw.githubusercontent.com/ojuschugh1/sqz/main/install.sh | sh
  See: https://github.com/ojuschugh1/sqz
⚠ Compression disabled for this run.
```

With `--brain` but no sqz, gc uses its built-in BPE-style compression as fallback.

## All Options

### Discovery & Filtering

| Option | Description |
|---|---|
| `-p, --paths` | Folders to include (e.g., `src libs`) |
| `-e, --extension` | File extensions to include (e.g., `js ts`) |
| `-x, --exclude` | Paths or patterns to skip |
| `--exclude-line-if-start` | Filter out lines starting with these strings |
| `--preset` | Use predefined configurations (`dotnet`, `web`, `python`, etc.) |
| `-d, --depth` | Maximum directory depth |
| `-f, --force` | Force filesystem discovery (ignore git) |

### Output

| Option | Description |
|---|---|
| `-o, --output` | Write to a file instead of clipboard |
| `--no-append` | Don't append to existing clipboard/file content |
| `-v, --verbose` | Enable verbose logging |
| `--debug` | Enable debug logging |

### Compression

| Option | Description |
|---|---|
| `-c, --compress` | Compress output through sqz |
| `--no-cache` | Disable sqz session dedup for this run |
| `-b, --brain` | Strip comments + collapse whitespace (BPE fallback without sqz) |

### Multi-Repo

| Option | Description |
|---|---|
| `--cluster` | Enable cluster mode for multi-repo processing |
| `--cluster-dir` | Directory containing multiple git repos |
| `--cluster-depth` | Max depth to search for git repos (default: 5) |

## Cluster Mode

Process multiple Git repositories at once:

```bash
gc --cluster --cluster-dir ~/projects
gc --cluster --cluster-dir ~/projects --extension cs --output all-code.md
gc --cluster --cluster-dir ~/projects --cluster-depth 3 --preset dotnet
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
