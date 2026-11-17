# GC Filter Semantics

The `gc` CLI utilizes a multi-layered filtering pipeline to efficiently narrow down millions of potential files to the exact subset needed for your LLM context. Understanding this pipeline is critical for managing large monorepos.

## Pipeline Order of Operations

When `gc` runs, it filters files in the following exact order:

1. **Discovery (Git/Filesystem):** Retrieves the initial list of paths. In Git mode, this utilizes `git ls-files --cached --others --exclude-standard`, meaning `.gitignore` is inherently respected at the source.
2. **Changed Since Filter:** If `--changed-since <ref>` is used, limits the discovery pool to files modified against the provided git reference.
3. **Extension Filter:** `-e` / `--ext`. Extremely fast, O(1) hash set lookup on the file suffix. If specified, any file not matching these extensions (or `filters.additionalExtensions` from the settings files) is immediately dropped.
4. **System Ignored Patterns:** Settings-file (`settings.filters.systemIgnoredPatterns`) defaults for `node_modules`, `bin/`, `obj/`, etc. Fast exact-match or simple contains checks.
5. **Exclude:** `-x` / `--exclude`. A folder, file or glob; plain names are checked first, globs go through `GlobMatcher`.
6. **Settings path patterns:** `settings.filters.excludePathPatterns` and `includePathPatterns`, evaluated with `GlobMatcher`. Excludes take precedence over includes. If include patterns exist, a file MUST match at least one.
7. **Directory/Search Path Filtering:** Remaining files must belong to one of the paths passed to `gc` (e.g., `gc src/ libs/`). A path containing `*`, `?` or `[` is a glob (e.g., `gc "src/**/*.cs"`); a file matching any path or glob is kept.
8. **Binary Check:** Before reading, files are checked against known binary extensions or evaluated by inspecting the first 4KB for null bytes.
9. **Content Filtering:** `--include-content` and `--exclude-content`, plus `settings.filters.includeContentPatterns` / `excludeContentPatterns`. The file's internal text is buffered (or streamed) and evaluated. `*` matches any text and `?` any one character; plain keywords are matched literally. If a file contains excluded text, it is dropped. If it does not contain the included text, it is dropped. Dropped files are not mentioned in the output.

## Debugging Filters
If you are confused about why a specific file is (or is not) appearing in your final context, use the `--explain-filter` flag:

```bash
gc --explain-filter src/Core/Algorithm.cs
```

This will run the target path through the exact local configuration and CLI state provided, printing a log of each pipeline stage (`[PASSED]`, `[EXCLUDED]`) and clearly indicating which rule forced the file to be ignored.
