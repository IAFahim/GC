using gc.Application.Services;
using gc.Application.UseCases;
using gc.Application.Validators;
using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Discovery;
using gc.Infrastructure.IO;
using gc.Infrastructure.Logging;
using gc.Infrastructure.System;

namespace gc.CLI;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // The summary lines use a check mark and paths may be non-ASCII; the Windows default code page
        // would turn them into '?' (also when output is redirected).
        try
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            // No console attached: keep the default.
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var console = new SystemConsole();
        var logger = new ConsoleLogger(null, console);
        var configLoader = new ConfigurationLoader(logger);

        var callerDirectory = Directory.GetCurrentDirectory();
        string currentDirectory;
        try
        {
            currentDirectory = InvocationRootResolver.FindRoot(args, callerDirectory);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logger.Error(ex.Message);
            return 1;
        }
        var configResult = await configLoader.LoadConfigAsync(rootPath: currentDirectory);
        var config = configResult.Value ?? new GcConfiguration();
        logger.ApplyConfiguration(config.Logging);

        var configDir = configLoader.GetConfigDirectory();
        if (!args.Contains("--migrate-settings"))
            foreach (var legacy in SettingsStore.FindLegacyFiles(configDir, currentDirectory,
                         configLoader.GetSystemConfigDirectory()))
                logger.Warning($"Ignoring old settings file {legacy}. Run gc --migrate-settings to move it into " +
                               $"{SettingsStore.UserFileName} / {SettingsStore.ProjectFileName}.");
        string[] resolvedArgs;
        bool shouldExit;
        int exitCode;
        (resolvedArgs, shouldExit, exitCode) = await CliArgumentResolver.ResolveAsync(args, configDir, currentDirectory);
        if (shouldExit)
        {
            return exitCode;
        }

        var parser = new CliParser();
        var parseResult = parser.Parse(resolvedArgs, config);
        if (!parseResult.IsSuccess)
        {
            logger.Error($"Failed to parse arguments: {parseResult.Error}");
            Console.WriteLine("Use --help to see all available options.");
            return 1;
        }

        var cliArgs = parseResult.Value!;

        try
        {
            currentDirectory = InvocationRootResolver.Resolve(cliArgs, callerDirectory);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logger.Error(ex.Message);
            return 1;
        }

        if (cliArgs.SplitError != null)
        {
            logger.Error($"Invalid split size '{cliArgs.SplitError}'. Use -k for 50KB, or --split 50KB.");
            return 1;
        }

        if (cliArgs.NoClipboard)
        {
            config = config with
            {
                Output = config.Output with { NoClipboard = true }
            };
        }

        // Validate shard argument early
        if (cliArgs.ShardError != null && cliArgs.ShardInfo == null)
        {
            logger.Error($"Invalid --shard value '{cliArgs.ShardError}'. Expected format: N.M where N ≤ M (e.g. 1.3, 2.5)");
            return 1;
        }
        if (cliArgs.MaxMemoryError != null)
        {
            logger.Error($"Invalid --max-memory value '{cliArgs.MaxMemoryError}'. Expected format: 100MB, 1GB, 500B, etc.");
            return 1;
        }
        if (cliArgs.Debug) logger.Level = LogLevel.Debug;
        else if (cliArgs.Verbose) logger.Level = LogLevel.Info;

        if (cliArgs.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (cliArgs.ShowVersion)
        {
            PrintVersion();
            return 0;
        }

        if (cliArgs.PrintCompletion != null)
            return CompletionInstaller.Print(logger, cliArgs.PrintCompletion);

        if (cliArgs.InstallCompletion != null)
        {
            var shell = ResolveInstallCompletionShell(args);
            return CompletionInstaller.Install(logger, shell);
        }

        if (!string.IsNullOrEmpty(cliArgs.ExportSchema))
            try
            {
                var appAssembly = typeof(ConfigurationService).Assembly;
                var cliAssembly = typeof(Program).Assembly;

                var appResourceName = appAssembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("schema.json", StringComparison.OrdinalIgnoreCase));
                var cliResourceName = cliAssembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("schema.json", StringComparison.OrdinalIgnoreCase));

                using var stream = (appResourceName != null ? appAssembly.GetManifestResourceStream(appResourceName) : null)
                                   ?? (cliResourceName != null ? cliAssembly.GetManifestResourceStream(cliResourceName) : null)
                                   ?? cliAssembly.GetManifestResourceStream("schema.json");

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

        var historyService = new HistoryService(configDir, logger);

        if (cliArgs.ShowHistory)
            return await HistoryMenu.ShowAsync(historyService, parser, config, cliArgs.HistoryIndex, console,
                cts.Token);

        var discovery = new FileDiscovery(logger);
        var filter = new FileFilter(logger);
        var contentFilter = new ContentFilter(logger);
        var generator = new MarkdownGenerator(logger);
        var clipboard = new ClipboardService(logger);
        var validator = new ConfigurationValidator();
        var configService = new ConfigurationService(logger, validator);

        var needsReporter = cliArgs.ShowStats || !string.IsNullOrEmpty(cliArgs.StatsOutput);
        var profileReporter = needsReporter ? new ProfileReporter() : null;

        var useCase = new GenerateContextUseCase(discovery, filter, contentFilter, generator, clipboard, logger);

        // Safety net: a CLI must never core-dump on bad input or a hostile environment. Convert any
        // unhandled exception (e.g. an output path that can't be written) into a clean error + exit
        // code, and treat cancellation (Ctrl+C) as the conventional 130.
        try
        {
            exitCode = await ExecuteAsync(currentDirectory, cliArgs, config, useCase, configService,
                logger, cts.Token, profileReporter);
        }
        catch (OperationCanceledException)
        {
            logger.Error("Operation cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            logger.Error($"Unexpected error: {ex.Message}");
            logger.Debug(ex.ToString());
            return 1;
        }

        if (profileReporter != null)
        {
            profileReporter.Stop();

            if (cliArgs.ShowStats) logger.Info("\n" + profileReporter.ToMarkdown());

            if (!string.IsNullOrEmpty(cliArgs.StatsOutput))
                try
                {
                    await File.WriteAllTextAsync(cliArgs.StatsOutput, profileReporter.ToJson(), cts.Token);
                    logger.Info($"Stats JSON written to {cliArgs.StatsOutput}");
                }
                catch (Exception ex)
                {
                    logger.Error($"Failed to write stats JSON: {ex.Message}");
                }
        }

        if (exitCode == 0)
            await historyService.AddEntryAsync(
                callerDirectory,
                resolvedArgs.Where(a => !a.Equals("--history", StringComparison.OrdinalIgnoreCase)).ToArray(),
                cts.Token);

        return exitCode;
    }

    internal static async Task<int> ExecuteAsync(string currentDirectory, CliArguments cliArgs, GcConfiguration config,
        GenerateContextUseCase useCase, ConfigurationService configService, ILogger logger, CancellationToken ct,
        ProfileReporter? profileReporter = null)
    {
        currentDirectory = InvocationRootResolver.Resolve(cliArgs, currentDirectory);
        var contextOptions = SourceContextOptions.Apply(cliArgs, config, currentDirectory);
        if (!contextOptions.IsSuccess)
        {
            logger.Error(contextOptions.Error!);
            return 1;
        }
        if (cliArgs.SplitError != null)
        {
            logger.Error("Invalid split size: " + cliArgs.SplitError);
            return 1;
        }
        if (cliArgs.RecurseSubmodules)
            config = config with { Discovery = config.Discovery with { RecurseSubmodules = true } };
        if (cliArgs.RecurseSubmodules && cliArgs.ChangedSince != null)
        {
            logger.Error("--changed-since cannot currently be combined with recursive submodules.");
            return 1;
        }

        ChunkOutputOptions? chunkOutput = null;
        if (cliArgs.SplitBytes > 0)
        {
            if (cliArgs.OutputDirectory != null && !string.IsNullOrEmpty(cliArgs.OutputFile))
            {
                logger.Error("Choose either --output-dir or --output for Markdown parts.");
                return 1;
            }
            var destination = cliArgs.OutputDirectory;
            var prefix = "context";
            if (!string.IsNullOrEmpty(cliArgs.OutputFile))
            {
                var file = Path.GetFullPath(cliArgs.OutputFile);
                prefix = Path.GetFileNameWithoutExtension(file);
                destination = Path.Combine(Path.GetDirectoryName(file)!, prefix + "-parts");
            }
            chunkOutput = new ChunkOutputOptions(cliArgs.SplitBytes, destination, prefix);
        }
        else if (cliArgs.OutputDirectory != null)
        {
            logger.Error("--output-dir requires -k or --split.");
            return 1;
        }
        else if (cliArgs.TempOutput && string.IsNullOrEmpty(cliArgs.OutputFile))
            cliArgs.OutputFile = Path.Combine(Path.GetTempPath(), "gc-" + Guid.NewGuid().ToString("N")[..12], "context.md");

        if (cliArgs.InitConfig)
            return (await configService.InitializeConfigAsync(
                Path.Combine(currentDirectory, SettingsStore.ProjectFileName))).IsSuccess ? 0 : 1;
        if (cliArgs.ValidateConfig)
        {
            var validationResult = configService.ValidateConfig(config);
            return validationResult.IsSuccess && validationResult.Value?.IsValid == true ? 0 : 1;
        }

        if (cliArgs.DumpConfig) return configService.DumpConfig(config).IsSuccess ? 0 : 1;

        if (cliArgs.NoGit)
            config = config with
            {
                Discovery = config.Discovery with
                {
                    Mode = "filesystem"
                }
            };

        if (cliArgs.Depth.HasValue)
            config = config with
            {
                Discovery = config.Discovery with
                {
                    MaxDepth = cliArgs.Depth
                }
            };

        if (cliArgs.MaxMemoryBytes > 0)
            config = config with
            {
                Limits = config.Limits with
                {
                    MaxMemoryBytes = cliArgs.MaxMemoryBytes + "B"
                }
            };

        if (cliArgs.Format != null)
        {
            if (cliArgs.Format.ToLowerInvariant() is not ("markdown" or "plain" or "json"))
            {
                logger.Error($"Invalid --format '{cliArgs.Format}'. Use markdown, plain or json.");
                return 1;
            }

            config = config with { Output = config.Output with { DefaultFormat = cliArgs.Format.ToLowerInvariant() } };
        }

        if (cliArgs.NoSort)
            config = config with
            {
                Output = config.Output with
                {
                    SortByPath = false
                }
            };

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

        // --list: just print the files that would be processed
        if (cliArgs.List)
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
                cliArgs.ExcludePathPatterns, cliArgs.IncludePathPatterns, currentDirectory, currentDirectory);

            if (!filterResult.IsSuccess)
            {
                logger.Error(filterResult.Error!);
                return 1;
            }

            var entries = filterResult.Value!.ToList();

            // Shard mode for dry-run: show only the requested shard's files
            if (cliArgs.ShardInfo != null && cliArgs.ShardInfo.Of > 1)
            {
                var splitter = new ShardSplitter();
                var shardLists =
                    splitter.SplitIntoShards(entries, cliArgs.ShardInfo.Of, cliArgs.ShardInfo.Slice, logger);
                var shardEntries = shardLists.Count >= cliArgs.ShardInfo.Slice
                    ? shardLists[cliArgs.ShardInfo.Slice - 1]
                    : entries;
                logger.Info(
                    $"Files that would be processed (shard {cliArgs.ShardInfo.Slice}/{cliArgs.ShardInfo.Of}): {shardEntries.Count} of {entries.Count}");
                foreach (var entry in shardEntries) Console.WriteLine(entry.DisplayPath ?? entry.Path);
                return 0;
            }

            logger.Info($"Files that would be processed ({entries.Count}):");
            foreach (var entry in entries) Console.WriteLine(entry.DisplayPath ?? entry.Path);
            return 0;
        }

        // Count tokens: run the real generator into a null sink so the estimate reflects exactly what
        // would be written (brain mode, line drops, content filters) and files resolve against the root.
        if (cliArgs.CountTokens)
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
                cliArgs.ExcludePathPatterns, cliArgs.IncludePathPatterns, currentDirectory, currentDirectory);

            if (!filterResult.IsSuccess)
            {
                logger.Error(filterResult.Error!);
                return 1;
            }

            var generator = new MarkdownGenerator(logger);
            var compiled = new ContentFilter(logger).CompilePatterns(
                ContentFilter.Merge(config.Filters?.ExcludeContentPatterns, cliArgs.ExcludeContentPatterns),
                ContentFilter.Merge(config.Filters?.IncludeContentPatterns, cliArgs.IncludeContentPatterns));
            var contents = filterResult.Value!.Select(e => new FileContent(e, null, e.Size));
            var counted = await generator.GenerateMarkdownStreamingAsync(contents, Stream.Null, config, compiled,
                cliArgs.ExcludeLineIfStart, cliArgs.BrainMode ? new BrainCrusher() : null, ct);
            if (!counted.IsSuccess)
            {
                logger.Error(counted.Error!);
                return 1;
            }

            // The token count is the explicit result of --count, not a log message: write it to
            // stdout so it always shows (logger.Info is suppressed at the default verbosity) and is
            // cleanly pipeable/greppable by scripts.
            Console.WriteLine($"Estimated token count: {generator.LastEstimatedTokens:N0}");
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
                clusterDir, config, cliArgs.ToRunOptions(profileReporter, chunkOutput), ct);

            if (!result.IsSuccess)
            {
                logger.Error(result.Error!);
                return 1;
            }

            return 0;
        }

        var normalResult = await useCase.ExecuteAsync(
            currentDirectory, config, cliArgs.ToRunOptions(profileReporter, chunkOutput), ct);

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
USAGE: gc [PATHS...] [OPTIONS]      gc @profile [OPTIONS]

