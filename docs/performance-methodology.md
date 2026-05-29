# GC Performance Methodology

GC is built to handle massive monorepos and cluster scanning at speeds that match pure I/O bounds. To maintain this speed while continuously adding features, we enforce strict performance methodology for every change.

## 1. Goal: Win Back Milliseconds
Every millisecond spent is time the user waits. Every confirmed millisecond saved across the organization scales linearly with developer adoption.

**Operating rules for all contributors:**
- Do not count a win until it is reproduced across at least 5 warm runs and 3 cold runs.
- Track both wall-clock latency and output correctness.
- Preserve current CLI behavior unless a breaking change is explicitly accepted.
- Every optimization PR must include: before/after numbers, allocation delta, correctness coverage.
- Favor changes that reduce allocations, repeated parsing, process startup, and unnecessary string creation.
- Maintain or add features while reducing runtime.

## 2. Profiling and Metrics
GC provides built-in tools for tracking its own performance:
- `--profile`: Prints a human-readable markdown table of execution stages (Discovery, Filtering, Preprocessing, Generation, Transformation, Clipboard) and their corresponding timings in milliseconds.
- `--stats`: Outputs execution metadata including the number of discovered files, brain mode replacements, and final output size.
- `--profile-json <file>` and `--json-stats <file>`: Exports these metrics to a machine-readable format for ingestion by continuous regression testing pipelines.

## 3. Hot Paths and known bottlenecks
### Discovery
- **Git Discovery:** We optimize git repo discovery by checking for `.git` folders using direct filesystem checks (`Directory.Exists`), bypassing slow `git rev-parse` process spawns during deep cluster scans.
- **Filesystem Discovery:** Uses `Directory.EnumerateFileSystemEntries` to minimize OS-level stat calls in a single pass instead of invoking separate enumerations for files and directories.

### Filtering
- **Glob Matching:** Custom `GlobMatcher` provides $O(1)$ fast-paths for common patterns (`*`, `**`, `*.ext`) and uses bounded backtracking to avoid $O(2^N)$ DoS vulnerabilities on complex wildcards.
- **Content Filtering:** File content filters (e.g., `--include-content`, `--exclude-content`) compile into closure-backed delegates containing pre-calculated `SearchValues`, applied as early as possible during streaming.

### Generation and I/O
- **Span-first Pipeline:** The core Markdown generation loop relies on `ReadOnlySpan<char>` and `System.IO.Pipelines.PipeWriter` chunks to avoid large string allocations and LOH (Large Object Heap) pressure.
- **Streaming Files:** Files over the 10MB threshold are streamed safely via bounded `SafeFileHandle` chunking, ensuring constant memory bounds.

## 4. Regression Gates (CI)
Future implementations should integrate the `--profile-json` output into GitHub actions to enforce a hard boundary against merge requests that regress the median performance by > 5% on standard benchmark repos.
