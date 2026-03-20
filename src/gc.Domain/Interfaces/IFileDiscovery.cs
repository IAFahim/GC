using gc.Domain.Common;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

public interface IFileDiscovery
{
    Task<Result<IEnumerable<string>>> DiscoverFilesAsync(string rootPath, GcConfiguration config, CancellationToken ct = default);
}