WHAT TO COLLECT:
    <folder-or-file>                 Collect that folder or file directly (globs like src/**/*.cs work too)
    -e, --ext <list>                 Only these extensions (cs,md or -e cs -e md)
    -x, --exclude <list>             Skip a folder, file or glob
    -z, --drop-lines <list>          Drop lines that start with this text
    --root <folder>                  Choose a discovery root without changing directory
    -r, --submodules                 Include initialized registered submodules
    --no-git                         Walk the filesystem instead of asking git
    -u, --unity                      src + Unity Runtime/Editor; skip tests and samples
    --exclude-content <keyword>      Skip files containing this text (* and ? allowed)
    --include-content <keyword>      Only files containing this text (* and ? allowed)
    --changed-since <ref>            Only files changed since a git ref
    --explain-filter <path>          Explain why a file is included or excluded
    -d, --depth <number>             Maximum directory depth

WHERE IT GOES:
    -o, --output <file>              Write a file instead of the clipboard
    --format <name>                  markdown (default), plain (==> path <== blocks) or json; settings: output.defaultFormat
    -k, --split [size]               Markdown parts, 50KB by default (KB=1000 bytes); saved to a temp folder
    -a, --append                     Add to what is already there
    -b, --brain                      Minify
    -c, --compress                   Structural compression using sqz (session dedup)
    --temp                           Save to the OS temp folder; print its path
    --output-dir <folder>            Destination for size-limited parts
    --no-clipboard                   Do not copy to the clipboard
    --no-cache                       Disable the sqz dedup cache
    --no-sort                        Do not sort output by file path

