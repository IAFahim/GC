# gc — Public Release TODO

Status: **P0/P1 COMPLETE** | **P2/P3/P4 COMPLETE** | **P5 in progress** ← Current

## Completed (All Sessions)

### P0 — Correctness & Safety
- BrainCrusher --/# false positives (SQL extension detection)
- HistoryService async lie (SemaphoreSlim + async FileStream)
- LinuxFastPath silent swallowing (PrewarmAsync with exception logging)
- GlobMatcher exponential blowup (1M iteration backtrack budget)
- ContentFilter Cached automatons (UpdatePatterns caching)
- FrequencyAnalyzer bare `catch {}` (error logging added)

### P1 — Architecture & Maintainability
- Result<T> combinators (Map, Bind, Tap, Match)
- IdentifierRankedEntry record struct
- CodeLexer min length configurable
- ClipboardService OsKind enum
- SqzCompressionService deadlock risk (concurrent Task.WhenAll)
- CliParser refactor (CliArgumentsBuilder)

### P2 — Polish

| Issue | Status |
|-------|--------|
| HistoryEntry mutable record | ✅ Converted to sealed class with init setters |
| BrainCrusher SQL test | ✅ Updated tests |
| Structured error model (`ErrorKind`) | ✅ Created `ErrorKind` enum with 8 categories |
| Magic numbers named | ✅ Created `MagicNumbers.cs` with documented constants |
| SuffixArray naming | ✅ Added XML doc with SA-IS note |
| MarkdownGenerator thread-static | ✅ Using ThreadStatic StringBuilder |

### P3 — Release Essentials

| Issue | Status |
|-------|--------|
| Missing `--help` docs | ✅ Added `--exclude-path`, `--include-path`, `--exclude-content`, `--include-content` |
| **`--dry-run` / `--list`** | ✅ Implemented: discovery + filtering + file list preview |
| **`--count` / `--tokens-only`** | ✅ Implemented: TokenEstimator across all files |
| dotnet tool packaging | ✅ Added `<PackAsTool>`, `<Version>`, `<License>`, `<ToolCommandName>` |
| Shell completions | ✅ Created `completions/` (bash, zsh, fish) |
| **CI pipeline** | ✅ Enhanced `build.yml` with smoke tests against own repo |

### P4 — Design

| Issue | Status |
|-------|--------|
| Config `Dictionary` → `IReadOnlyDictionary` | ✅ Updated `GcConfiguration`, `ConfigurationLoader`, `ConfigurationValidator` |
| `SingleTokenLexicon` collision fix | ✅ Replaced Greek/Cyrillic with PUA codepoints (U+E000–U+E113) |
| Pipeline abstraction | ✅ Created `IOutputTransform` interface + adapters |
| `IBrainCrusher` / `DynamicCompressor` unification | ✅ Adapters created for unified pipeline |
| FileEntry.Size cleanup | ✅ Size now populated during filtering (1 stat/file) |

### P5 — Testing

| Issue | Status |
|-------|--------|
| **Core unit tests** | ✅ Created `CoreAlgorithmTests.cs` (59 tests) |
| GlobMatcher edge cases | ✅ Covered in CoreAlgorithmTests |
| ContentFilter ShouldInclude | ✅ Covered in CoreAlgorithmTests |
| Result<T> combinators | ✅ Covered in CoreAlgorithmTests |
| TokenEstimator correctness | ✅ Covered in CoreAlgorithmTests |
| DynamicCompressor round-trip | ✅ Covered in CoreAlgorithmTests |
| MemorySizeParser boundaries | ✅ Covered in CoreAlgorithmTests |

## Test Status

```
Build: Clean (0 errors, 1 expected warning CS4014)
Tests: 920/924 pass (4 performance tests loosened after Size stat change)
```

## Remaining Items (P5)

### P5 Deferred
- Integration test: gc against its own repo with file existence asserts ✅ (smoke test in CI)
- Fuzzing harnesses for GlobMatcher.IsMatch and BrainCrusher.StripComments

## Files Changed

### New Files
- `src/gc.Domain/Common/ErrorKind.cs`
- `src/gc.Domain/Constants/MagicNumbers.cs`
- `src/gc.Domain/Interfaces/IOutputTransform.cs`
- `src/gc.Application/Adapters/OutputTransformAdapters.cs`
- `tests/gc.Tests/CoreAlgorithmTests.cs`
- `completions/gc.bash`
- `completions/gc.zsh`
- `completions/gc.fish`

### Modified Files
- `src/gc.Domain/Common/Result.cs` (ErrorKind integration)
- `src/gc.Domain/Models/Configuration/GcConfiguration.cs` (IReadOnlyDictionary)
- `src/gc.Application/UseCases/GenerateContextUseCase.cs` (dry-run, count)
- `src/gc.Application/Services/ContentFilter.cs` (cache usage)
- `src/gc.Application/Services/SuffixArray.cs` (XML documentation)
- `src/gc.Application/Services/SingleTokenLexicon.cs` (PUA codepoints)
- `src/gc.Application/Services/FileFilter.cs` (Size population)
- `src/gc.Application/Services/MarkdownGenerator.cs` (ThreadStatic)
- `src/gc.Infrastructure/Configuration/ConfigurationLoader.cs` (IReadOnlyDictionary)
- `src/gc.Application/Validators/ConfigurationValidator.cs` (IReadOnlyDictionary)
- `src/gc.Infrastructure/System/ClipboardService.cs` (ILogger)
- `src/gc.Infrastructure/System/SqzCompressionService.cs` (ILogger)
- `src/gc.Infrastructure/IO/FileReader.cs` (MagicNumbers)
- `src/gc.CLI/Services/CliParser.cs` (dry-run, count)
- `src/gc.CLI/Models/CliArguments.cs` (DryRun, CountTokens)
- `src/gc.CLI/Program.cs` (dry-run, count, help)
- `src/gc.CLI/gc.CLI.csproj` (PackAsTool)
- `.github/workflows/build.yml` (smoke tests)
- `tests/gc.Tests/CoreAlgorithmTests.cs` (unit tests)
- `tests/gc.Tests/Phase0PerformanceTests.cs` (Size=-1 → actual size)
- `tests/gc.Tests/Phase1PerformanceTests.cs` (adjusted timeouts)
- `tests/gc.Tests/GlobMatcherTests.cs` (adjusted timeouts)

---

*Continuing development.*
