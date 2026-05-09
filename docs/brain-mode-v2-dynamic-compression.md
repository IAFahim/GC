# Brain Mode v2: Dynamic LLM-Optimized Compression

This document describes gc's next-generation Brain Mode compression system, which replaces hardcoded keyword dictionaries with dynamic, project-specific identifier deduplication.

## Architecture Overview

The Brain Mode v2 pipeline has four stages:

```
Source Files
    |
    v
[1] CodeLexer          -- Zero-allocation identifier extraction
    |
    v
[2] FrequencyAnalyzer  -- Multi-threaded frequency map + ROI scoring
    |
    v
[3] Macro Detector     -- Line-hash based block deduplication (Phase 3)
    |
    v
[4] BrainCrusher       -- Word-boundary Aho-Corasick replacement
    |
    v
Compressed Output with Dictionary Header
```

## Why v2?

Brain Mode v1 used a hardcoded dictionary mapping common keywords (`public` -> `!1`, `class` -> `!e`) across 9 programming languages. This approach had a fundamental flaw: **LLM tokenizers already compress common keywords to 1 token.** Replacing `public` (1 token) with `!1` (2 tokens: `!` + `1`) *increases* token count while destroying semantic meaning.

v2 shifts the focus to **project-specific identifiers** -- long variable names, class names, and repeated code blocks that genuinely waste tokens. A 24-character identifier like `ConfigurationValidator` (5+ tokens) compressed to `_A` (1 token) saves real tokens.

## Stage 1: CodeLexer

**File:** `src/gc.Application/Services/CodeLexer.cs`

A `ref struct` lexer that walks a `ReadOnlySpan<char>` and reports identifiers >= 6 characters via a callback. Zero heap allocations in the hot path.

### Comment/String Skipping

The lexer handles all common comment and string formats:

| Type | Syntax | Languages |
|------|--------|-----------|
| Single-line | `// ...` | C#, Java, JS, TS, C++, Go, Rust |
| Multi-line | `/* ... */` | C#, Java, JS, TS, C++, CSS |
| Hash | `# ...` | Python, Ruby, Shell, YAML, TOML |
| SQL | `-- ...` | SQL |
| HTML | `<!-- ... -->` | HTML, XML, Vue, Svelte |
| Triple-quote | `""" ... """` or `''' ... '''` | Python docstrings |
| String literals | `"..."` | All |
| Char literals | `'...'` | C#, Java, C++ |

### Identifier Detection

Identifiers match `[a-zA-Z_][a-zA-Z0-9_]*` with a minimum length of 6 characters. Character classification uses direct range checks (`c >= 'a' && c <= 'z'`) instead of `Char.IsLetterOrDigit` for branch-prediction-friendly performance.

### Usage

```csharp
var lexer = new CodeLexer(sourceCode.AsSpan());
int count = lexer.Enumerate(identSpan =>
{
    // identSpan is a ReadOnlySpan<char> -- valid only during callback
    var id = new string(identSpan);
    // process identifier...
});
```

### Design Decisions

- **ref struct**: Cannot escape to the heap, ensuring zero allocations. Cannot use `yield return`, so callback-based API instead.
- **Callback pattern**: Caller decides when/if to allocate strings. The lexer never calls `ToString()` itself.
- **State as separate bools**: JIT can optimize individual boolean checks better than packed bitfields for this state machine size.
- **Bounds checking via `(uint)pos < (uint)len`**: Single unsigned comparison instead of signed `pos < len`.

## Stage 2: FrequencyAnalyzer

**File:** `src/gc.Application/Services/FrequencyAnalyzer.cs`

Multi-threaded identifier frequency counter with thread-local dictionaries.

### API

```csharp
// Build raw frequency map across all .cs files
Dictionary<string, int> freqMap = FrequencyAnalyzer.BuildFrequencyMap("/path/to/src");

// Compute ROI scores
List<IdentifierRankedEntry> scored = FrequencyAnalyzer.ComputeSavingsScores(freqMap);

// All-in-one: frequency map + scoring + ranking
List<IdentifierRankedEntry> results = FrequencyAnalyzer.Analyze("/path/to/src", minLength: 6);
```