LOOK, DO NOT WRITE:
    -l, --list                       Show the files only
    --count                          Token estimate only
    --stats [file.json]              Timings; with a file, also write them as JSON

CLUSTER AND SHARD:
    horde, --cluster                 Process every repo under a folder
    --cluster-dir <path>             Folder to scan for repos (default: current)
    --cluster-depth <number>         How deep to look for repos (default: 2)
    --shard <N.M>                    Process piece N of M (e.g. 1.3)

SETTINGS (two files, one format):
    gc.json (user) and .gc.json (project). Each holds settings, defaults (arguments
    always applied) and profiles (name to arguments). Project overrides user.
    @name                            Use a profile: gc @unity-source, gc @docs -o out.md
    --preset <name>                  Same as @name for a built-in preset (web,backend,dotnet,unity...)
    init [args...]                   Write .gc.json here with these arguments as defaults
    --init-config                    Write an empty .gc.json (settings, defaults, profiles)
    --migrate-settings               Move old config.json / .gc / profiles.json files into the two files
    --save                           Save the typed arguments as this folder's defaults
    --save-profile <name> [args...]  Save arguments as a user profile
    --list-profiles                  List project, user and built-in profiles
    --validate-config                Validate the merged settings
    --dump-config                    Show the merged settings
    --export-schema <file>           Export the settings JSON schema

SHELL COMPLETION:
    --install-completion             Install tab-completion for your shell (auto-detects bash/zsh/fish)
    --print-completion <shell>       Print the completion script (bash|zsh|fish)

OTHER:
    --history [N]                    Show run history (optionally re-run entry N)
    -v, --verbose                    Verbose logging
    --debug                          Debug logging
    --version                        Show version information
    -h, --help                       Show this help message");
    }

    private static string? ResolveInstallCompletionShell(string[] rawArgs)
    {
        for (var i = 0; i < rawArgs.Length; i++)
        {
            if (rawArgs[i].Equals("--install-completion", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < rawArgs.Length && !rawArgs[i + 1].StartsWith('-'))
                return rawArgs[i + 1];
        }

        return null;
    }

    private static void PrintVersion()
    {
        var version = typeof(Program).Assembly.GetName().Version;
        Console.WriteLine($"gc version {version?.ToString(3) ?? "unknown"}");
        Console.WriteLine("Git Copy with native Markdown parts and direct folder selection");
    }
}
