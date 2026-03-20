using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

public interface IMarkdownGenerator
{
    Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents, Stream outputStream, GcConfiguration config, CancellationToken ct = default);
    Task<Result<string>> GenerateMarkdownAsync(IEnumerable<FileContent> contents, GcConfiguration config, CancellationToken ct = default);
}
