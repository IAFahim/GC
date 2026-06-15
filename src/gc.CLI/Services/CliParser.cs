using gc.CLI.Models;
using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.CLI.Services;

// ========================================================================
// Declarative CLI parser — single option table, no ref-bool storm.
//
// Adding a flag: one entry in the OptionSpec table.
// Removing a flag: delete one entry.
// No flag appears in more than one place.
// ========================================================================

/// <summary>
///     Describes one CLI option: its token(s), kind, and how to apply it.
/// </summary>
internal readonly record struct OptionSpec(
    string[] Tokens,
    OptionKind Kind,
    Action<CliArguments, string?> Apply);

internal enum OptionKind
{
    /// A bare flag with no value (--verbose, --help).
    Flag,

    /// Expects exactly one following value (--output FILE).
    SingleValue,

    /// Accumulates multiple following values (--paths a b c).
    MultiValue
}

public sealed class CliParser
{
    // ── The single source of truth for all options ──
    // Static: the table, its delegates, and the token map are immutable and
    // identical for every parser, so build them once per process rather than
    // per `new CliParser()`.
    private static readonly OptionSpec[] Options =
    [
        // ── Flags ──
        new(["-h", "--help"], OptionKind.Flag, (a, _) => a.ShowHelp = true),
        new(["--version"], OptionKind.Flag, (a, _) => a.ShowVersion = true),
        new(["--test"], OptionKind.Flag, (a, _) => a.RunTests = true),
        new(["--benchmark"], OptionKind.Flag, (a, _) => a.RunRealBenchmark = true),
        new(["-v", "--verbose"], OptionKind.Flag, (a, _) => a.Verbose = true),
        new(["--debug"], OptionKind.Flag, (a, _) => a.Debug = true),
        new(["--init-config"], OptionKind.Flag, (a, _) => a.InitConfig = true),
        new(["--validate-config"], OptionKind.Flag, (a, _) => a.ValidateConfig = true),
        new(["--dump-config"], OptionKind.Flag, (a, _) => a.DumpConfig = true),
        new(["--append"], OptionKind.Flag, (a, _) => a.Append = true),
        new(["--no-append"], OptionKind.Flag, (a, _) => a.Append = false),
        new(["--no-sort"], OptionKind.Flag, (a, _) => a.NoSort = true),
        new(["-f", "--force"], OptionKind.Flag, (a, _) => a.Force = true),
        new(["--no-clipboard"], OptionKind.Flag, (a, _) => a.NoClipboard = true),
        new(["--history"], OptionKind.Flag, (a, _) => a.ShowHistory = true),
        new(["-b", "--brain", "brain"], OptionKind.Flag, (a, _) => a.BrainMode = true),
        new(["-c", "--compress", "compress"], OptionKind.Flag, (a, _) => a.Compress = true),
        new(["--no-cache"], OptionKind.Flag, (a, _) => a.NoCache = true),
        new(["--cluster"], OptionKind.Flag, (a, _) => a.Cluster = true),
        new(["--dry-run", "--list"], OptionKind.Flag, (a, _) => a.DryRun = true),
        new(["--count", "--tokens-only"], OptionKind.Flag, (a, _) => a.CountTokens = true),
        // Timing profiler. NOTE: bare "--profile <name>" is intercepted earlier by CliArgumentResolver
        // to apply a saved named profile, so the timing flag uses a distinct token to avoid a collision.
        new(["--profile-timing"], OptionKind.Flag, (a, _) => a.Profile = true),
        new(["--profile-json"], OptionKind.SingleValue, (a, v) =>
        {
            a.Profile = true;
            a.ProfileOutput = v;
        }),
        new(["--unsafe-direct-write"], OptionKind.Flag, (a, _) => a.UnsafeDirectWrite = true),
        new(["--stats"], OptionKind.Flag, (a, _) => a.ShowStats = true),
        new(["--json-stats"], OptionKind.SingleValue, (a, v) =>
        {
            a.ShowStats = true;
            a.StatsOutput = v;
        }),

        // ── Single-value options ──
        new(["-s", "-o", "--output", "spit"], OptionKind.SingleValue, (a, v) => a.OutputFile = v ?? ""),
        new(["--max-memory"], OptionKind.SingleValue, (a, v) =>
        {
            // Fail loud, not open: an unparseable value must not silently clobber the configured
            // limit with the 100MB default (MemorySizeParser.Parse falls back). Keep the seeded
            // value and record an error the caller surfaces.
            if (MemorySizeParser.TryParse(v ?? "", out var bytes)) a.MaxMemoryBytes = bytes;
            else a.MaxMemoryError = v ?? "(empty)";
        }),
        new(["-d", "--depth"], OptionKind.SingleValue, (a, v) =>
        {
            if (int.TryParse(v, out var d)) a.Depth = d;
        }),
        new(["--cluster-dir"], OptionKind.SingleValue, (a, v) => a.ClusterDir = (v ?? "").Replace('\\', '/')),
        new(["--cluster-depth"], OptionKind.SingleValue, (a, v) =>
        {
            if (int.TryParse(v, out var cd) && cd > 0) a.ClusterDepth = cd;
        }),
        new(["--changed-since"], OptionKind.SingleValue, (a, v) => a.ChangedSince = v),
        new(["--export-schema"], OptionKind.SingleValue, (a, v) => a.ExportSchema = v),
        new(["--explain-filter"], OptionKind.SingleValue, (a, v) => a.ExplainFilter = v),
        new(["--shard"], OptionKind.SingleValue, (a, v) =>
        {
            if (v != null) a.ShardInfo = ShardInfo.TryParse(v);
            if (a.ShardInfo == null) a.ShardError = v ?? "(empty)";
        }),

        // ── Multi-value options (accumulate) ──
        new(["-g", "-p", "--paths", "grab"], OptionKind.MultiValue,
            (a, v) => a.Paths = [..a.Paths, (v ?? "").Replace('\\', '/')]),
        new(["-t", "-e", "--extension", "--extensions", "type"], OptionKind.MultiValue,
            (a, v) => ProcessExtensions(v ?? "", a)),
        new(["-y", "-x", "--exclude", "--excludes", "yeet"], OptionKind.MultiValue,
            (a, v) => a.Excludes = [..a.Excludes, (v ?? "").Replace('\\', '/')]),
        new(["--preset", "--presets"], OptionKind.MultiValue,
            (a, v) => a.Presets = [..a.Presets, (v ?? "").ToLowerInvariant()]),
        new(["-z", "--exclude-line-if-start", "zap"], OptionKind.MultiValue, (a, v) =>
        {
            var val = v == "\\n" ? "\n" : v;
            a.ExcludeLineIfStart = [..a.ExcludeLineIfStart, val ?? ""];
        }),
        new(["--exclude-path"], OptionKind.MultiValue,
            (a, v) => a.ExcludePathPatterns = [..a.ExcludePathPatterns, (v ?? "").Replace('\\', '/')]),
        new(["--include-path"], OptionKind.MultiValue,
            (a, v) => a.IncludePathPatterns = [..a.IncludePathPatterns, (v ?? "").Replace('\\', '/')]),
        new(["--exclude-content"], OptionKind.MultiValue,
            (a, v) => a.ExcludeContentPatterns = [..a.ExcludeContentPatterns, v ?? ""]),
        new(["--include-content"], OptionKind.MultiValue,
            (a, v) => a.IncludeContentPatterns = [..a.IncludeContentPatterns, v ?? ""]),

        // ── Bare-word verbs ──
        new(["horde"], OptionKind.Flag, (a, _) => a.Cluster = true)
    ];

