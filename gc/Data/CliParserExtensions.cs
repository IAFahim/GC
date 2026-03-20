using System.Globalization;

namespace gc.Data;

public enum DiscoveryMode
{
    Auto,
    Git,
    FileSystem
}

public static class CliParserExtensions
{
    public static CliArguments ParseCli(this string[] args)
    {
        var config = ConfigurationLoader.LoadConfiguration();

        var paths = new List<string>(4);
        var extensions = new List<string>(8);
        var excludes = new List<string>(8);
        var presets = new List<string>(4);
        var output = string.Empty;
        var showHelp = false;
        var runTests = false;
        var runRealBenchmark = false;
        var discoveryMode = ParseDiscoveryMode(config.Discovery.Mode);
        var maxMemoryBytes = config.Limits.GetMaxMemoryBytesValue();
        var verbose = false;
        var debug = false;
        var initConfig = false;
        var validateConfig = false;
        var dumpConfig = false;

        var state = ParseState.None;
        var onlyPaths = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (onlyPaths)
            {
                paths.Add(arg.Replace('\\', '/'));
                continue;
            }

            if (arg == "--")
            {
                onlyPaths = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsHelpFlag())
            {
                showHelp = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsTestFlag())
            {
                runTests = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsBenchmarkFlag())
            {
                runRealBenchmark = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsVerboseFlag())
            {
                verbose = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsDebugFlag())
            {
                debug = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsInitConfigFlag())
            {
                initConfig = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsValidateConfigFlag())
            {
                validateConfig = true;
                state = ParseState.None;
                continue;
            }

            if (arg.IsDumpConfigFlag())
            {
                dumpConfig = true;
                state = ParseState.None;
                continue;
            }

            if (arg.TryGetNewState(out var newState))
            {
                state = newState;
                continue;
            }

            if (arg.TryGetNewDiscoveryState(out var discoveryState))
            {
                state = discoveryState;
                continue;
            }

            if (state != ParseState.None)
            {
                arg.ProcessArg(state, paths, extensions, excludes, presets, ref output, ref discoveryMode,
                    ref maxMemoryBytes);

                if (state == ParseState.Output || state == ParseState.MaxMemory || state == ParseState.Discovery)
                    state = ParseState.None;
            }
            else
            {
                if (!arg.StartsWith("-"))
                    paths.Add(arg.Replace('\\', '/'));
                else
                    Console.Error.WriteLine($"Warning: Unrecognized option: {arg}");
            }
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
            debug,
            initConfig,
            validateConfig,
            dumpConfig,
            config);
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

    private static bool IsInitConfigFlag(this string arg)
    {
        return string.Equals(arg, "--init-config", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidateConfigFlag(this string arg)
    {
        return string.Equals(arg, "--validate-config", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDumpConfigFlag(this string arg)
    {
        return string.Equals(arg, "--dump-config", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetNewState(this string arg, out ParseState state)
    {
        state = ParseState.None;

        if (string.Equals(arg, "-p", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--paths", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Paths;
            return true;
        }

        if (string.Equals(arg, "-e", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--extension", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--extensions", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Extensions;
            return true;
        }

        if (string.Equals(arg, "-x", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--exclude", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--excludes", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Excludes;
            return true;
        }

        if (string.Equals(arg, "--preset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--presets", StringComparison.OrdinalIgnoreCase))
        {
            state = ParseState.Presets;
            return true;
        }

        if (string.Equals(arg, "-o", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase))
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

    private static void ProcessArg(this string arg, ParseState state, List<string> paths, List<string> extensions,
        List<string> excludes, List<string> presets, ref string output, ref DiscoveryMode discoveryMode,
        ref long maxMemoryBytes)
    {
        switch (state)
        {
            case ParseState.Paths:
                paths.Add(arg.Replace('\\', '/'));
                break;
            case ParseState.Extensions:
                if (arg.Contains(","))
                    foreach (var ext in arg.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        extensions.Add(ext.Trim().TrimStart('.').ToLowerInvariant());
                else
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
        }
    }

    private static long ParseMemorySize(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 104857600;

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

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return (long)(value * multiplier);

        return 104857600;
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
}