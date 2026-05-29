using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Application.Services;
using gc.Application.UseCases;
using gc.Application.Validators;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using gc.Infrastructure.System;
using gc.Infrastructure.Testing;
using gc.Infrastructure.Benchmark;

namespace gc.CLI;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        var console = new SystemConsole();
        var logger = new ConsoleLogger(null, console);
        var configLoader = new ConfigurationLoader(logger);

        var configResult = await configLoader.LoadConfigAsync();
        var config = configResult.Value ?? new GcConfiguration();

        var parser = new CliParser();
        var parseResult = parser.Parse(args, config);
        if (!parseResult.IsSuccess)
        {
            logger.Error($"Failed to parse arguments: {parseResult.Error}");
            return 1;
        }

        var cliArgs = parseResult.Value!;
        if (cliArgs.Debug) logger.Level = gc.Domain.Interfaces.LogLevel.Debug;
        else if (cliArgs.Verbose) logger.Level = gc.Domain.Interfaces.LogLevel.Info;

        if (cliArgs.ShowHelp) { PrintHelp(); return 0; }
        if (cliArgs.ShowVersion) { PrintVersion(); return 0; }
        if (cliArgs.RunTests) { TestRunner.RunTests(); return 0; }
        if (cliArgs.RunRealBenchmark) { await RealBenchmark.RunRealBenchmarkAsync(logger); return 0; }

        if (!string.IsNullOrEmpty(cliArgs.ExportSchema))
        {
            try
            {
                using var stream = typeof(Program).Assembly.GetManifestResourceStream("schema.json")
                                   ?? typeof(ConfigurationService).Assembly.GetManifestResourceStream("schema.json");

                if (stream == null)
                {
                    logger.Error("Could not find embedded schema.json resource.");
                    return 1;
                }

                using var fileStream = File.Create(cliArgs.ExportSchema);
                await stream.CopyToAsync(fileStream, cts.Token);
                logger.Success($"Schema exported to {cliArgs.ExportSchema}");
                return 0;
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to export schema: {ex.Message}");
                return 1;
            }
        }

        var historyService = new HistoryService(configLoader.GetConfigDirectory(), logger);

        if (cliArgs.ShowHistory)
        {
            return await HistoryMenu.ShowAsync(historyService, parser, config, cliArgs.HistoryIndex, console, cts.Token);
        }

        var discovery = new FileDiscovery(logger);
        var filter = new FileFilter(logger);
        var contentFilter = new ContentFilter(logger);
        var generator = new MarkdownGenerator(logger);
        var clipboard = new ClipboardService(logger);
        var validator = new ConfigurationValidator();
        var configService = new ConfigurationService(logger, validator);

        bool needsReporter = cliArgs.Profile || cliArgs.ShowStats || !string.IsNullOrEmpty(cliArgs.StatsOutput) || !string.IsNullOrEmpty(cliArgs.ProfileOutput);
        ProfileReporter? profileReporter = needsReporter ? new ProfileReporter() : null;

        var useCase = new GenerateContextUseCase(discovery, filter, contentFilter, generator, clipboard, logger);

        var exitCode = await ExecuteAsync(Directory.GetCurrentDirectory(), cliArgs, config, useCase, configService, logger, cts.Token, profileReporter);

        if (profileReporter != null)
        {
            profileReporter.Stop();
            
            if (cliArgs.Profile || cliArgs.ShowStats)
            {
                logger.Info("\n" + profileReporter.ToMarkdown());
            }

            if (!string.IsNullOrEmpty(cliArgs.ProfileOutput))
            {
                try
                {
                    File.WriteAllText(cliArgs.ProfileOutput, profileReporter.ToJson());
                    logger.Info($"Profile JSON written to {cliArgs.ProfileOutput}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to write profile JSON: {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(cliArgs.StatsOutput))
            {
                try
                {
                    File.WriteAllText(cliArgs.StatsOutput, profileReporter.ToJson());
                    logger.Info($"Stats JSON written to {cliArgs.StatsOutput}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to write stats JSON: {ex.Message}");
                }
            }
        }

        if (exitCode == 0)
        {
            await historyService.AddEntryAsync(
                Directory.GetCurrentDirectory(),
                args.Where(a => !a.Equals("--history", StringComparison.OrdinalIgnoreCase)).ToArray(),
                cts.Token);
        }

        return exitCode;
    }

    internal static async Task<int> ExecuteAsync(string currentDirectory, CliArguments cliArgs, GcConfiguration config, GenerateContextUseCase useCase, ConfigurationService configService, ILogger logger, CancellationToken ct, ProfileReporter? profileReporter = null)
    {
        if (cliArgs.InitConfig) return (await configService.InitializeConfigAsync()).IsSuccess ? 0 : 1;
        if (cliArgs.ValidateConfig)
        {
            var validationResult = configService.ValidateConfig(config);
            return validationResult.IsSuccess && validationResult.Value?.IsValid == true ? 0 : 1;
        }
        if (cliArgs.DumpConfig) return configService.DumpConfig(config).IsSuccess ? 0 : 1;

        if (cliArgs.Force)
        {
            config = config with
            {
                Discovery = config.Discovery with
                {
                    Mode = "filesystem"
                }
            };
        }

        if (cliArgs.Depth.HasValue)
        {
            config = config with
            {
                Discovery = config.Discovery with
                {
                    MaxDepth = cliArgs.Depth
                }
            };
        }

        if (cliArgs.MaxMemoryBytes > 0)
        {
            config = config with
            {
                Limits = config.Limits with
                {
                    MaxMemoryBytes = cliArgs.MaxMemoryBytes.ToString() + "B"
                }
            };
        }

        if (cliArgs.NoSort)
        {
            config = config with
            {
                Output = config.Output with
                {
                    SortByPath = false
                }
            };
        }

        if (!string.IsNullOrEmpty(cliArgs.ExplainFilter))
        {
            var filter2 = new FileFilter(logger);
            filter2.ExplainFilter(
                cliArgs.ExplainFilter,
                config,
                cliArgs.Paths,
                cliArgs.Excludes,
                cliArgs.Extensions,
                cliArgs.ExcludePathPatterns,
                cliArgs.IncludePathPatterns);
            return 0;
        }

        // Dry-run: just print the files that would be processed
        if (cliArgs.DryRun)
        {
            var discovery = new FileDiscovery(logger);
            var filter2 = new FileFilter(logger);
            var discoveryResult = await discovery.DiscoverFilesAsync(currentDirectory, config, ct);
            if (!discoveryResult.IsSuccess)
            {
                logger.Error(discoveryResult.Error!);
                return 1;
            }

            var filterResult = filter2.FilterFiles(
                discoveryResult.Value!, config,
                cliArgs.Paths, cliArgs.Excludes, cliArgs.Extensions,
                cliArgs.ExcludePathPatterns, cliArgs.IncludePathPatterns, currentDirectory);

            if (!filterResult.IsSuccess)
            {
                logger.Error(filterResult.Error!);
                return 1;
            }

            var entries = filterResult.Value!.ToList();

            // Shard mode for dry-run: show only the requested shard's files
            if (cliArgs.ShardInfo != null && cliArgs.ShardInfo.Of > 1)
            {
                var splitter = new gc.Application.Services.ShardSplitter();
                var shardLists = splitter.SplitIntoShards(entries, cliArgs.ShardInfo.Of, cliArgs.ShardInfo.Slice, logger);
                var shardEntries = shardLists.Count >= cliArgs.ShardInfo.Slice ? shardLists[cliArgs.ShardInfo.Slice - 1] : entries;
                logger.Info($"Files that would be processed (shard {cliArgs.ShardInfo.Slice}/{cliArgs.ShardInfo.Of}): {shardEntries.Count} of {entries.Count}");
                foreach (var entry in shardEntries)
                {
                    Console.WriteLine(entry.DisplayPath ?? entry.Path);
                }
                return 0;
            }

            logger.Info($"Files that would be processed ({entries.Count}):");
            foreach (var entry in entries)
            {
                Console.WriteLine(entry.DisplayPath ?? entry.Path);
            }
            return 0;
        }

        // Count tokens: quick scan only
        if (cliArgs.CountTokens)
        {
            var discovery = new FileDiscovery(logger);
            var filter2 = new FileFilter(logger);
            var reader = new FileReader(logger);
            var discoveryResult = await discovery.DiscoverFilesAsync(currentDirectory, config, ct);
            if (!discoveryResult.IsSuccess)
            {
                logger.Error(discoveryResult.Error!);
                return 1;
            }

            var filterResult = filter2.FilterFiles(
                discoveryResult.Value!, config,
                cliArgs.Paths, cliArgs.Excludes, cliArgs.Extensions,
                cliArgs.ExcludePathPatterns, cliArgs.IncludePathPatterns, currentDirectory);

            if (!filterResult.IsSuccess)
            {
                logger.Error(filterResult.Error!);
                return 1;
            }

            var entries = filterResult.Value!.ToList();
            long totalTokens = 0;
            foreach (var entry in entries)
            {
                if (entry.Path.StartsWith('[')) continue; // Skip synthetic entries
                try
                {
                    var contentResult = await reader.ReadAsync(entry, ct);
                    if (contentResult.IsSuccess)
                    {
                        var fileContent = contentResult.Value;
                        var text = fileContent.Content ?? fileContent.Entry.Path;
                        totalTokens += TokenEstimator.EstimateTokens(text);
                    }
                }
                catch { }
            }
            logger.Info($"Estimated token count: {totalTokens:N0}");
            return 0;
        }

        if (cliArgs.Cluster)
        {
            var clusterDir = string.IsNullOrEmpty(cliArgs.ClusterDir)
                ? currentDirectory
                : Path.GetFullPath(cliArgs.ClusterDir);

            if (!Directory.Exists(clusterDir))
            {
                logger.Error($"Cluster directory does not exist: {clusterDir}");
                return 1;
            }

            config = config with
            {
                Output = config.Output with { SortByPath = false }
            };

            if (cliArgs.ClusterDepth.HasValue)
            {
                var existingCluster = config.Discovery?.Cluster ?? new ClusterConfiguration();
                config = config with
                {
                    Discovery = (config.Discovery ?? new DiscoveryConfiguration()) with
                    {
                        Cluster = existingCluster with
                        {
                            Enabled = true,
                            MaxDepth = cliArgs.ClusterDepth.Value
                        }
                    }
                };
            }
            else if (config.Discovery?.Cluster == null || config.Discovery.Cluster.Enabled != true)
            {
                config = config with
                {
                    Discovery = (config.Discovery ?? new DiscoveryConfiguration()) with
                    {
                        Cluster = (config.Discovery?.Cluster ?? new ClusterConfiguration()) with
                        {
                            Enabled = true
                        }
                    }
                };
            }

            logger.Info($"Cluster mode: scanning {clusterDir} for git repositories...");

            var result = await useCase.ExecuteClusterAsync(
                clusterDir,
                config,
                cliArgs.Paths,
                cliArgs.Excludes,
                cliArgs.Extensions,
                cliArgs.OutputFile,
                cliArgs.Append,
                cliArgs.ExcludeLineIfStart,
                cliArgs.BrainMode,
                cliArgs.Compress,
                cliArgs.NoCache,
                cliArgs.ExcludePathPatterns,
                cliArgs.IncludePathPatterns,
                cliArgs.ExcludeContentPatterns,
                cliArgs.IncludeContentPatterns,
                ct,
                profileReporter,
                cliArgs.UnsafeDirectWrite,
                cliArgs.ChangedSince,
                cliArgs.ShardInfo);

            if (!result.IsSuccess)
            {
                logger.Error(result.Error!);
                return 1;
            }

            return 0;
        }

        var normalResult = await useCase.ExecuteAsync(
            currentDirectory,
            config,
            cliArgs.Paths,
            cliArgs.Excludes,
            cliArgs.Extensions,
            cliArgs.OutputFile,
            cliArgs.Append,
            cliArgs.ExcludeLineIfStart,
            cliArgs.BrainMode,
            cliArgs.Compress,
            cliArgs.NoCache,
            cliArgs.ExcludePathPatterns,
            cliArgs.IncludePathPatterns,
            cliArgs.ExcludeContentPatterns,
            cliArgs.IncludeContentPatterns,
            ct,
            profileReporter,
            cliArgs.UnsafeDirectWrite,
            cliArgs.ChangedSince,
            cliArgs.ShardInfo);

        if (!normalResult.IsSuccess)
        {
            logger.Error(normalResult.Error!);
            return 1;
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"gc - Git Copy (Elite C# Edition)
USAGE: gc [OPTIONS]

DISCOVERY:
    -f, --force                      Force filesystem discovery (ignore git)
    -d, --depth <number>             Maximum directory depth to penetrate

FILTERING:
    -g, grab, -p, --paths <paths>    Filter by starting paths
    -t, type, -e, --extension <ext>  Filter by extension
    -y, yeet, -x, --exclude <path>   Exclude folder, path or pattern
    -z, zap, --exclude-line-if-start Exclude lines starting with this string
    --exclude-path <pattern>         Exclude paths matching glob pattern
    --include-path <pattern>         Include only paths matching glob pattern
    --exclude-content <keyword>      Exclude files containing this keyword
    --include-content <keyword>      Include only files containing this keyword
    --changed-since <ref>            Only include files changed since git ref
    --explain-filter <path>          Explain why a file is included or excluded
    --preset <name>                  Use a built-in preset (web,backend,dotnet,etc)

OUTPUT:
    -s, spit, -o, --output <file>    Save output to file instead of clipboard
    -b, brain, --brain               Universal minification + Dynamic BPE fallback
    -c, compress, --compress         Structural compression using sqz (session dedup)
    --no-cache                       Disable sqz dedup cache (fresh output)
    --append                         Append to current clipboard/file content
    --no-append                      Do not append (default)
    --no-sort                        Do not sort output by file path
    --dry-run, --list                Preview files to be included without generating output
    --count, --tokens-only           Show token count estimate without generating output
    --profile                        Print stage timing profile after execution
    --profile-json <file>            Write machine-readable profile JSON to file
    --stats                          Show detailed execution statistics
    --json-stats <file>              Write execution statistics to JSON file
    --unsafe-direct-write            Disable transactional file writes (write directly to target)

CLUSTER MODE:
    horde, --cluster                 Enable cluster mode (batch process repos)
    --cluster-dir <path>             Directory to scan for repos (default: CWD)
    --cluster-depth <number>         Max depth to scan for repos (default: 2)

SHARD MODE:
    --shard <N.M>                    Process shard N of M pieces (e.g. 1.3, 2.3)

    Smart grouping by module/folder, then by file size for balanced shards.

    The file listing at the end always shows all files in the repo.

CONFIGURATION:
    --init-config                    Initialize configuration
    --validate-config                Validate configuration
    --dump-config                    Show configuration
    --export-schema <file>           Export configuration JSON schema to file

OTHER:
    --history [N]                    Show run history (optionally re-run entry N)
    -v, --verbose                    Enable verbose logging
    --debug                          Enable debug logging
    --test                           Run tests
    --benchmark                      Run benchmark
    --version                        Show version information
    -h, --help                       Show this help message");
    }

    private static void PrintVersion()
    {
        Console.WriteLine("gc version 1.1.0");
        Console.WriteLine("Git Copy (Elite C# Edition) with Cluster Mode and Shard Mode");
    }
}
