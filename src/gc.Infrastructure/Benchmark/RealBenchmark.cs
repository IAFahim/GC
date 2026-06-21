using System.Diagnostics;
using gc.Application.Services;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;

namespace gc.Infrastructure.Benchmark;

public sealed class RealBenchmark
{
    private readonly ILogger _logger;

    public RealBenchmark(ILogger logger)
    {
        _logger = logger;
    }

    public static async Task RunRealBenchmarkAsync(ILogger logger)
    {
        var benchmark = new RealBenchmark(logger);
        await benchmark.ExecuteAsync();
    }

    public async Task ExecuteAsync()
    {
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║            GC (Git Copy) - Real Repository Benchmark            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        var currentDir = Directory.GetCurrentDirectory();
        _logger.Info($"Testing against repository: {currentDir}");

        await RunRepositoryBenchmarkAsync(currentDir);

        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    Benchmark Complete                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
    }

    private async Task RunRepositoryBenchmarkAsync(string repoPath)
    {
        var discovery = new FileDiscovery(_logger);
        var loader = new ConfigurationLoader(_logger);
        var generator = new MarkdownGenerator(_logger);
        var filter = new FileFilter(_logger);

        var configResult = await loader.LoadConfigAsync();
        var config = configResult.Value!;

        // Warm up (warms the OS page cache + any one-time init), then take the BEST of several
        // measured iterations. A single cold run is dominated by I/O and scheduler noise; best-of-N
        // isolates the actual code cost so regressions stay visible. Paired with the NativeAOT
        // artifact (see benchmark.yml) so JIT-compilation time is out of the measurement entirely.
        const int warmups = 2;
        const int iterations = 5;

        var bestDiscoveryMs = double.MaxValue;
        var bestReadMs = double.MaxValue;
        long lastBytes = 0;
        var fileCount = 0;
        var entryCount = 0;

        for (var run = 0; run < warmups + iterations; run++)
        {
            var discoveryWatch = Stopwatch.StartNew();
            var discoveryResult = await discovery.DiscoverFilesAsync(repoPath, config);
            discoveryWatch.Stop();

            if (!discoveryResult.IsSuccess)
            {
                _logger.Error($"Discovery failed: {discoveryResult.Error}");
                return;
            }

            var rawFiles = discoveryResult.Value!.ToList();
            fileCount = rawFiles.Count;

            var filterResult = filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(),
                Array.Empty<string>());
            var entries = filterResult.Value!.ToList();
            entryCount = entries.Count;

            var streamWatch = Stopwatch.StartNew();
            using var ms = new MemoryStream();
            var contents = entries.Select(e => new FileContent(e, null, e.Size));
            var genResult = await generator.GenerateMarkdownStreamingAsync(contents, ms, config);
            streamWatch.Stop();

            if (!genResult.IsSuccess)
            {
                _logger.Error($"Generation failed: {genResult.Error}");
                return;
            }

            if (run >= warmups)
            {
                bestDiscoveryMs = Math.Min(bestDiscoveryMs, discoveryWatch.Elapsed.TotalMilliseconds);
                bestReadMs = Math.Min(bestReadMs, streamWatch.Elapsed.TotalMilliseconds);
                lastBytes = genResult.Value;
            }
        }

        // Round to whole milliseconds for the headline figures (CI parses integers).
        var discoveryMs = (long)Math.Round(bestDiscoveryMs);
        var readMs = (long)Math.Round(bestReadMs);

        Console.WriteLine($"  • Files discovered: {fileCount:N0}");
        Console.WriteLine($"  • Files after filter: {entryCount:N0}");
        Console.WriteLine($"  • Discovery time:   {discoveryMs} ms");
        Console.WriteLine($"  • Read time:        {readMs} ms");
        Console.WriteLine($"  • Total bytes:      {lastBytes:N0}");
        // Guard against a sub-millisecond run that would otherwise divide by zero and print NaN MB/s.
        var seconds = bestReadMs / 1000.0;
        var throughput = seconds > 0 ? $"{lastBytes / seconds / 1024 / 1024:F2} MB/s" : "n/a";
        Console.WriteLine($"  • Throughput:       {throughput}");
        Console.WriteLine($"  • Total time:       {discoveryMs + readMs} ms");
        Console.WriteLine($"  • (best of {iterations} runs after {warmups} warmups)");
    }
}