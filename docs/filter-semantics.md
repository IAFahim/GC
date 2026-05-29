# GC Filter Semantics

The `gc` CLI utilizes a multi-layered filtering pipeline to efficiently narrow down millions of potential files to the exact subset needed for your LLM context. Understanding this pipeline is critical for managing large monorepos.

## Pipeline Order of Operations

When `gc` runs, it filters files in the following exact order:

1. **Discovery (Git/Filesystem):** Retrieves the initial list of paths. In Git mode, this utilizes `git ls-files --cached --others --exclude-standard`, meaning `.gitignore` is inherently respected at the source.
2. **Changed Since Filter:** If `--changed-since <ref>` is used, limits the discovery pool to files modified against the provided git reference.
3. **Extension Filter:** `--extension`, `-e`, `-t`. Extremely fast, O(1) hash set lookup on the file suffix. If specified, any file not matching these extensions is immediately dropped.
4. **System Ignored Patterns:** Project-level (`config.json`) defaults for `node_modules`, `bin/`, `obj/`, etc. Fast exact-match or simple contains checks.
5. **Exact Exclude Patterns:** `--exclude`, `-y`, `-x`. Applied as a single SIMD-accelerated `SearchValues` contains check across the full path.
6. **Glob Exclude/Include:** `--exclude-path` and `--include-path`. Evaluated using `GlobMatcher`. Excludes take precedence over includes. If include patterns exist, a file MUST match at least one.
7. **Directory/Search Path Filtering:** Remaining files must belong to one of the positional path arguments passed to `gc` (e.g., `gc src/ libs/`).
8. **Binary Check:** Before reading, files are checked against known binary extensions or evaluated by inspecting the first 4KB for null bytes.
9. **Content Filtering:** `--include-content` and `--exclude-content`. The file's internal text is buffered (or streamed) and evaluated. If a file contains excluded strings, it is dropped. If it does not contain the included strings, it is dropped.

## Debugging Filters
If you are confused about why a specific file is (or is not) appearing in your final context, use the `--explain-filter` flag:

```bash
gc --explain-filter src/Core/Algorithm.cs
```

This will run the target path through the exact local configuration and CLI state provided, printing a log of each pipeline stage (`[PASSED]`, `[EXCLUDED]`) and clearly indicating which rule forced the file to be ignored.