    private static readonly Dictionary<string, OptionSpec> TokenMap = BuildTokenMap();

    private static Dictionary<string, OptionSpec> BuildTokenMap()
    {
        var map = new Dictionary<string, OptionSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in Options)
            foreach (var token in spec.Tokens)
                map[token] = spec;
        return map;
    }

    public Result<CliArguments> Parse(string[] args, GcConfiguration configuration)
    {
        var result = new CliArguments
        {
            Configuration = configuration,
            MaxMemoryBytes = configuration.Limits.GetMaxMemoryBytesValue(),
            Depth = configuration.Discovery.MaxDepth
        };

        var onlyPaths = false;
        OptionSpec? pendingSingleValue = null;
        OptionSpec? activeMultiValue = null;
        var unknownFlags = new List<string>();

        foreach (var arg in args)
        {
            // After --, everything is a path
            if (onlyPaths)
            {
                result.Paths = [.. result.Paths, arg.Replace('\\', '/')];
                continue;
            }

            if (arg == "--")
            {
                onlyPaths = true;
                continue;
            }

            // If waiting for a single value, consume it
            if (pendingSingleValue != null)
            {
                // If the next arg looks like a known flag but we expected a value,
                // that's an error.
                if (arg.StartsWith('-') && TokenMap.TryGetValue(arg, out var flagSpec) &&
                    flagSpec.Kind == OptionKind.Flag)
                {
                    var flagName = pendingSingleValue.Value.Tokens.First();
                    return Result<CliArguments>.Failure($"Missing value for {flagName} before {arg}");
                }

                pendingSingleValue.Value.Apply(result, arg);
                pendingSingleValue = null;
                activeMultiValue = null;
                continue;
            }

            // Table lookup
            if (TokenMap.TryGetValue(arg, out var spec))
            {
                switch (spec.Kind)
                {
                    case OptionKind.Flag:
                        spec.Apply(result, null);
                        activeMultiValue = null;
                        break;
                    case OptionKind.SingleValue:
                        pendingSingleValue = spec;
                        activeMultiValue = null;
                        break;
                    case OptionKind.MultiValue:
                        activeMultiValue = spec;
                        break;
                }

                continue;
            }

            // If we're in a multi-value state, accumulate
            if (activeMultiValue != null)
            {
                activeMultiValue.Value.Apply(result, arg);
                continue;
            }

            // Unknown token
            if (arg.StartsWith('-') && arg.Length > 1 && !char.IsDigit(arg[1]))
            {
                unknownFlags.Add(arg);
                continue;
            }

            // Default: treat bare words as paths (but handle history index)
            if (result.ShowHistory && result.HistoryIndex is null && int.TryParse(arg, out var idx) && idx > 0)
            {
                result.HistoryIndex = idx;
                continue;
            }

            result.Paths = [.. result.Paths, arg.Replace('\\', '/')];
        }

        // Missing value for a single-value option
        if (pendingSingleValue != null)
        {
            var flagName = pendingSingleValue.Value.Tokens.FirstOrDefault() ?? "unknown";
            return Result<CliArguments>.Failure($"Missing value for argument: {flagName}");
        }

        if (unknownFlags.Count > 0)
        {
            return Result<CliArguments>.Failure($"Unrecognized option: {string.Join(", ", unknownFlags)}");
        }

        return Result<CliArguments>.Success(result);
    }

    private static void ProcessExtensions(string arg, CliArguments result)
    {
        if (arg.Contains(','))
        {
            var split = arg.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var ext in split)
                result.Extensions = [.. result.Extensions, ext.Trim().TrimStart('.').ToLowerInvariant()];
        }
        else
        {
            result.Extensions = [.. result.Extensions, arg.TrimStart('.').ToLowerInvariant()];
        }
    }
}