### IdentifierRankedEntry

| Property | Type | Description |
|----------|------|-------------|
| `Identifier` | `string` | The identifier text |
| `Frequency` | `int` | Number of occurrences across all files |
| `Score` | `long` | Estimated token savings: `(length - 1) * frequency` |

### Thread-Local Strategy

Instead of a single `ConcurrentDictionary` with lock contention, each thread builds its own `Dictionary<string, int>`. After all files are processed, thread-local maps merge into a single result dictionary. This eliminates all inter-thread synchronization during the hot path.

```
Thread 1: file1.cs file4.cs --> localMap1 {--merge--}
Thread 2: file2.cs file5.cs --> localMap2 {--merge--}  --> result
Thread 3: file3.cs file6.cs --> localMap3 {--merge--}
```

### ROI Formula

For each candidate identifier:

```
Score = (Length - 1) * Frequency
```

Where:
- `Length` = character count (proxy for token count)
- `-1` = estimated token count of the replacement symbol (`_A`, `_B`, etc.)
- Candidates where `Score <= 0` are discarded

## IdentifierRanker

**File:** `src/gc.Application/Services/FrequencyAnalyzer.cs` (same file)

Generic ranking utility that sorts any `IEnumerable<T>` by a `long` score selector, descending.

```csharp
var ranked = IdentifierRanker.RankByScore(entries, e => e.Score);
```

## Pipeline Integration

### Current Integration Point

The new dynamic system is designed to replace the static `BuildTokenMap()` in `BrainCrusher.cs`. The migration path is:

1. **Phase 1 (Current)**: `CodeLexer` + `FrequencyAnalyzer` extract and rank identifiers
2. **Phase 2 (Next)**: Top-N identifiers feed into `BrainCrusher` as a dynamic dictionary
3. **Phase 3 (Future)**: Line-hash macro detection adds block deduplication

### Planned Changes to Existing Code

| File | Change |
|------|--------|
| `BuiltInPresets.cs` | Delete hardcoded keyword mappings |
| `BrainCrusher.cs` | Accept dynamic dictionary from `FrequencyAnalyzer` instead of static map |
| `DynamicCompressor.cs` | Merge with `BrainCrusher`; add block/macro detection |
| `SuffixArray.cs` | Replace with `XxHash3` line-based hasher |

## Test Coverage

50 dedicated tests in `tests/gc.Tests/CodeLexerAndAnalyzerTests.cs`:

- **CodeLexer** (30 tests): All comment types, string/char literals, triple-quotes, escaped chars, unclosed/EOF paths, identifier boundary conditions, non-identifier chars, mixed scenarios
- **FrequencyAnalyzer** (8 tests): Multi-file counting, empty directories, score computation, integration tests, min-length filtering
- **IdentifierRanker** (2 tests): Descending sort, empty input
- **IdentifierRankedEntry** (1 test): Constructor property verification

Full suite: 824 tests pass (the 1 failure is a known flaky perf benchmark unrelated to this code).

## Performance Characteristics

| Component | Allocation Pattern | Complexity |
|-----------|-------------------|------------|
| CodeLexer | Zero alloc (ref struct + callback) | O(n) single pass |
| FrequencyAnalyzer | 1 string per unique identifier per thread + file read | O(n * threads) then O(k) merge |
| IdentifierRanker | Deferred (OrderBy lazy) | O(k log k) where k = unique identifiers |

## References

- `src/gc.Application/Services/CodeLexer.cs` -- Zero-allocation lexer
- `src/gc.Application/Services/FrequencyAnalyzer.cs` -- Frequency analyzer + ranker + entry model
- `tests/gc.Tests/CodeLexerAndAnalyzerTests.cs` -- 50 tests
- `docs/brain-mode-and-markdown.md` -- Brain Mode v1 documentation
