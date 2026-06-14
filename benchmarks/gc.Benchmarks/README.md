# gc.Benchmarks

Comprehensive performance benchmark suite for the gc application using BenchmarkDotNet.

## Overview

This benchmark suite measures the performance of critical hot paths in the gc codebase:

- **SuffixArray.Build()** - Suffix array construction and maximal phrase extraction
- **TokenEstimator.EstimateTokens()** - LLM token estimation with BPE-aware heuristics
- **GlobMatcher.IsMatch()** - Glob pattern matching with wildcard support
- **BrainCrusher.StripComments()** - Zero-allocation comment stripping
- **DynamicCompressor.Compress()** - Dynamic BPE-style compression
- **AhoCorasick** - Multi-pattern string matching
- **MarkdownGenerator** - Streaming markdown generation and fence selection
- **FileDiscovery** - Git/filesystem file discovery
- **Parallel Operations** - Multi-threaded processing performance

## Size Categories

| Category | Size | Use Case |
|----------|------|----------|
| Small    | 1KB  | Single function/class, quick operations |
| Medium   | 100KB| Typical source file, batch processing |
| Large    | 10MB | Large codebase concatenation, stress testing |

## Running Benchmarks

### Quick Run (Recommended)

```bash
cd benchmarks/gc.Benchmarks
dotnet run -c Release
```

### Specific Benchmark

```bash
dotnet run -c Release --filter "Benchmark.TokenEstimator*"
```

### With Hardware Counters (Windows, Admin Required)

Uncomment the `[HardwareCounters]` attribute in `Benchmark.cs` and run:

```bash
dotnet run -c Release
```

### Generate Assembly

To view JIT-generated assembly code:

```bash
dotnet run -c Release --disasm
```

## Understanding Results

### Metrics

- **Mean** - Average execution time per operation
- **Allocated** - Memory allocated per operation (in bytes)
- **Gen 0/1/2 Collections** - Garbage collection pressure
- **Operations/Second** - Throughput metric

### Diagnosers

#### MemoryDiagnoser
Shows allocation behavior:
- **Allocated** - Total heap allocation
- **Gen 0/1/2** - GC generation collections

#### DisassemblyDiagnoser
Generates assembly code for:
- Identifying JIT optimization opportunities
- Verifying SIMD usage
- Checking for unnecessary bounds checks

#### ThreadingDiagnoser
Shows thread pool utilization:
- Total operations
- Thread count
- Work items processed

## Benchmark Categories

### 1. SuffixArray Benchmarks

```csharp
SuffixArray_Build_SmallMedium(size)
SuffixArray_ExtractMaximalPhrases(size)
```

Measures suffix array construction (O(n log n)) and phrase extraction for compression candidate identification.

### 2. TokenEstimator Benchmarks

```csharp
TokenEstimator_EstimateTokens(size)
TokenEstimator_Baseline_Split(size)
TokenEstimator_Manual_Scalar(size)
```

Compares optimized BPE-aware estimation against naive implementations.

### 3. GlobMatcher Benchmarks

```csharp
GlobMatcher_IsMatch_SinglePattern(pattern)
GlobMatcher_MatchesAny_Batch()
GlobMatcher_Baseline_EndsWith()
```

Tests glob pattern matching with various wildcard patterns.

### 4. BrainCrusher Benchmarks

```csharp
BrainCrusher_Crush(size)
BrainCrusher_CrushBlock(size)
BrainCrusher_Baseline_SimpleRegex(size)
```

Measures zero-allocation comment stripping performance.

### 5. DynamicCompressor Benchmarks

```csharp
DynamicCompressor_Compress(size)
DynamicCompressor_Compress_WithCodeBlocks()
DynamicCompressor_Baseline_SimpleReplace(size)
```

Tests phrase-based compression with context-aware replacement.

### 6. AhoCorasick Benchmarks

```csharp
AhoCorasick_Build()
AhoCorasick_Search()
AhoCorasick_Baseline_Contains()
```

Measures multi-pattern search automaton performance.

### 7. Threading Benchmarks

```csharp
Parallel_SuffixArray_Build(size)
Parallel_TokenEstimator_Batch()
```

Tests parallel processing scalability.

## Expected Performance Baselines

Based on actual benchmark runs (Intel Core i5-4200U):

| Operation | Size | Mean | Allocated |
|-----------|------|-------------|------------------|
| SuffixArray.Build | 1KB | 1.26ms | 13KB |
| SuffixArray.Build | 100KB | 276ms | 1.2MB |
| TokenEstimator | 1KB | 3.1μs | 0 bytes |
| TokenEstimator | 100KB | 371μs | 0 bytes |
| GlobMatcher.IsMatch | N/A | 466ns | 0 bytes |
| BrainCrusher.Crush | 1KB | 35μs | 3.6KB |
| BrainCrusher.Crush | 100KB | 6.3ms | 324KB |

### Key Findings

1. **TokenEstimator** - Zero allocations as designed, excellent performance
2. **GlobMatcher** - Sub-microsecond pattern matching with zero allocations
3. **BrainCrusher** - Linear scaling with input size, moderate allocations
4. **SuffixArray** - O(n log n) complexity visible, larger inputs need optimization

## CI Integration

To run in CI for performance regression detection:

```yaml
# .github/workflows/benchmark.yml
- name: Run Benchmarks
  run: |
    cd benchmarks/gc.Benchmarks
    dotnet run -c Release --exporters json
  env:
    BASELINE: ${{ github.base_ref }}

- name: Compare Results
  run: |
    # Compare current results against baseline
    # Fail if regression > 10%
```

## Tips for Accurate Benchmarking

1. **Run in Release mode** - Debug mode adds overhead
2. **Close other applications** - Reduce system noise
3. **Warm-up period** - BenchmarkDotNet handles this automatically
4. **Multiple iterations** - Statistical accuracy improves with more runs
5. **Consistent environment** - Same hardware, same thermal state

## Contributing

When adding new benchmarks:

1. Inherit from `BenchmarkBase` for shared utilities
2. Use the `[Benchmark]` attribute
3. Add size arguments with `[Arguments(size)]`
4. Include baselines for comparison
5. Add descriptive summary comments
6. Update expected baselines in README.md

## License

Same license as the main gc project.
