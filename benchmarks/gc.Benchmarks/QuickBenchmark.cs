using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using gc.Application.Services;

namespace gc.Benchmarks;

/// <summary>
/// Quick verification benchmark - subset of key hot paths.
/// Run with: dotnet run -c Release
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class QuickBenchmark : BenchmarkBase
{
    private string _smallInput = string.Empty;
    private string _mediumInput = string.Empty;

    private BrainCrusher _brainCrusher = null!;
    private DynamicCompressor _compressor = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _smallInput = GenerateCSharpCode(SmallSize);
        _mediumInput = GenerateCSharpCode(MediumSize);
        _brainCrusher = new BrainCrusher("cs");
        _compressor = new DynamicCompressor();

        // Warmup JIT
        _ = SuffixArray.Build("test");
        _ = TokenEstimator.EstimateTokens("test");
        _ = GlobMatcher.IsMatch("test.cs", "*.cs");
    }

    [Benchmark]
    public int TokenEstimator_Small()
    {
        return TokenEstimator.EstimateTokens(_smallInput);
    }

    [Benchmark]
    public int TokenEstimator_Medium()
    {
        return TokenEstimator.EstimateTokens(_mediumInput);
    }

    [Benchmark]
    public bool GlobMatcher_IsMatch()
    {
        return GlobMatcher.IsMatch("src/Services/UserService.cs", "src/**/*.cs");
    }

    [Benchmark]
    public string BrainCrusher_Crush_Small()
    {
        return _brainCrusher.Crush(_smallInput);
    }

    [Benchmark]
    public string BrainCrusher_Crush_Medium()
    {
        return _brainCrusher.Crush(_mediumInput);
    }

    [Benchmark]
    public int[] SuffixArray_Build_Small()
    {
        return SuffixArray.Build(_smallInput);
    }

    [Benchmark]
    public int[] SuffixArray_Build_Medium()
    {
        return SuffixArray.Build(_mediumInput);
    }
}
