using gc.Domain.Common;
using gc.Domain.Models;

namespace gc.Domain.Interfaces;

public interface IFileReader
{
    Task<Result<Stream>> ReadStreamingAsync(string path, CancellationToken ct = default);
    Task<Result<FileContent>> ReadAsync(FileEntry entry, CancellationToken ct = default);
}
