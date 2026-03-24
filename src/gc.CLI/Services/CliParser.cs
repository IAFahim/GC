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
        Discovery,
        Compact,
        Append,
        Depth
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
        var maxMemoryBytes = MemorySizeParser.Parse(configuration.Limits.MaxMemoryBytes);
        var compactLevel = gc.Domain.Models.Configuration.CompactLevel.None;
        var appendMode = false;
        var force = false;
        int? depth = configuration.Discovery.MaxDepth;

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
                ProcessFlag(flagType, ref showHelp, ref showVersion, ref runTests, ref runRealBenchmark, ref verbose, ref debug, ref initConfig, ref validateConfig, ref dumpConfig, ref compactLevel, ref appendMode, ref force);
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
                ProcessStateArg(arg, state, paths, extensions, excludes, presets, ref output, ref discoveryMode, ref maxMemoryBytes, ref compactLevel, ref depth);
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
            Compact = compactLevel,
            Append = appendMode,
            Force = force,
            Depth = depth,
            Configuration = configuration
        });
    }

    private static bool IsFlag(string arg, out string flagType)
    {
        flagType = arg switch
        {
            "-h" or "--help" or "--Help" => "help",
            "--version" or "--Version" => "version",
            "--test" or "--Test" => "test",
            "--benchmark" or "--Benchmark" => "benchmark",
            "-v" or "--verbose" or "--Verbose" => "verbose",
            "--debug" or "--Debug" => "debug",
            "--init-config" or "--Init-Config" => "init-config",
            "--validate-config" or "--Validate-Config" => "validate-config",
            "--dump-config" or "--Dump-Config" => "dump-config",
            "--compact" or "--Compact" => "compact",
            "--append" or "--Append" => "append",
            "-f" or "--force" or "--Force" => "force",
            _ => string.Empty
        };
        return !string.IsNullOrEmpty(flagType);
    }

    private static void ProcessFlag(string flagType, ref bool showHelp, ref bool showVersion, ref bool runTests, ref bool runRealBenchmark, ref bool verbose, ref bool debug, ref bool initConfig, ref bool validateConfig, ref bool dumpConfig, ref gc.Domain.Models.Configuration.CompactLevel compactLevel, ref bool appendMode, ref bool force)
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
            case "compact": compactLevel = gc.Domain.Models.Configuration.CompactLevel.Mild; break;
            case "append": appendMode = true; break;
            case "force": force = true; break;
        }
    }

    private static bool TryGetNewState(string arg, out ParseState state)
    {
        state = arg switch
        {
            "-p" or "--paths" or "--Paths" => ParseState.Paths,
            "-e" or "--extension" or "--extensions" or "--Extension" or "--Extensions" => ParseState.Extensions,
            "-x" or "--exclude" or "--excludes" or "--Exclude" or "--Excludes" => ParseState.Excludes,
            "--preset" or "--presets" or "--Preset" or "--Presets" => ParseState.Presets,
            "-o" or "--output" or "--Output" => ParseState.Output,
            "--max-memory" or "--Max-Memory" => ParseState.MaxMemory,
            "-D" or "--discovery" or "--Discovery" => ParseState.Discovery,
            "-d" or "--depth" or "--Depth" => ParseState.Depth,
            "--compact-level" or "--Compact-Level" => ParseState.Compact,
            _ => ParseState.None
        };
        return state != ParseState.None;
    }

    private static bool IsSingleValueState(ParseState state)
    {
        return state is ParseState.Output or ParseState.MaxMemory or ParseState.Discovery or ParseState.Depth;
    }

    private static void ProcessStateArg(string arg, ParseState state, List<string> paths, List<string> extensions, List<string> excludes, List<string> presets, ref string output, ref DiscoveryMode discoveryMode, ref long maxMemoryBytes, ref gc.Domain.Models.Configuration.CompactLevel compactLevel, ref int? depth)
    {
        switch (state)
        {
            case ParseState.Paths: paths.Add(arg.Replace('\\', '/')); break;
            case ParseState.Extensions: ProcessExtensions(arg, extensions); break;
            case ParseState.Excludes: excludes.Add(arg.Replace('\\', '/')); break;
            case ParseState.Presets: presets.Add(arg.ToLowerInvariant()); break;
            case ParseState.Output: output = arg; break;
            case ParseState.MaxMemory: maxMemoryBytes = MemorySizeParser.Parse(arg); break;
            case ParseState.Discovery: discoveryMode = ParseDiscoveryMode(arg); break;
            case ParseState.Compact: compactLevel = ParseCompactLevel(arg); break;
            case ParseState.Depth: 
                if (int.TryParse(arg, out var d)) depth = d;
                break;
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

    private static gc.Domain.Models.Configuration.CompactLevel ParseCompactLevel(string level)
    {
        return (level?.ToLowerInvariant()) switch
        {
            "mild" => gc.Domain.Models.Configuration.CompactLevel.Mild,
            "aggressive" => gc.Domain.Models.Configuration.CompactLevel.Aggressive,
            "none" => gc.Domain.Models.Configuration.CompactLevel.None,
            _ => gc.Domain.Models.Configuration.CompactLevel.Mild
        };
    }
}
