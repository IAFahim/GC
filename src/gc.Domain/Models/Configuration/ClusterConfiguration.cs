namespace gc.Domain.Models.Configuration;

public sealed record ClusterConfiguration
{
    // Omittable values are nullable so the merger can distinguish "omitted" (null -> keep the lower
    // config layer) from an explicit value (including an explicit 0). Concrete defaults live only in
    // BuiltInPresets.GetDefaultConfiguration().
    public bool? Enabled { get; init; }
    public int? MaxDepth { get; init; }
    public string? RepoSeparator { get; init; }
    public bool? IncludeRepoHeader { get; init; }
    public int? MaxParallelRepos { get; init; }
    public string[]? SkipDirectories { get; init; }
    public bool? IncludeRootFiles { get; init; }
    public bool? FailFast { get; init; }
}