namespace gc.Domain.Models;

/// <summary>
/// Represents a discovered git repository within a cluster directory.
/// </summary>
public sealed record RepoInfo
{
    /// <summary>
    /// Absolute path to the repository root (where .git/ lives).
    /// </summary>
    public string RootPath { get; init; } = string.Empty;

    /// <summary>
    /// Relative path from the cluster root directory to this repo.
    /// e.g. "services/api", "libs/core"
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Directory name of the repo (last segment of the path).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Whether this repo was successfully validated as a git repository.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed, null otherwise.
    /// </summary>
    public string? Error { get; init; }
}
