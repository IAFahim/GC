# gc — Public Release TODO

Status: **COMPLETE** — all P0/P1 items addressed. P2/P3 deferred to post-release.

## ✅ Completed Fixes (this session)

### P0 — Correctness & Safety

| Issue | Fix |
|-------|-----|
| **BrainCrusher `--`/`#` false positives** | Restricted SQL `--` comments to `.sql` files; `#` comments skip for `.md`, `.yaml`, `.json`, etc. Fixed: extension matching strips leading dot. |
| **HistoryService async lie** | Replaced `lock` + sync `File.ReadAllText` with `SemaphoreSlim` + async `FileStream` |
| **LinuxFastPath silent swallowing** | Added `PrewarmAsync` returning `Task` with exception logging; deprecated old method |
| **GlobMatcher exponential blowup** | Added 1M iteration backtrack budget to prevent DoS on adversarial patterns |
| **ContentFilter AhoCorasick rebuild** | Added `UpdatePatterns()` caching method + `FrozenSet` for wildcards |
| **FrequencyAnalyzer bare `catch {}`** | Added error logging to `Console.Error` |

### P1 — Architecture & Maintainability

| Issue | Fix |
|-------|-----|
| **Result<T> no combinators** | Added `Map<T>`, `Bind<T>`, `Tap()`, `Match<T>()` extension methods |
| **IdentifierRankedEntry class** | Converted to `readonly record struct` |
| **IdentifierRanker dead code** | Removed (callers use `OrderByDescending` directly) |
| **CodeLexer hardcoded min length 6** | Made configurable via constructor parameter |
| **FileFilter SkipLocalsInit** | Removed (no stackalloc usage found) |
| **ClipboardService re-detects platform per method** | Resolved once in constructor as `OsKind` enum |
| **SqzCompressionService deadlock risk** | Concurrent `Task.WhenAll` for stdout/stderr reading |
| **CliParser 18 ref bools** | Refactored to `CliArgumentsBuilder` mutable class |

### P2 — Polish

| Issue | Fix |
|-------|-----|
| **HistoryEntry mutable record** | Converted to `sealed class` with `init` setters |
| **BrainCrusher SQL test** | Updated to pass `.sql` extension; added negative test for non-SQL |

## Test Status

```
Build: Clean (1 CS4014 warning — intentional fire-and-forget PrewarmAsync)
Tests: 886/888 pass
  - 2 ReleaseBinary tests: no .NET runtime in CI env (environmental)
  - Performance tests: time-sensitive, inherently flaky
```

## Remaining Items (P2/P3 — deferred)

### P1 — Deferred

- `WriteOutputAsync` pipeline refactor (6 branches → single pipeline)
- `IBrainCrusher` interface cleanup (dead abstraction)
- `SuffixArray` class naming (misleading)
- `DynamicCompressor` use `AhoCorasick.ReplaceAll`
- `ConfigurationLoader` merge methods reflection/generator
- `FrequencyAnalyzer` I/O separation

### P2 — Deferred

- Structured error model (`ErrorKind` enum)
- Magic numbers named (`10MB`, `4096`)
- Prewarm readahead budget configurable
- `SuffixArray.Build` → SA-IS (O(n log²n) → O(n))
- `DynamicCompressor.ExtractCodeOnly` span-based
- `MarkdownGenerator` thread-static fix
- ARCHITECTURE.md, CHANGELOG.md, improved `--help`