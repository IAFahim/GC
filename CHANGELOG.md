# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Brain Mode v2: Dynamic LLM-Optimized Compression** -- replaces hardcoded keyword dictionaries with project-specific identifier deduplication
  - `CodeLexer`: Zero-allocation `ref struct` lexer that extracts identifiers >= 6 chars from source code, skipping all comment/string forms (C-style, hash, SQL, HTML, triple-quote)
  - `FrequencyAnalyzer`: Multi-threaded identifier frequency counter with thread-local dictionaries and ROI scoring
  - `IdentifierRankedEntry`: Score model combining frequency and estimated token savings
  - `IdentifierRanker`: Generic ranking utility for score-based sorting
  - 50 dedicated tests for full branch coverage of lexer state machine and analyzer
- **Cluster Mode**: Process multiple Git repositories in a single command with `--cluster` flag
  - `--cluster-dir` to specify parent directory containing repos
  - `--cluster-depth` to control recursion depth for repo discovery
  - Automatic per-repo .gitignore respect
  - Prefixed file paths (repo_name/file_path) to avoid collisions
- `DisplayPath` on `FileEntry` for clean markdown headers in cluster mode
- `ClusterRepoInfo` model for discovered repo metadata
- `DiscoverMultipleReposAsync` on `IFileDiscovery` for batch discovery
- `ExecuteClusterAsync` on `GenerateContextUseCase` for multi-repo orchestration
- Comprehensive test suite: 208 tests covering cluster mode, edge cases, security, performance

### Changed

- `MarkdownGenerator` now supports absolute filesystem paths with separate display paths
- `FileDiscovery` supports nested git repo discovery with BFS traversal
- Help text updated with cluster mode documentation

### Fixed

- Cluster mode file path resolution (absolute paths for I/O, relative for display)
- Binary file detection in cluster mode
- Gitignore respect per-repo in cluster mode
- Empty repository handling
- Non-git directory skipping
- Symlink handling in cluster discovery
