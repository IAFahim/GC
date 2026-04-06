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

    // Cluster mode options
    /// <summary>
    /// Enable cluster mode — scan for multiple git repos in the working directory.
    /// </summary>
    public bool Cluster { get; init; }

    /// <summary>
    /// Specify a different directory to scan for repos (instead of CWD).
    /// </summary>
    public string ClusterDir { get; init; } = string.Empty;

    /// <summary>
    /// Maximum depth to scan for repos within the cluster directory.
    /// </summary>
    public int? ClusterDepth { get; init; }
}
