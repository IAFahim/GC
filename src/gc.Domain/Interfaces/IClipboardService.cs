using gc.Domain.Common;

namespace gc.Domain.Interfaces;

public interface IClipboardService
{
    Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default);
    Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default);
}
