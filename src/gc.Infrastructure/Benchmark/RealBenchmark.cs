using System.Diagnostics;
using System.Globalization;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;
using gc.Application.Services;

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
        var reader = new FileReader(_logger);
        var generator = new MarkdownGenerator(_logger);
        var filter = new FileFilter();

        var configResult = await loader.LoadConfigAsync();
        var config = configResult.Value!;

        var discoveryWatch = Stopwatch.StartNew();
        var discoveryResult = await discovery.DiscoverFilesAsync(repoPath, config);
        discoveryWatch.Stop();

        if (!discoveryResult.IsSuccess)
        {
            _logger.Error($"Discovery failed: {discoveryResult.Error}");
            return;
        }

        var rawFiles = discoveryResult.Value!.ToList();
        Console.WriteLine($"  • Files discovered: {rawFiles.Count:N0}");
        Console.WriteLine($"  • Discovery time:   {discoveryWatch.ElapsedMilliseconds} ms");

        var filterResult = filter.FilterFiles(rawFiles, config, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        var entries = filterResult.Value!.ToList();

        Console.WriteLine($"  • Files after filter: {entries.Count:N0}");

        var streamWatch = Stopwatch.StartNew();
        using var ms = new MemoryStream();
        var contents = entries.Select(e => new FileContent(e, null, e.Size));
        var genResult = await generator.GenerateMarkdownStreamingAsync(contents, ms, config);
        streamWatch.Stop();

        if (genResult.IsSuccess)
        {
            Console.WriteLine($"  • Read time:        {streamWatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"  • Total bytes:      {genResult.Value:N0}");
            var speed = genResult.Value / (streamWatch.ElapsedMilliseconds / 1000.0);
            Console.WriteLine($"  • Throughput:       {speed / 1024 / 1024:F2} MB/s");
            Console.WriteLine($"  • Total time:       {discoveryWatch.ElapsedMilliseconds + streamWatch.ElapsedMilliseconds} ms");
        }
    }
}
