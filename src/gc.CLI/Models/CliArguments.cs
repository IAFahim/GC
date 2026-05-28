using gc.Domain.Models.Configuration;

namespace gc.CLI.Models;

public sealed record CliArguments
{
    public string[] Paths { get; init; } = Array.Empty<string>();
    public string[] Extensions { get; init; } = Array.Empty<string>();
    public string[] Excludes { get; init; } = Array.Empty<string>();
    public string[] Presets { get; init; } = Array.Empty<string>();
    public string[] ExcludeLineIfStart { get; init; } = Array.Empty<string>();
    public string OutputFile { get; init; } = string.Empty;
    public bool ShowHelp { get; init; }
    public bool ShowVersion { get; init; }
    public bool RunTests { get; init; }
    public bool RunRealBenchmark { get; init; }
    public long MaxMemoryBytes { get; init; }
    public bool Verbose { get; init; }
    public bool Debug { get; init; }
    public bool InitConfig { get; init; }
    public bool ValidateConfig { get; init; }
    public bool DumpConfig { get; init; }
    public bool Append { get; init; }
    public bool NoSort { get; init; }
    public bool Force { get; init; }
    public int? Depth { get; init; }
    public bool ShowHistory { get; init; }
    public int? HistoryIndex { get; init; }
    public GcConfiguration? Configuration { get; init; }

    public bool BrainMode { get; init; }

    public bool Compress { get; init; }

    public bool NoCache { get; init; }

    public string[] ExcludePathPatterns { get; init; } = Array.Empty<string>();
    public string[] IncludePathPatterns { get; init; } = Array.Empty<string>();
    public string[] ExcludeContentPatterns { get; init; } = Array.Empty<string>();
    public string[] IncludeContentPatterns { get; init; } = Array.Empty<string>();

    public bool DryRun { get; init; }

    public bool CountTokens { get; init; }

    public bool Profile { get; init; }

    public string? ProfileOutput { get; init; }

    public bool Cluster { get; init; }

    public string ClusterDir { get; init; } = string.Empty;

    public int? ClusterDepth { get; init; }
}
