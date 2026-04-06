using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

/// <summary>
/// Service for discovering files in a directory or git repository.
/// </summary>
public interface IFileDiscovery
{
    /// <summary>
    /// Discovers files in a git repository or directory based on configuration.
    /// </summary>
    /// <param name="rootPath">Absolute path to the repository/directory root.</param>
    /// <param name="config">Configuration controlling discovery behavior (extensions, excludes, etc).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing discovered file paths, or failure with error message.</returns>
    Task<Result<IEnumerable<string>>> DiscoverFilesAsync(string rootPath, GcConfiguration config, CancellationToken ct = default);

    /// <summary>
    /// Discovers all git repositories within a cluster directory.
    /// Scans the directory tree looking for .git folders and returns info about each repo found.
    /// </summary>
    /// <param name="clusterRoot">Absolute path to the parent directory containing repos.</param>
    /// <param name="clusterConfig">Cluster-specific configuration (depth, skip dirs, etc).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing list of discovered repos, or failure with error message.</returns>
    Task<Result<IReadOnlyList<RepoInfo>>> DiscoverGitReposAsync(string clusterRoot, ClusterConfiguration clusterConfig, CancellationToken ct = default);
}
