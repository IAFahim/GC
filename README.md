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

Both install scripts also set up shell tab-completion automatically. To do it
by hand later:

```bash
gc --install-completion          # auto-detects bash / zsh / fish
```

### Nautilus Integration (GNOME)

```bash
git submodule update --init integration/nautilus
chmod +x integration/nautilus/setup.sh
./integration/nautilus/setup.sh
```

> See [docs/installation.md](docs/installation.md) for build-from-source,
> manual completion setup, all integrations, and uninstall instructions.

## Quick Start

### Short commands for AI context

```sh
gc src -k          # collect this folder; make Markdown files of at most 50,000 bytes
gc -u -k          # portable src + Unity Runtime/Editor; exclude tests and samples
gc /path/to/src -k # pick a folder directly, from anywhere
```

`-k` writes the parts to an OS temporary folder and prints its path. It does not need a clipboard session. `-k50` and `--split 50KB` select the same limit; `KB` means 1,000 bytes, and `KiB` is explicit. Files stay whole when they fit in one part. Larger files have continuation labels with their path and original source line range. Each part is valid Markdown; the manifest records file ranges, byte ranges and hashes for lossless reconstruction of the collected export.

Set your preferred workflow once in a project:

```sh
gc init -u -k --temp -e cs
```

This writes a `.gc.json` file in the project (see [Settings](#settings)). After that, use **`gc`** for the project, **`gc src`** for a folder, or **`gc file.cs`** for one file. The saved options are also found when selecting that project from another directory. The Unity source preset includes initialized registered submodules and skips tests, samples, build output and local worktrees. Keep `.gc.json` with the project to use the same short commands on another PC.

Use `--output-dir <new-or-empty-folder>` for a specific parts folder, or `--temp` without splitting for a single temporary Markdown file. Existing nonempty part folders are preserved. Normal commands without splitting keep their existing single-file/clipboard behavior.

Split output streams uncompressed source. Combining `-k` with `--brain` or `--compress` reports an error before generation; those transforms need the whole export and cannot preserve source line ranges.

```bash
# Copy all tracked files to clipboard
gc

# Copy only C# files from src/
gc src -e cs

# Exclude node_modules and tests
gc -x node_modules "tests/*"

# Save to a file
gc -o context.md

# Use a profile (a built-in preset, or one you saved)
gc @web
```

### Fun Keywords

`gc` supports natural-language shortcuts. These can be used interchangeably with their standard flags:

| Keyword | Flag | Purpose |
|---|---|---|
| `grab` | (paths) | What to collect |
| `type` | `-e` | File extensions to include |
| `yeet` | `-x` | Paths or patterns to skip |
| `zap` | `-z` | Drop lines that start with this text |
| `brain` | `-b` | Minify |
| `compress` | `-c` | Activate sqz compression |
| `spit` | `-o` | Save to a file |
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

This header is automatically prepended to compressed output so LLMs respond with real code, not symbols. It describes only what actually ran: with `--brain` alone it explains the `GC_DICT` shorthand and does not mention sqz markers.

## All Options

Run `gc --help` for the same list. Older spellings keep working but are not shown here (see the [changelog](CHANGELOG.md)).

### What to collect

| Option | Keyword | Description |
|---|---|---|
| (paths) | `grab` | Folders, files or globs to collect, e.g. `gc src libs` or `gc "src/**/*.cs"` |
| `-e, --ext` | `type` | Only these extensions (`cs,md` or `-e cs -e md`) |
| `-x, --exclude` | `yeet` | Skip a folder, file or glob |
| `-z, --drop-lines` | `zap` | Drop lines that start with this text |
| `--exclude-content` | - | Skip files containing this text (`*` any text, `?` one character) |
| `--include-content` | - | Only files containing this text (`*` and `?` work too) |
| `--root` | - | Choose a discovery root without changing directory |
| `-r, --submodules` | - | Include initialized registered submodules |
| `--no-git` | - | Walk the filesystem instead of asking git |
| `-u, --unity` | - | `src` + Unity `Runtime`/`Editor`; skip tests and samples |
| `-d, --depth` | - | Maximum directory depth |
| `--changed-since` | - | Only files changed since a git ref |
| `--explain-filter` | - | Explain why a given file is included or excluded |

### Where it goes

| Option | Keyword | Description |
|---|---|---|
| `-o, --output` | `spit` | Write a file instead of the clipboard |
| `-k, --split [size]` | - | Markdown parts, 50KB by default; the clipboard pages them |
| `--format <name>` | - | `markdown` (default), `plain` (`==> path <==` blocks, no fences) or `json` (`{ fileCount, files: [{ path, content }] }`); setting: `output.defaultFormat`. Not combinable with `-k` |
| `-a, --append` | - | Add to what is already there |
| `-b, --brain` | `brain` | Minify (universal minification + dynamic BPE) |
| `-c, --compress` | `compress` | Structural compression via `sqz` |
| `--no-cache` | - | Disable the `sqz` session cache for a fresh run |
| `--no-clipboard` | - | Do not copy to the clipboard |
| `--no-sort` | - | Disable alphabetical sorting of files |
| `--max-memory` | - | Limit output size (e.g., `100MB`, `500KB`) |

### Look, do not write

| Option | Description |
|---|---|
| `-l, --list` | Show the files only |
| `--count` | Token estimate only |
| `--stats [file.json]` | Timings; with a file, also write them as JSON |
| `-v, --verbose` / `--debug` | More logging |

### Cluster Mode (Multi-Repo)

| Option | Keyword | Description |
|---|---|---|
| `--cluster` | `horde` | Enable cluster mode |
| `--cluster-dir` | - | Directory to scan for repos (default: CWD) |
| `--cluster-depth` | - | Max depth to search for git repos (default: 2) |

### Settings & history

| Option | Description |
|---|---|
| `@name` | Use a profile |
| `init [args]` | Write `.gc.json` here with these arguments as defaults |
| `--init-config` | Write an empty `.gc.json` |
| `--migrate-settings` | Move old `config.json` / `.gc` / `profiles.json` files into the two files |
| `--save` | Save the typed arguments as this folder's defaults |
| `--save-profile <name> [args]` | Save arguments as a user profile |
| `--list-profiles` | List project, user and built-in profiles |
| `--validate-config` / `--dump-config` | Check or print the merged settings |
| `--export-schema <file>` | Write the JSON schema for settings files |
| `--history [N]` | Show history or re-run session N |

### Shell Completion

| Option | Description |
|---|---|
| `--install-completion` | Install tab-completion for your shell (auto-detects bash/zsh/fish) |
| `--print-completion <shell>` | Print the completion script to stdout (`bash`, `zsh`, or `fish`) |

## Settings

Settings live in two files that share one format:

| File | Where | Holds |
|---|---|---|
| `gc.json` | your user config folder (`%APPDATA%\gc`, or `~/.config/gc`) | your own settings, defaults and profiles |
| `.gc.json` | the project folder (found from any subfolder, up to the Git root) | the project's settings, defaults and profiles |

```json
{
  "$schema": "https://raw.githubusercontent.com/IAFahim/GC/main/schema.json",
  "settings": { "markdown": { "languageDetection": "filename" } },
  "defaults": ["-e", "cs", "-x", "bin"],
  "profiles": { "docs": ["-e", "md", "-x", "node_modules"] }
}
```

- **`settings`** override the built-in configuration (project over user). Every key has an effect; `gc --dump-config` prints the merged result and `gc --export-schema schema.json` writes the schema for editor completion.
- **`defaults`** are arguments applied on every run, before the ones you type (user first, then project).
- **`profiles`** are named argument lists, used as `gc @docs -o out.md`. Lookup order: project, user, then built-in presets (`gc @web` is `--preset web`; `gc @unity-source` is `-u`). `gc --list-profiles` shows them all.

Settings from before the two-file format (`config.json` layers, a `.gc` arguments file, `directory_defaults.json`, `profiles.json`) are no longer read. gc prints a warning naming each one it finds; run `gc --migrate-settings` to move them into the two files above (originals are renamed to `*.migrated`).

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
dotnet build gc.sln -c Debug
dotnet test tests/gc.Tests/gc.Tests.csproj -c Debug --filter "FullyQualifiedName!~ReleaseBinary"
```

Some tests start the built `gc` executable, so build the Debug configuration first. End-to-end timings: `dotnet run --project benchmarks/gc.Benchmarks -c Release -- --real`.

## License

MIT
