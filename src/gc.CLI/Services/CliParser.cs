using System.Globalization;
using gc.CLI.Models;
using gc.Domain.Common;
using gc.Domain.Models.Configuration;

namespace gc.CLI.Services;

public sealed class CliParser
{
    private enum ParseState
    {
        None,
        Paths,
        Extensions,
        Excludes,
        Presets,
        Output,
        MaxMemory,
        Discovery
    }

    public Result<CliArguments> Parse(string[] args, GcConfiguration configuration)
    {
        var paths = new List<string>();
        var extensions = new List<string>();
        var excludes = new List<string>();
        var presets = new List<string>();
        var output = string.Empty;
        var showHelp = false;
        var showVersion = false;
        var runTests = false;
        var runRealBenchmark = false;
        var verbose = false;
        var debug = false;
        var initConfig = false;
        var validateConfig = false;
        var dumpConfig = false;
        var discoveryMode = ParseDiscoveryMode(configuration.Discovery.Mode);
        var maxMemoryBytes = ParseMemorySize(configuration.Limits.MaxMemoryBytes);

        var state = ParseState.None;
        var onlyPaths = false;
        var unknownFlagFound = false;

        foreach (var arg in args)
        {
            if (onlyPaths)
            {
                paths.Add(arg.Replace('\\', '/'));
                continue;
            }

            if (arg == "--")
            {
                onlyPaths = true;
                continue;
            }

            if (IsFlag(arg, out var flagType))
            {
                ProcessFlag(flagType, ref showHelp, ref showVersion, ref runTests, ref runRealBenchmark, ref verbose, ref debug, ref initConfig, ref validateConfig, ref dumpConfig);
                state = ParseState.None;
                continue;
            }

            if (TryGetNewState(arg, out var newState))
            {
                state = newState;
                continue;
            }

            if (state != ParseState.None)
            {
                ProcessStateArg(arg, state, paths, extensions, excludes, presets, ref output, ref discoveryMode, ref maxMemoryBytes);
                if (IsSingleValueState(state))
                {
                    state = ParseState.None;
                }
            }
            else
            {
                if (!ProcessDefaultArg(arg, paths))
                {
                    unknownFlagFound = true;
                }
            }
        }

        // If unknown flag found, show help
        if (unknownFlagFound)
        {
            showHelp = true;
        }

        return Result<CliArguments>.Success(new CliArguments
        {
            Paths = paths.ToArray(),
            Extensions = extensions.ToArray(),
            Excludes = excludes.ToArray(),
            Presets = presets.ToArray(),
            OutputFile = output,
            ShowHelp = showHelp,
            ShowVersion = showVersion,
            RunTests = runTests,
            RunRealBenchmark = runRealBenchmark,
            DiscoveryMode = discoveryMode,
            MaxMemoryBytes = maxMemoryBytes,
            Verbose = verbose,
            Debug = debug,
            InitConfig = initConfig,
            ValidateConfig = validateConfig,
            DumpConfig = dumpConfig,
            Configuration = configuration
        });
    }

    private static bool IsFlag(string arg, out string flagType)
    {
        flagType = arg.ToLowerInvariant() switch
        {
            "-h" or "--help" => "help",
            "--version" => "version",
            "--test" => "test",
            "--benchmark" => "benchmark",
            "-v" or "--verbose" => "verbose",
            "--debug" => "debug",
            "--init-config" => "init-config",
            "--validate-config" => "validate-config",
            "--dump-config" => "dump-config",
            _ => string.Empty
        };
        return !string.IsNullOrEmpty(flagType);
    }

    private static void ProcessFlag(string flagType, ref bool showHelp, ref bool showVersion, ref bool runTests, ref bool runRealBenchmark, ref bool verbose, ref bool debug, ref bool initConfig, ref bool validateConfig, ref bool dumpConfig)
    {
        switch (flagType)
        {
            case "help": showHelp = true; break;
            case "version": showVersion = true; break;
            case "test": runTests = true; break;
            case "benchmark": runRealBenchmark = true; break;
            case "verbose": verbose = true; break;
            case "debug": debug = true; break;
            case "init-config": initConfig = true; break;
            case "validate-config": validateConfig = true; break;
            case "dump-config": dumpConfig = true; break;
        }
    }

    private static bool TryGetNewState(string arg, out ParseState state)
    {
        state = arg.ToLowerInvariant() switch
        {
            "-p" or "--paths" => ParseState.Paths,
            "-e" or "--extension" or "--extensions" => ParseState.Extensions,
            "-x" or "--exclude" or "--excludes" => ParseState.Excludes,
            "--preset" or "--presets" => ParseState.Presets,
            "-o" or "--output" => ParseState.Output,
            "--max-memory" => ParseState.MaxMemory,
            "-d" or "--discovery" => ParseState.Discovery,
            _ => ParseState.None
        };
        return state != ParseState.None;
    }

    private static bool IsSingleValueState(ParseState state)
    {
        return state is ParseState.Output or ParseState.MaxMemory or ParseState.Discovery;
    }

    private static void ProcessStateArg(string arg, ParseState state, List<string> paths, List<string> extensions, List<string> excludes, List<string> presets, ref string output, ref DiscoveryMode discoveryMode, ref long maxMemoryBytes)
    {
        switch (state)
        {
            case ParseState.Paths: paths.Add(arg.Replace('\\', '/')); break;
            case ParseState.Extensions: ProcessExtensions(arg, extensions); break;
            case ParseState.Excludes: excludes.Add(arg.Replace('\\', '/')); break;
            case ParseState.Presets: presets.Add(arg.ToLowerInvariant()); break;
            case ParseState.Output: output = arg; break;
            case ParseState.MaxMemory: maxMemoryBytes = ParseMemorySize(arg); break;
            case ParseState.Discovery: discoveryMode = ParseDiscoveryMode(arg); break;
        }
    }

    private static void ProcessExtensions(string arg, List<string> extensions)
    {
        if (arg.Contains(','))
        {
            var split = arg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var ext in split)
            {
                extensions.Add(ext.Trim().TrimStart('.').ToLowerInvariant());
            }
        }
        else
        {
            extensions.Add(arg.TrimStart('.').ToLowerInvariant());
        }
    }

    private static bool ProcessDefaultArg(string arg, List<string> paths)
    {
        if (!arg.StartsWith('-'))
        {
            paths.Add(arg.Replace('\\', '/'));
            return true;
        }

        // Unknown flag (starts with - but wasn't recognized)
        return false;
    }

    private static long ParseMemorySize(string size)
    {
        if (string.IsNullOrWhiteSpace(size)) return 104857600;

        size = size.Trim().ToUpperInvariant();
        long multiplier = 1;

        if (size.EndsWith("KB", StringComparison.Ordinal)) { multiplier = 1024; size = size[..^2]; }
        else if (size.EndsWith("MB", StringComparison.Ordinal)) { multiplier = 1048576; size = size[..^2]; }
        else if (size.EndsWith("GB", StringComparison.Ordinal)) { multiplier = 1073741824; size = size[..^2]; }
        else if (size.EndsWith("B", StringComparison.Ordinal)) { size = size[..^1]; }

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return (long)(value * multiplier);
        }

        return 104857600;
    }

    private static DiscoveryMode ParseDiscoveryMode(string mode)
    {
        return (mode?.ToLowerInvariant()) switch
        {
            "auto" => DiscoveryMode.Auto,
            "git" => DiscoveryMode.Git,
            "filesystem" => DiscoveryMode.FileSystem,
            _ => DiscoveryMode.Auto
        };
    }
}
