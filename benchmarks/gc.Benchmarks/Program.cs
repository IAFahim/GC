using BenchmarkDotNet.Running;

namespace gc.Benchmarks;

/// <summary>
/// Entry point for the gc.Benchmarks suite.
/// Run with: dotnet run -c Release
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== gc Performance Benchmark Suite ===");
        Console.WriteLine();
        Console.WriteLine("This suite benchmarks the hot paths of the gc application:");
        Console.WriteLine("  - SuffixArray.Build() and phrase extraction");
        Console.WriteLine("  - TokenEstimator.EstimateTokens()");
        Console.WriteLine("  - GlobMatcher pattern matching");
        Console.WriteLine("  - BrainCrusher comment stripping");
        Console.WriteLine("  - DynamicCompressor compression");
        Console.WriteLine("  - AhoCorasick pattern matching");
        Console.WriteLine("  - MarkdownGenerator fence selection");
        Console.WriteLine("  - Parallel operations");
        Console.WriteLine();
        Console.WriteLine("Size categories:");
        Console.WriteLine("  - Small: 1KB");
        Console.WriteLine("  - Medium: 100KB");
        Console.WriteLine("  - Large: 10MB");
        Console.WriteLine();
        Console.WriteLine("Diagnosers enabled:");
        Console.WriteLine("  - MemoryDiagnoser: Tracks allocations");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -c Release                    # Run all benchmarks");
        Console.WriteLine("  dotnet run -c Release -- --filter <name>  # Run specific benchmark");
        Console.WriteLine();
        Console.WriteLine("Quick verification (7 key benchmarks):");
        Console.WriteLine("  Running QuickBenchmark...");
        Console.WriteLine();

        BenchmarkRunner.Run<QuickBenchmark>();
    }
}
