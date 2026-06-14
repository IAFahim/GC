using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Order;
using gc.Application.Services;

namespace gc.Benchmarks;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
/* Uncomment for hardware counter analysis (requires admin privileges on Windows)
 *[HardwareCounters(HardwareCounter.BranchMispredictions, HardwareCounter.CacheMisses)]
 */
public class Benchmark : BenchmarkBase
{
    private string _smallInput = string.Empty;
    private string _mediumInput = string.Empty;
    private string _largeInput = string.Empty;

    private string _repeatedSmall = string.Empty;
    private string _repeatedMedium = string.Empty;

    private string _backtickMedium = string.Empty;
    private string _backtickLarge = string.Empty;

    private string[] _globPatterns = Array.Empty<string>();
    private string[] _filePaths = Array.Empty<string>();

    private string[] _ahoPatterns = Array.Empty<string>();
    private string[] _ahoLargePatterns = Array.Empty<string>();
    private string _ahoSearchText = string.Empty;

    private BrainCrusher _brainCrusher = null!;
    private DynamicCompressor _compressor = null!;
    private AhoCorasick _ahoCorasick = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        // Initialize test data
        _smallInput = GenerateCSharpCode(SmallSize);
        _mediumInput = GenerateCSharpCode(MediumSize);
        _largeInput = GenerateCSharpCode(LargeSize);

        _repeatedSmall = GenerateRepeatedText(SmallSize);
        _repeatedMedium = GenerateRepeatedText(MediumSize);

        _backtickMedium = GenerateTextWithBackticks(MediumSize);
        _backtickLarge = GenerateTextWithBackticks(LargeSize);

        _globPatterns = GenerateGlobPatterns(50);
        _filePaths = GenerateFilePaths(200);

        _ahoPatterns = GenerateAhoCorasickPatterns(20);
        _ahoLargePatterns = GenerateAhoCorasickPatterns(100);
        _ahoSearchText = GenerateCSharpCode(MediumSize);

        // Initialize services
        _brainCrusher = new BrainCrusher("cs");
        _compressor = new DynamicCompressor();
        _ahoCorasick = new AhoCorasick(_ahoPatterns);

        // Warmup JIT
        WarmupJit();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WarmupJit()
    {
        // Force JIT compilation of all benchmarked methods
        _ = SuffixArray.Build("test");
        _ = TokenEstimator.EstimateTokens("test");
        _ = GlobMatcher.IsMatch("test.cs", "*.cs");
        _ = _brainCrusher.Crush("// comment\ncode");
        _ = _compressor.Compress("code code code", 10);
        _ = new AhoCorasick(new[] { "test" });
    }

    // ========================================================================
    // SuffixArray.Build() Benchmarks
    // ========================================================================

    [Benchmark]
    [Arguments(SmallSize)]
    [Arguments(MediumSize)]
    public int[] SuffixArray_Build_SmallMedium(int size)
    {
        var input = size == SmallSize ? _repeatedSmall : _repeatedMedium;
        return SuffixArray.Build(input);
    }

    [Benchmark]
    public int[] SuffixArray_Build_10MB()
    {
        // Create a 10MB input with repeated patterns
        var input = GenerateRepeatedText(LargeSize);
        return SuffixArray.Build(input);
    }

    [Benchmark]
    [Arguments(SmallSize)]
    [Arguments(MediumSize)]
    public List<PhraseCandidate> SuffixArray_ExtractMaximalPhrases(int size)
    {
        var input = size == SmallSize ? _repeatedSmall : _repeatedMedium;
        return SuffixArray.ExtractMaximalPhrases(input, 10, 2, 50);
    }

    // ========================================================================
    // TokenEstimator Benchmarks
    // ========================================================================

    [Benchmark]
    [Arguments(SmallSize)]
    [Arguments(MediumSize)]
    [Arguments(LargeSize)]
    public int TokenEstimator_EstimateTokens(int size)
    {
        var input = size switch
        {
            SmallSize => _smallInput,
            MediumSize => _mediumInput,
            _ => _largeInput
        };
        return TokenEstimator.EstimateTokens(input);
    }

