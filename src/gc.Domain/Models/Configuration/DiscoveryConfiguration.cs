namespace gc.Domain.Models.Configuration;

/// <summary>
/// Controls how files are discovered in a repository or directory.
/// </summary>
public sealed record DiscoveryConfiguration
{
    /// <summary>
    /// Discovery mode: "auto" (try git, fallback to filesystem), "git", or "filesystem".
    /// </summary>
    public string Mode { get; init; } = "auto";

    /// <summary>
    /// Whether to use git ls-files for discovery when in a git repository.
    /// </summary>
    public bool UseGit { get; init; } = true;

    /// <summary>
    /// Whether to follow symbolic links during filesystem discovery.
    /// </summary>
    public bool FollowSymlinks { get; init; } = false;

    /// <summary>
    /// Maximum directory depth to traverse during filesystem discovery.
    /// Null means no limit.
    /// </summary>
    public int? MaxDepth { get; init; }

    /// <summary>
    /// Cluster configuration for multi-repo batch processing.
    /// Null means cluster mode is disabled.
    /// </summary>
    public ClusterConfiguration? Cluster { get; init; }
}
