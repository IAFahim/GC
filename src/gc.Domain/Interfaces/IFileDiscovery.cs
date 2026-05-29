using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

public interface IFileDiscovery
{
    Task<Result<IEnumerable<string>>> DiscoverFilesAsync(string rootPath, GcConfiguration config,
        CancellationToken ct = default);

    Task<Result<IEnumerable<string>>> DiscoverFilesSinceAsync(string rootPath, string reference, GcConfiguration config,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<RepoInfo>>> DiscoverGitReposAsync(string clusterRoot, ClusterConfiguration clusterConfig,
        CancellationToken ct = default);
}