    // Baseline: Naive token counting (split by whitespace and count)
    [Benchmark]
    [Arguments(MediumSize)]
    public int TokenEstimator_Baseline_Split(int size)
    {
        var input = _mediumInput;
        return input.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    // SIMD vs Scalar comparison (manual implementation for comparison)
    [Benchmark]
    [Arguments(MediumSize)]
    public int TokenEstimator_Manual_Scalar(int size)
    {
        var input = _mediumInput;
        return CountTokensScalar(input);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountTokensScalar(ReadOnlySpan<char> text)
    {
        if (text.Length == 0) return 0;
        var tokens = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                tokens++;
        }
        return tokens + 1;
    }

    // ========================================================================
    // GlobMatcher Benchmarks
    // ========================================================================

    [Benchmark]
    [Arguments("*.cs")]
    [Arguments("src/**/*.cs")]
    [Arguments("**/*.txt")]
    [Arguments("**/node_modules/**")]
    public bool GlobMatcher_IsMatch_SinglePattern(string pattern)
    {
        return GlobMatcher.IsMatch(_filePaths[50], pattern);
    }

    [Benchmark]
    public bool GlobMatcher_IsMatch_ManyPatterns()
    {
        // Test matching against all patterns
        var matched = false;
        foreach (var pattern in _globPatterns)
        {
            matched |= GlobMatcher.IsMatch(_filePaths[50], pattern);
        }
        return matched;
    }

    [Benchmark]
    public int GlobMatcher_MatchesAny_Batch()
    {
        var count = 0;
        foreach (var path in _filePaths)
        {
            if (GlobMatcher.MatchesAny(path, _globPatterns))
                count++;
        }
        return count;
    }

    // Baseline: Simple string matching (no wildcards)
    [Benchmark]
    public int GlobMatcher_Baseline_EndsWith()
    {
        var count = 0;
        foreach (var path in _filePaths)
        {
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    // ========================================================================
    // BrainCrusher Benchmarks
    // ========================================================================

    [Benchmark]
    [Arguments(SmallSize)]
    [Arguments(MediumSize)]
    [Arguments(LargeSize)]
    public string BrainCrusher_Crush(int size)
    {
        var input = size switch
        {
            SmallSize => _smallInput,
            MediumSize => _mediumInput,
            _ => _largeInput
        };
        return _brainCrusher.Crush(input);
    }

    [Benchmark]
    [Arguments(MediumSize)]
    public string BrainCrusher_CrushBlock(int size)
    {
        return _brainCrusher.CrushBlock(_mediumInput);
    }

    // Baseline: Simple comment removal
    [Benchmark]
    [Arguments(MediumSize)]
    public string BrainCrusher_Baseline_SimpleRegex(int size)
    {
        var input = _mediumInput;
        // Very basic comment removal (not feature-complete)
        var lines = input.Split('\n');
        var result = new StringBuilder(input.Length);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("//") && !trimmed.StartsWith("/*"))
            {
                result.AppendLine(line);
            }
        }
        return result.ToString();
    }

    // ========================================================================
    // DynamicCompressor Benchmarks
    // ========================================================================

    [Benchmark]
    [Arguments(SmallSize)]
    [Arguments(MediumSize)]
    public DynamicCompressor.CompressResult DynamicCompressor_Compress(int size)
    {
        var input = size == SmallSize ? _smallInput : _mediumInput;
        return _compressor.Compress(input);
    }

    [Benchmark]
    public DynamicCompressor.CompressResult DynamicCompressor_Compress_WithCodeBlocks()
    {
        var input = $"""
            ```csharp
            {_mediumInput}
            ```
            """;
        return _compressor.Compress(input);
    }

    // Baseline: Simple string replacement
    [Benchmark]
    [Arguments(MediumSize)]
    public string DynamicCompressor_Baseline_SimpleReplace(int size)
    {
        var input = _mediumInput;
        // Simple repeated phrase replacement (not feature-complete)
        return input.Replace("public ", "p ");
    }

    // ========================================================================
    // AhoCorasick Benchmarks
    // ========================================================================

    [Benchmark]
    public AhoCorasick AhoCorasick_Build()
    {
        return new AhoCorasick(_ahoPatterns);
    }

    [Benchmark]
    public AhoCorasick AhoCorasick_Build_Large()
    {
        return new AhoCorasick(_ahoLargePatterns);
    }

    [Benchmark]
    public int AhoCorasick_Search()
    {
        var count = 0;
        var span = _ahoSearchText.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (_ahoCorasick.TryGetCharIndex(c, out var charIndex))
            {
                // Navigate automaton
                var state = 0;
                state = _ahoCorasick.GetGoto(state, charIndex);
                if (state >= 0)
                {
                    var output = _ahoCorasick.GetOutput(state);
                    if (output >= 0) count++;
                }
            }
        }
        return count;
    }

