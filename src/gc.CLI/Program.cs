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

        var logger = new ConsoleLogger();
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

        // Setup UseCase
        var discovery = new FileDiscovery(logger);
        var filter = new FileFilter(logger);
        var reader = new FileReader(logger);
        var generator = new MarkdownGenerator(logger);
        var clipboard = new ClipboardService(logger);
        var validator = new ConfigurationValidator();
        var configService = new ConfigurationService(logger, validator);
        
        var useCase = new GenerateContextUseCase(discovery, filter, reader, generator, clipboard, logger);

        return await ExecuteAsync(cliArgs, config, useCase, configService, logger, cts.Token);
    }

    private static async Task<int> ExecuteAsync(CliArguments cliArgs, GcConfiguration config, GenerateContextUseCase useCase, ConfigurationService configService, ILogger logger, CancellationToken ct)
    {
        if (cliArgs.InitConfig) return (await configService.InitializeConfigAsync()).IsSuccess ? 0 : 1;
        if (cliArgs.ValidateConfig) return configService.ValidateConfig(config).IsSuccess ? 0 : 1;
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

        // Override compact level if specified via CLI
        if (cliArgs.Compact != gc.Domain.Models.Configuration.CompactLevel.None)
        {
            config = config with { Compact = cliArgs.Compact };
        }

        var result = await useCase.ExecuteAsync(
            Directory.GetCurrentDirectory(),
            config,
            cliArgs.Paths,
            cliArgs.Excludes,
            cliArgs.Extensions,
            cliArgs.OutputFile,
            cliArgs.Append,
            ct);

        if (!result.IsSuccess)
        {
            logger.Error(result.Error!);
            return 1;
        }

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"gc - Git Copy (Elite C# Edition)
USAGE: gc [OPTIONS]
OPTIONS:
    -p, --paths <paths>        Filter by starting paths
    -e, --extension <ext>      Filter by extension
    -x, --exclude <path>       Exclude folder, path or pattern
    -o, --output <file>        Save output to file instead of clipboard
    -f, --force                Force filesystem discovery (ignore git)
    -d, --depth <number>       Maximum directory depth to penetrate
    -v, --verbose              Enable verbose logging
    --init-config               Initialize configuration
    --validate-config           Validate configuration
    --dump-config               Show configuration
    --test                     Run tests
    --benchmark                Run benchmark
    --version                  Show version information");
    }

    private static void PrintVersion()
    {
        Console.WriteLine("gc version 1.0.0");
        Console.WriteLine("Git Copy (Elite C# Edition)");
    }
}
