using System;
using System.Collections.Generic;

namespace GC.Data;

public static class CliParserExtensions
{
    private enum ParseState
    {
        None,
        Paths,
        Extensions,
        Excludes,
        Presets,
        Output
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

            if (arg.TryGetNewState(out var newState))
            {
                state = newState;
                continue;
            }

            arg.ProcessArg(state, paths, extensions, excludes, presets, ref output);
        }

        return new CliArguments(
            paths.ToArray(),
            extensions.ToArray(),
            excludes.ToArray(),
            presets.ToArray(),
            output,
            showHelp,
            runTests);
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

        return false;
    }

    private static void ProcessArg(this string arg, ParseState state, List<string> paths, List<string> extensions, List<string> excludes, List<string> presets, ref string output)
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
            case ParseState.None:
                break;
        }
    }
}
