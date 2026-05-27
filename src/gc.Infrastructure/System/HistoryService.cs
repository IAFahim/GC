using System.IO;
using System.Text.Json;
using System.Threading;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.System;

public sealed class HistoryService : IHistoryService
{
    private readonly string _historyFilePath;
    private const int MaxHistoryEntries = 50;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public HistoryService(string configDirectory, ILogger logger)
    {
        _historyFilePath = Path.Combine(configDirectory, "history.json");
        _logger = logger;
    }

    public async Task<Result> AddEntryAsync(string directory, string[] arguments, CancellationToken ct = default)
    {
        try
        {
            var entries = await LoadInternalAsync(ct);

            entries.RemoveAll(e =>
                string.Equals(e.Directory, directory, StringComparison.Ordinal) &&
                e.Arguments.SequenceEqual(arguments));

            entries.Insert(0, new HistoryEntry(directory, arguments, DateTime.UtcNow));

            if (entries.Count > MaxHistoryEntries)
                entries.RemoveRange(MaxHistoryEntries, entries.Count - MaxHistoryEntries);

            await SaveInternalAsync(entries, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to save history: {ex.Message}");
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var entries = await LoadInternalAsync(ct);

            var pruned = entries.Where(e => Directory.Exists(e.Directory)).ToList();

            pruned.Sort((a, b) => b.LastRun.CompareTo(a.LastRun));

            if (pruned.Count != entries.Count)
                await SaveInternalAsync(pruned, ct);

            return Result<IReadOnlyList<HistoryEntry>>.Success(pruned);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<HistoryEntry>>.Failure(ex.Message);
        }
    }

    public async Task<Result> ClearHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            await SaveInternalAsync([], ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    private async Task<List<HistoryEntry>> LoadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_historyFilePath))
            return [];

        await _semaphore.WaitAsync(ct);
        try
        {
            await using var fs = new FileStream(_historyFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            using var reader = new StreamReader(fs);
            var json = await reader.ReadToEndAsync(ct);
            if (string.IsNullOrWhiteSpace(json))
                return [];
            var typeInfo = GcJsonSerializerContext.Default.ListHistoryEntry;
            return JsonSerializer.Deserialize(json, typeInfo)?.ToList() ?? [];
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SaveInternalAsync(List<HistoryEntry> entries, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await _semaphore.WaitAsync(ct);
        try
        {
            await using var fs = new FileStream(_historyFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using var writer = new StreamWriter(fs);
            var typeInfo = GcJsonSerializerContext.Default.ListHistoryEntry;
            var json = JsonSerializer.Serialize(entries, typeInfo);
            await writer.WriteAsync(json);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
