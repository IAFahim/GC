using gc.Domain.Common;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

public interface IClipboardService
{
    Task<Result> CopyToClipboardAsync(Stream stream, CancellationToken ct = default);
    Task<Result> CopyToClipboardAsync(Stream stream, LimitsConfiguration limits, bool append = false, CancellationToken ct = default);
    Task<Result> CopyToClipboardAsync(string content, CancellationToken ct = default);
    Task<Result> CopyToClipboardAsync(string content, LimitsConfiguration limits, bool append = false, CancellationToken ct = default);
}
