using System;
using System.Collections.Generic;

namespace GC.Data;

public enum DiscoveryMode
{
    Auto,
    Git,
    FileSystem
}

public static class CliParserExtensions
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

    public static CliArguments ParseCli(this string[] args)
    {
        var paths = new List<string>(4);
        var extensions = new List<string>(8);
        var excludes = new List<string>(8);
        var presets = new List<string>(4);
        var output = string.Empty;
        var showHelp = false;
        var runTests = false;
        var runRealBenchmark = false;
        var discoveryMode = DiscoveryMode.Auto;
        var maxMemoryBytes = ParseMemorySize("100MB");
        var verbose = false;
        var debug = false;

        var state = ParseState.None;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.IsHelpFlag())
            {
                showHelp = true;
                continue;
            }

            if (arg.IsTestFlag())
            {
                runTests = true;
                continue;
            }

            if (arg.IsBenchmarkFlag())
            {
                runRealBenchmark = true;
                continue;
            }

            if (arg.TryGetNewDiscoveryState(out var discoveryState))
            {
                state = discoveryState;
                continue;
            }

            if (arg.IsVerboseFlag())
            {
                verbose = true;
                continue;
            }

            if (arg.IsDebugFlag())
            {
                debug = true;
                continue;
            }

            if (arg.TryGetNewState(out var newState))
            {
                // Check if we have a dangling argument from previous state
                if (state != ParseState.None && i + 1 >= args.Length)
                {
                    Console.Error.WriteLine($"Error: Missing value for argument: {args[args.Length - 1]}");
                    Environment.Exit(1);
                }

                state = newState;
                continue;
            }

            arg.ProcessArg(state, paths, extensions, excludes, presets, ref output, ref discoveryMode, ref maxMemoryBytes);
            state = ParseState.None; // Reset state after processing
        }

        // Check for dangling state at the end
        if (state != ParseState.None)
        {
            Console.Error.WriteLine($"Error: Missing value for last argument");
            Environment.Exit(1);
        }

        return new CliArguments(
            paths.ToArray(),
            extensions.ToArray(),
            excludes.ToArray(),
            presets.ToArray(),
            output,
            showHelp,
            runTests,
            runRealBenchmark,
            discoveryMode,
            maxMemoryBytes,
            verbose,
            debug);
    }

    private static bool IsHelpFlag(this string arg)
    {
        return string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTestFlag(this string arg)
    {
        return string.Equals(arg, "--test", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBenchmarkFlag(this string arg)
    {
        return string.Equals(arg, "--benchmark", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVerboseFlag(this string arg)
    {
        return string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDebugFlag(this string arg)
    {
        return string.Equals(arg, "--debug", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetNewState(this string arg, out ParseState state)
    {
        state = ParseState.None;

        if (string.Equals(arg, "-p", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--paths", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Paths;
            return true;
        }

        if (string.Equals(arg, "-e", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--extension", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--extensions", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Extensions;
            return true;
        }

        if (string.Equals(arg, "-x", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--exclude", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--excludes", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Excludes;
            return true;
        }

        if (string.Equals(arg, "--preset", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--presets", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Presets;
            return true;
        }

        if (string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Output;
            return true;
        }

        if (string.Equals(arg, "--max-memory", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.MaxMemory;
            return true;
        }

        return false;
    }

    private static void ProcessArg(this string arg, ParseState state, List<string> paths, List<string> extensions, List<string> excludes, List<string> presets, ref string output, ref DiscoveryMode discoveryMode, ref long maxMemoryBytes)
    {
        switch (state)
        {
            case ParseState.Paths:
                paths.Add(arg.Replace('\\', '/'));
                break;
            case ParseState.Extensions:
                extensions.Add(arg.TrimStart('.').ToLowerInvariant());
                break;
            case ParseState.Excludes:
                excludes.Add(arg.Replace('\\', '/'));
                break;
            case ParseState.Presets:
                presets.Add(arg.ToLowerInvariant());
                break;
            case ParseState.Output:
                output = arg;
                break;
            case ParseState.MaxMemory:
                maxMemoryBytes = ParseMemorySize(arg);
                break;
            case ParseState.Discovery:
                discoveryMode = ParseDiscoveryMode(arg);
                break;
            case ParseState.None:
                break;
        }
    }

    private static long ParseMemorySize(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 104857600; // 100MB default

        size = size.Trim().ToUpperInvariant();
        long multiplier = 1;

        if (size.EndsWith("KB", StringComparison.Ordinal))
        {
            multiplier = 1024;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("MB", StringComparison.Ordinal))
        {
            multiplier = 1048576;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("GB", StringComparison.Ordinal))
        {
            multiplier = 1073741824;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("B", StringComparison.Ordinal))
        {
            size = size.Substring(0, size.Length - 1);
        }

        if (double.TryParse(size, out var value))
        {
            return (long)(value * multiplier);
        }

        return 104857600; // 100MB default if parsing fails
    }

    private static DiscoveryMode ParseDiscoveryMode(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "auto" => DiscoveryMode.Auto,
            "git" => DiscoveryMode.Git,
            "filesystem" => DiscoveryMode.FileSystem,
            _ => DiscoveryMode.Auto
        };
    }

    private static bool TryGetNewDiscoveryState(this string arg, out ParseState state)
    {
        state = ParseState.None;

        if (string.Equals(arg, "--discovery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-d", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Discovery;
            return true;
        }

        return false;
    }
}