    // Baseline: Naive string searching
    [Benchmark]
    public int AhoCorasick_Baseline_Contains()
    {
        var count = 0;
        foreach (var pattern in _ahoPatterns)
        {
            if (_ahoSearchText.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    // ========================================================================
    // MarkdownGenerator Fence Selection
    // ========================================================================

    [Benchmark]
    [Arguments(MediumSize)]
    [Arguments(LargeSize)]
    public string MarkdownGenerator_GetFenceForBytes(int size)
    {
        var input = size == MediumSize ? _backtickMedium : _backtickLarge;
        var bytes = Encoding.UTF8.GetBytes(input);
        return GetFenceForBytesBenchmark(bytes, bytes.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFenceForBytesBenchmark(byte[] buffer, int length)
    {
        var longestRun = 3;
        var run = 0;
        for (var i = 0; i < length; i++)
        {
            if (buffer[i] == (byte)'`')
            {
                run++;
                if (run > longestRun) longestRun = run;
            }
            else
            {
                run = 0;
            }
        }
        return new string('`', longestRun + 1);
    }

    // Baseline: Using string operations
    [Benchmark]
    [Arguments(MediumSize)]
    public string MarkdownGenerator_Baseline_StringOps(int size)
    {
        var input = _backtickMedium;
        var maxRun = 3;
        var currentRun = 0;
        foreach (var c in input)
        {
            if (c == '`')
            {
                currentRun++;
                maxRun = Math.Max(maxRun, currentRun);
            }
            else
            {
                currentRun = 0;
            }
        }
        return new string('`', maxRun + 1);
    }

    // ========================================================================
    // Threading/Parallel Benchmarks
    // ========================================================================

    [Benchmark]
    [Arguments(SmallSize)]
    [Arguments(MediumSize)]
    public long Parallel_SuffixArray_Build(int size)
    {
        var inputs = Enumerable.Range(0, Environment.ProcessorCount)
            .Select(_ => size == SmallSize ? _repeatedSmall : _repeatedMedium)
            .ToArray();

        var results = new int[inputs.Length][];
        Parallel.For(0, inputs.Length, i =>
        {
            results[i] = SuffixArray.Build(inputs[i]);
        });

        return results.Sum(r => (long)r.Length);
    }

    [Benchmark]
    public int Parallel_TokenEstimator_Batch()
    {
        var inputs = Enumerable.Range(0, 100)
            .Select(i => _smallInput)
            .ToArray();

        var total = 0;
        Parallel.For(0, inputs.Length, i =>
        {
            Interlocked.Add(ref total, TokenEstimator.EstimateTokens(inputs[i]));
        });

        return total;
    }

    // ========================================================================
    // Memory and Allocation Benchmarks
    // ========================================================================

    [Benchmark]
    public void SuffixArray_Build_MemoryPattern()
    {
        // Repeated calls to test allocation behavior
        for (var i = 0; i < 100; i++)
        {
            _ = SuffixArray.Build(_repeatedSmall);
        }
    }

    [Benchmark]
    public void TokenEstimator_EstimateTokens_MemoryPattern()
    {
        for (var i = 0; i < 1000; i++)
        {
            _ = TokenEstimator.EstimateTokens(_smallInput);
        }
    }

    // ========================================================================
    // Edge Cases and Stress Tests
    // ========================================================================

    [Benchmark]
    public int SuffixArray_Build_Empty()
    {
        return SuffixArray.Build(string.Empty).Length;
    }

    [Benchmark]
    public int SuffixArray_Build_SingleChar()
    {
        return SuffixArray.Build("x").Length;
    }

    [Benchmark]
    public int TokenEstimator_EstimateTokens_Empty()
    {
        return TokenEstimator.EstimateTokens(string.Empty);
    }

    [Benchmark]
    public bool GlobMatcher_IsMatch_EmptyPattern()
    {
        return GlobMatcher.IsMatch(_filePaths[0], string.Empty);
    }

    [Benchmark]
    public string BrainCrusher_Crush_Empty()
    {
        return _brainCrusher.Crush(string.Empty);
    }

    // ========================================================================
    // Real-World Workload Simulation
    // ========================================================================

    [Benchmark]
    public async Task<string> RealWorld_Pipeline_CompressMarkdown()
    {
        // Simulate: code → crush → compress → estimate
        var crushed = _brainCrusher.Crush(_mediumInput);
        var compressed = _compressor.Compress(crushed);
        var tokens = TokenEstimator.EstimateTokens(compressed.Output);
        await Task.CompletedTask; // Keep signature async
        return compressed.Output;
    }

    [Benchmark]
    public void RealWorld_FileDiscovery_Filter()
    {
        // Simulate filtering files with glob patterns
        var filtered = new List<string>();
        foreach (var path in _filePaths)
        {
            if (GlobMatcher.MatchesAny(path, _globPatterns))
            {
                filtered.Add(path);
            }
        }
    }
}

// ========================================================================
// Benchmark Configuration
// ========================================================================

public class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddDiagnoser(MemoryDiagnoser.Default);
    }
}
