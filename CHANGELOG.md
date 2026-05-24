# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
