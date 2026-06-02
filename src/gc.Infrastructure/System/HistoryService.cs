using System.Text.Json;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.System;

public sealed class HistoryService : IHistoryService
{
    private const int MaxHistoryEntries = 50;
    private readonly string _historyFilePath;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public HistoryService(string configDirectory, ILogger logger)
    {
        _historyFilePath = Path.Combine(configDirectory, "history.json");
        _logger = logger;
    }

    public async Task<Result> AddEntryAsync(string directory, string[] arguments, CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var entries = await LoadFromFileAsync();

            entries.RemoveAll(e =>
                string.Equals(e.Directory, directory, StringComparison.Ordinal) &&
                e.Arguments.SequenceEqual(arguments));

            entries.Insert(0, new HistoryEntry(directory, arguments, DateTime.UtcNow));

            if (entries.Count > MaxHistoryEntries)
                entries.RemoveRange(MaxHistoryEntries, entries.Count - MaxHistoryEntries);

            await SaveToFileAsync(entries);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to save history: {ex.Message}");
            return Result.Failure(ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            var entries = await LoadFromFileAsync();

            var pruned = entries.Where(e => Directory.Exists(e.Directory)).ToList();

            pruned.Sort((a, b) => b.LastRun.CompareTo(a.LastRun));

            if (pruned.Count != entries.Count)
                await SaveToFileAsync(pruned);

            return Result<IReadOnlyList<HistoryEntry>>.Success(pruned);
        }
        catch (Exception ex)
        {
            return Result<IReadOnlyList<HistoryEntry>>.Failure(ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<Result> ClearHistoryAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            await SaveToFileAsync([]);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure(ex.Message);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<List<HistoryEntry>> LoadFromFileAsync()
    {
        if (!File.Exists(_historyFilePath))
            return [];

        try
        {
            await using var fs = new FileStream(_historyFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096,
                FileOptions.Asynchronous);
            using var reader = new StreamReader(fs);
            var json = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(json))
                return [];
            var typeInfo = GcJsonSerializerContext.Default.ListHistoryEntry;
            return JsonSerializer.Deserialize(json, typeInfo)?.ToList() ?? [];
        }
        catch (JsonException ex)
        {
            _logger.Debug($"History file is corrupted, resetting. Error: {ex.Message}");
            try
            {
                await SaveToFileAsync([]);
            }
            catch
            {
                // Ignore failure to write during auto-heal
            }
            return [];
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to load history: {ex.Message}");
            return [];
        }
    }

    private async Task SaveToFileAsync(List<HistoryEntry> entries)
    {
        var directory = Path.GetDirectoryName(_historyFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var tmpPath = _historyFilePath + ".tmp." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096,
                             FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(fs))
            {
                var typeInfo = GcJsonSerializerContext.Default.ListHistoryEntry;
                var json = JsonSerializer.Serialize(entries, typeInfo);
                await writer.WriteAsync(json);
            }

            File.Move(tmpPath, _historyFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
                try
                {
                    File.Delete(tmpPath);
                }
                catch
                {
                }
        }
    }
}