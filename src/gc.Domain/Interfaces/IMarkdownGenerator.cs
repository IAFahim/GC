using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

public interface IMarkdownGenerator
{
    Task<Result<long>> GenerateMarkdownStreamingAsync(IEnumerable<FileContent> contents, Stream outputStream, GcConfiguration config, IEnumerable<string>? excludeLineIfStart = null, CancellationToken ct = default);
}
