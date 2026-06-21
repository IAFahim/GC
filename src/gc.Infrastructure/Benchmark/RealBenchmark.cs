using System.Diagnostics;
using System.Text;
using gc.Application.Services;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Discovery;

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

        var loader = new ConfigurationLoader(_logger);
        var config = (await loader.LoadConfigAsync()).Value!;

        // (1) The real repository (this CWD). Small workload — mostly fixed overhead.
        var currentDir = Directory.GetCurrentDirectory();
        _logger.Info($"Testing against repository: {currentDir}");
        var repo = await MeasureAsync(currentDir, config);
        if (repo != null)
        {
            Console.WriteLine($"  • Files discovered: {repo.FileCount:N0}");
            Console.WriteLine($"  • Files after filter: {repo.EntryCount:N0}");
            Console.WriteLine($"  • Discovery time:   {repo.DiscoveryMs} ms");
            Console.WriteLine($"  • Read time:        {repo.ReadMs} ms");
            Console.WriteLine($"  • Total bytes:      {repo.Bytes:N0}");
            Console.WriteLine($"  • Throughput:       {repo.Throughput}");
            Console.WriteLine($"  • Total time:       {repo.DiscoveryMs + repo.ReadMs} ms");
            Console.WriteLine($"  • (best of {repo.Iterations} runs after {repo.Warmups} warmups)");
        }

        // (2) A larger synthetic corpus so the parallel read+generate pipeline — which barely
        // registers on the small self-benchmark — is actually exercised and guarded against
        // regression. This is where the worker-side CPU pushdown and syscall cuts pay off.
        await RunSyntheticBenchmarkAsync(config);

        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    Benchmark Complete                            ║");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝");
    }

    private async Task RunSyntheticBenchmarkAsync(GcConfiguration config)
    {
        // ~1,500 source-like files + a few large ones — enough that per-file overhead and the
        // parallel pipeline dominate, unlike the small self-benchmark.
        const int smallFiles = 1500;
        const int largeFiles = 5;

        var tempRoot = Path.Combine(Path.GetTempPath(), $"gc-bench-{Guid.NewGuid():N}");
        Console.WriteLine();
        _logger.Info($"Synthetic large workload: {smallFiles + largeFiles} files in {tempRoot}");

        try
        {
            Directory.CreateDirectory(tempRoot);

            // A ~3 KB source-like body (exercises fence scan, token estimate, CamelCase, punctuation).
            var unit =
                "public sealed class Widget { public int Compute(int x) => x * 2 + 1; } // configurationValidator XMLParser IConfig\n";
            var body = new StringBuilder(unit.Length * 30);
            for (var r = 0; r < 30; r++) body.Append(unit);
            var bodyText = body.ToString();

            for (var i = 0; i < smallFiles; i++)
                await File.WriteAllTextAsync(Path.Combine(tempRoot, $"file_{i}.cs"), $"// file {i}\n{bodyText}");

            // A handful of ~1 MB files to exercise the large-file read/copy path.
            var largeBody = new StringBuilder(bodyText.Length * 350);
            for (var r = 0; r < 350; r++) largeBody.Append(bodyText);
            var largeText = largeBody.ToString();
            for (var i = 0; i < largeFiles; i++)
                await File.WriteAllTextAsync(Path.Combine(tempRoot, $"large_{i}.cs"), largeText);

            // Force filesystem discovery: a temp dir is not a git repo, and we don't want a wasted
            // `git ls-files` spawn per iteration polluting the measurement.
            var fsConfig = config with
            {
                Discovery = (config.Discovery ?? new DiscoveryConfiguration()) with { Mode = "filesystem" }
            };

            var result = await MeasureAsync(tempRoot, fsConfig);
            if (result != null)
            {
                Console.WriteLine($"  • Large files discovered: {result.FileCount:N0}");
                Console.WriteLine($"  • Large files after filter: {result.EntryCount:N0}");
                Console.WriteLine($"  • Large discovery time: {result.DiscoveryMs} ms");
                Console.WriteLine($"  • Large read time:      {result.ReadMs} ms");
                Console.WriteLine($"  • Large total bytes:    {result.Bytes:N0}");
                Console.WriteLine($"  • Large throughput:     {result.Throughput}");
                Console.WriteLine($"  • (best of {result.Iterations} runs after {result.Warmups} warmups)");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Synthetic benchmark failed", ex);
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch
            {
                // Best-effort cleanup; a leftover temp dir is harmless.
            }
        }
    }

    /// <summary>
    ///     Warms up (warming the OS page cache + any one-time init), then takes the BEST of several
    ///     measured iterations. A single cold run is dominated by I/O and scheduler noise; best-of-N
    ///     isolates the actual code cost so regressions stay visible. Paired with the NativeAOT artifact
    ///     (see benchmark.yml) so JIT-compilation time is out of the measurement entirely.
    /// </summary>
    private async Task<MeasureResult?> MeasureAsync(string repoPath, GcConfiguration config)
    {
        var discovery = new FileDiscovery(_logger);
        var generator = new MarkdownGenerator(_logger);
        var filter = new FileFilter(_logger);

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
                return null;
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
                return null;
            }

            if (run >= warmups)
            {
                bestDiscoveryMs = Math.Min(bestDiscoveryMs, discoveryWatch.Elapsed.TotalMilliseconds);
                bestReadMs = Math.Min(bestReadMs, streamWatch.Elapsed.TotalMilliseconds);
                lastBytes = genResult.Value;
            }
        }

        var seconds = bestReadMs / 1000.0;
        var throughput = seconds > 0 ? $"{lastBytes / seconds / 1024 / 1024:F2} MB/s" : "n/a";

        return new MeasureResult(
            (long)Math.Round(bestDiscoveryMs),
            (long)Math.Round(bestReadMs),
            lastBytes,
            fileCount,
            entryCount,
            throughput,
            warmups,
            iterations);
    }

    private sealed record MeasureResult(
        long DiscoveryMs,
        long ReadMs,
        long Bytes,
        int FileCount,
        int EntryCount,
        string Throughput,
        int Warmups,
        int Iterations);
}
