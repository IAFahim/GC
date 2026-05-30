using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.CLI.Models;

/// <summary>
///     Mutable CLI argument bag — populated by the declarative CliParser table.
///     Properties use { get; set; } so the OptionSpec.Apply lambdas can write them.
/// </summary>
public sealed class CliArguments
{
    public string[] Paths { get; set; } = [];
    public string[] Extensions { get; set; } = [];
    public string[] Excludes { get; set; } = [];
    public string[] Presets { get; set; } = [];
    public string[] ExcludeLineIfStart { get; set; } = [];
    public string OutputFile { get; set; } = string.Empty;
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool RunTests { get; set; }
    public bool RunRealBenchmark { get; set; }
    public long MaxMemoryBytes { get; set; }
    public bool Verbose { get; set; }
    public bool Debug { get; set; }
    public bool InitConfig { get; set; }
    public bool ValidateConfig { get; set; }
    public bool DumpConfig { get; set; }
    public bool Append { get; set; }
    public bool NoSort { get; set; }
    public bool Force { get; set; }
    public bool NoClipboard { get; set; }
    public int? Depth { get; set; }
    public bool ShowHistory { get; set; }
    public int? HistoryIndex { get; set; }
    public GcConfiguration? Configuration { get; set; }

    public bool BrainMode { get; set; }

    public bool Compress { get; set; }

    public bool NoCache { get; set; }

    public string[] ExcludePathPatterns { get; set; } = [];
    public string[] IncludePathPatterns { get; set; } = [];
    public string[] ExcludeContentPatterns { get; set; } = [];
    public string[] IncludeContentPatterns { get; set; } = [];

    public bool DryRun { get; set; }

    public bool CountTokens { get; set; }

    public bool Profile { get; set; }

    public string? ProfileOutput { get; set; }

    public bool Cluster { get; set; }

    public string ClusterDir { get; set; } = string.Empty;

    public int? ClusterDepth { get; set; }

    public bool UnsafeDirectWrite { get; set; }

    public bool ShowStats { get; set; }

    public string? StatsOutput { get; set; }

    public string? ChangedSince { get; set; }

    public string? ExplainFilter { get; set; }

    public string? ExportSchema { get; set; }

    public ShardInfo? ShardInfo { get; set; }

    public string? ShardError { get; set; }
}