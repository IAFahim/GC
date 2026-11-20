# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Smaller flag surface (#14).** The documented options are paths, `-e/--ext`, `-x/--exclude`, `-z/--drop-lines`, `-o/--output`, `-k/--split`, `-a/--append`, `-b/--brain`, `-l/--list`, `--count`, and `--stats [file.json]`. These older spellings still work and are hidden from help, so saved profiles, `.gc` files and history keep running:
  - `-p`, `--paths` and `--include-path` are plain paths (a path with `*`, `?` or `[` is a glob)
  - `--exclude-path` is `-x`
  - `-s` is `-o`
  - `-f` / `--force` is `--no-git`
  - `--dry-run` is `--list`
  - `--profile-timing`, `--profile-json <file>` and `--json-stats <file>` are `--stats [file.json]`
- **Two settings files, one format (#14).** `gc.json` (user config folder) and `.gc.json` (project) each hold `settings`, `defaults` (arguments always applied) and `profiles` (name to arguments). Presets are built-in profiles, used as `gc @unity-source` or `gc @web`. `gc init`, `--save`, `--save-profile` and `--init-config` write these files; `.gc` is no longer both a file and a folder. The old `config.json` layers, `.gc` arguments file, `directory_defaults.json` and `profiles.json` are **no longer read**; gc warns about each one it finds and `gc --migrate-settings` moves them into the two files (originals are renamed to `*.migrated`).
- One `RunOptions` record is passed to `GenerateContextUseCase.ExecuteAsync` / `ExecuteClusterAsync` instead of 21 parameters (#14).
- `schema.json` now describes the settings-file format and includes settings it was missing (`discovery.cluster`, `discovery.maxDepth`, `discovery.recurseSubmodules`, the path and content patterns); a test keeps it in sync (#14).

### Added

- `--no-git`, `-l`, `-a`, `-e/--ext` and `--drop-lines` as the canonical spellings; `gc @name` profiles; `gc --list-profiles` lists project, user and built-in profiles.
- `--format markdown|plain|json` and `output.defaultFormat`: `plain` writes `==> path <==` blocks with each file's exact content (no fences or file listing); `json` writes `{ "fileCount", "files": [{ "path", "content" }] }`. Markdown parts (`-k`) stay Markdown-only (#14).
- `*` and `?` wildcards in `--include-content` / `--exclude-content` (plain keywords stay literal and fast), as the docs already claimed (#14).

### Removed

- `--test` and `--benchmark`. The end-to-end benchmark moved to `dotnet run --project benchmarks/gc.Benchmarks -c Release -- --real` (the Makefile and benchmark workflow use it) (#14).
- `--no-append` and `--unsafe-direct-write` have no effect any more (still accepted so old arguments parse): appending is opt-in with `-a`, and every non-append write is transactional (#14).
- The unused `gc.Generators` project and the `GenerateConfigurationAccessors` attribute. Nothing built or referenced them (#14).

### Fixed

- `--format json` with `-b` now keeps valid JSON (minifying file bodies via `BrainCrusher` without injecting headers/dict text) and rejects `--compress`; `--format plain -b` keeps `==> path <==` headers intact (#14)
- `-u` / `@unity-source` now excludes `*.Tests` / `*.Tests.*` package folders and projects (e.g. `MyPackage.Tests/`), and repositories with `.gitmodules` emit a hint when no files match without `-r` (#11)
- `FileReader.ReadAsync(entry, limits)` resolves `FileInfo` against `entry.AbsolutePath` rather than `entry.Path` (#14)
- `CliParser` treats path/glob tokens after `-e` (e.g. `gc -e cs "src/**/*.cs"`) as paths instead of extensions, and `--install-completion` reads its optional shell only from raw CLI arguments (#14)
- Console output is UTF-8: the check mark in summary lines was printed as `?` on Windows, also when output was redirected (#14)
- A missing file gives `File not found: ...` on every platform (it was the raw .NET message on Windows) (#14)
- Settings that nothing read now work (#14): `filters.excludeContentPatterns` / `includeContentPatterns`, `filters.additionalExtensions` (allowed alongside an active extension filter), `discovery.useGit` (`false` is `--no-git`), `cluster.includeRootFiles`, `markdown.languageDetection` (`filename`, `content` uses the shebang of extensionless files), `output.includeStats` (`false` drops the figures from the summary line). `output.defaultFormat` now works: `markdown` (default), `plain` or `json`, also available as `--format`.

- `--count` now runs the real generator into a null sink, so it resolves files against the root (`gc src --count` no longer prints 0) and reflects `-b`, `-z` and content filters (#14)
- `FileReader` resolves entries against their root instead of the process folder (#14)
- Code fences are 3 backticks unless the content needs more (was always at least 4) (#14)
- `-b` and `-z` no longer add an extra blank line before the closing fence; in-memory and streamed paths now share one layout (#14)
- Files rejected by `--include-content` / `--exclude-content` no longer write a `[Skipped by content filter: ...]` line; it is logged at debug level (#14)
- The AI header only describes the transforms that ran (brain-only output no longer mentions sqz markers) (#14)
- `ClipboardService` no longer scans loaded assemblies in production; tests set `GC_TEST_MODE=true` (#14)

## [v1.8.0] - 2026-05-24

### Added

- **Brain Mode v2: Dynamic LLM-Optimized Compression** -- replaces hardcoded keyword dictionaries with project-specific identifier deduplication
  - `CodeLexer`: Zero-allocation `ref struct` lexer that extracts identifiers >= 6 chars from source code, skipping all comment/string forms
  - `FrequencyAnalyzer`: Multi-threaded identifier frequency counter with thread-local dictionaries and ROI scoring
  - 50 dedicated tests for full branch coverage of lexer state machine and analyzer
- **Cluster Mode**: Process multiple Git repositories in a single command with `--cluster` flag
  - `--cluster-dir` to specify parent directory containing repos
  - `--cluster-depth` to control recursion depth for repo discovery
  - Automatic per-repo .gitignore respect
  - Prefixed file paths (repo_name/file_path) to avoid collisions
- **Modernized Nautilus Integration**: Native Nautilus scripts replacing fragile Actions framework
  - `Copy to Clipboard (gc)`: General-purpose robust script
  - `Copy C# Code (gc)`: Specialized script targeting `.cs` files
  - Hardened `PATH` handling for `~/.local/bin` and `/usr/local/bin`
  - Integrated `notify-send` for visual toast notifications

### Changed

- **Native AOT by Default**: Updated all installation scripts (`install.sh`, `install.ps1`, `install-local.sh`) to publish as Native AOT binaries for near-instant startup
- **Chunked I/O Pipeline**: Refactored `MarkdownGenerator` and `FileReader` to use 64KB streaming chunks, preventing memory spikes on massive files
- **Optimized File Discovery**: Single-pass depth calculation and efficient binary detection, improving throughput by **~20%**
- `MarkdownGenerator` now supports absolute filesystem paths with separate display paths

### Fixed

- **Test Stability**: Major architectural refactoring to decouple UI from static `Console`, resolving race conditions and `ObjectDisposedException` in parallel test runs
- Cluster mode file path resolution and gitignore respect
- Windows Installer architecture detection (`x64` vs `arm64`)
