namespace gc.Domain.Models.Configuration;

/// <summary>
/// Configuration for cluster mode — discovering and processing multiple git repos
/// inside a parent directory as a batch.
/// </summary>
public sealed record ClusterConfiguration
{
    /// <summary>
    /// Whether cluster mode is enabled. When true, scans the working directory
    /// (or --cluster-dir) for git repositories and processes them all.
    /// </summary>
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Maximum depth to scan for git repos inside the cluster directory.
    /// Default is 2 (e.g., clusterdir/repo or clusterdir/group/repo).
    /// </summary>
    public int MaxDepth { get; init; } = 2;

    /// <summary>
    /// Separator string used in merged output between repo sections.
    /// Default is a horizontal rule.
    /// </summary>
    public string RepoSeparator { get; init; } = "---";

    /// <summary>
    /// Include the repo name as a header before each repo's files in the output.
    /// </summary>
    public bool IncludeRepoHeader { get; init; } = true;

    /// <summary>
    /// Maximum number of repos to process in parallel.
    /// Default is processor count.
    /// </summary>
    public int MaxParallelRepos { get; init; } = 0;

    /// <summary>
    /// Directory names to skip when scanning for repos (e.g. "archive", "deprecated").
    /// </summary>
    public string[] SkipDirectories { get; init; } = [];

    /// <summary>
    /// If true, also processes loose files found in the cluster root itself
    /// (not inside any git repo). Default false.
    /// </summary>
    public bool IncludeRootFiles { get; init; } = false;

    /// <summary>
    /// If true, stops processing remaining repos when the first error occurs.
    /// Default false — continues and reports errors at the end.
    /// </summary>
    public bool FailFast { get; init; } = false;
}
