using System.Text.Json;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.System;

public sealed class HistoryService : IHistoryService
{
    private readonly string _historyFilePath;
    private const int MaxHistoryEntries = 50;
    private readonly ILogger _logger;
    private readonly object _fileLock = new();

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

            // Remove duplicate entry (same directory + arguments)
            entries.RemoveAll(e =>
                string.Equals(e.Directory, directory, StringComparison.Ordinal) &&
                e.Arguments.SequenceEqual(arguments));

            // Insert at top
            entries.Insert(0, new HistoryEntry(directory, arguments, DateTime.UtcNow));

            // Cap the list
            if (entries.Count > MaxHistoryEntries)
                entries.RemoveRange(MaxHistoryEntries, entries.Count - MaxHistoryEntries);

            await SaveInternalAsync(entries, ct);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to save history: {ex.Message}");
            // History saving should never crash the main operation
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(CancellationToken ct = default)
    {
        try
        {
            var entries = await LoadInternalAsync(ct);

            // Filter out deleted directories
            var pruned = entries.Where(e => Directory.Exists(e.Directory)).ToList();

            // Sort by LastRun descending
            pruned.Sort((a, b) => b.LastRun.CompareTo(a.LastRun));

            // Save pruned list back to keep file clean
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

        lock (_fileLock)
        {
            var json = File.ReadAllText(_historyFilePath);
            if (string.IsNullOrWhiteSpace(json))
                return [];
            var typeInfo = GcJsonSerializerContext.Default.ListHistoryEntry;
            return JsonSerializer.Deserialize(json, typeInfo)?.ToList() ?? [];
        }
    }

    private async Task SaveInternalAsync(List<HistoryEntry> entries, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        lock (_fileLock)
        {
            var typeInfo = GcJsonSerializerContext.Default.ListHistoryEntry;
            var json = JsonSerializer.Serialize(entries, typeInfo);
            File.WriteAllText(_historyFilePath, json);
        }

        await Task.CompletedTask;
    }
}
