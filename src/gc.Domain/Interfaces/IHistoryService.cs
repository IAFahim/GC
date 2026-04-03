using gc.Domain.Common;
using gc.Domain.Models.Configuration;

namespace gc.Domain.Interfaces;

public interface IHistoryService
{
    Task<Result> AddEntryAsync(string directory, string[] arguments, CancellationToken ct = default);
    Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(CancellationToken ct = default);
    Task<Result> ClearHistoryAsync(CancellationToken ct = default);
}
