namespace gc.Domain.Models.Configuration;

public sealed record ClusterConfiguration
{
    public bool? Enabled { get; init; }
    public int MaxDepth { get; init; } = 2;
    public string RepoSeparator { get; init; } = "---";
    public bool? IncludeRepoHeader { get; init; }
    public int MaxParallelRepos { get; init; } = 0;
    public string[] SkipDirectories { get; init; } = [];
    public bool? IncludeRootFiles { get; init; }
    public bool? FailFast { get; init; }
}