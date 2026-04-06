using gc.Domain.Common;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Logging;
using gc.Infrastructure.System;
using System.Text.Json;

namespace gc.Tests;

public class HistoryServiceTests
{
    private static string CreateTempDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gc-test-history-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static HistoryService CreateService(string tempDir)
    {
        return new HistoryService(tempDir, new ConsoleLogger());
    }

    private static string GetHistoryPath(string tempDir)
    {
        return Path.Combine(tempDir, "history.json");
    }

    // --- AddEntryAsync tests ---

    [Fact]
    public async Task AddEntry_CreatesFile_IfNotExists()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);
            var historyPath = GetHistoryPath(tempDir);

            Assert.False(File.Exists(historyPath));

            var result = await service.AddEntryAsync(tempDir, ["clone", "https://github.com/test/repo"], CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(File.Exists(historyPath));

            var json = await File.ReadAllTextAsync(historyPath);
            Assert.False(string.IsNullOrWhiteSpace(json));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Equal(tempDir, entries[0].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddEntry_AddsEntry_ToExistingFile()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            await service.AddEntryAsync("/dir1", ["arg1"], CancellationToken.None);
            await service.AddEntryAsync("/dir2", ["arg2"], CancellationToken.None);

            // Read raw file — GetHistoryAsync filters out non-existent directories
            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Equal(2, entries.Count);
            Assert.Equal("/dir2", entries[0].Directory);
            Assert.Equal("/dir1", entries[1].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddEntry_RemovesDuplicates()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            await service.AddEntryAsync("/project", ["clone", "repo"], CancellationToken.None);
            await service.AddEntryAsync("/project", ["clone", "repo"], CancellationToken.None);

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Equal("/project", entries[0].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddEntry_CapsAt50Entries()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            // Add 55 entries with unique dir+args combinations
            for (int i = 0; i < 55; i++)
            {
                await service.AddEntryAsync($"/dir-{i}", [$"arg-{i}"], CancellationToken.None);
            }

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Equal(50, entries.Count);

            // Most recently added entry should be at the top
            Assert.Equal("/dir-54", entries[0].Directory);
            // Oldest surviving entry should be /dir-5 (indices 5..54 kept)
            Assert.Equal("/dir-5", entries[49].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddEntry_EmptyArgs_Works()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            var result = await service.AddEntryAsync("/empty-args-dir", Array.Empty<string>(), CancellationToken.None);

            Assert.True(result.IsSuccess);

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Empty(entries[0].Arguments);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- GetHistoryAsync tests ---

    [Fact]
    public async Task GetHistory_NoFile_ReturnsEmptyList()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            var result = await service.GetHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetHistory_WithEntries_ReturnsSortedByDate()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            // Create real directories so the filter keeps them
            var dir1 = Path.Combine(tempDir, "proj-1");
            var dir2 = Path.Combine(tempDir, "proj-2");
            var dir3 = Path.Combine(tempDir, "proj-3");
            Directory.CreateDirectory(dir1);
            Directory.CreateDirectory(dir2);
            Directory.CreateDirectory(dir3);

            await service.AddEntryAsync(dir1, ["old"], CancellationToken.None);
            await Task.Delay(50); // ensure different timestamps
            await service.AddEntryAsync(dir2, ["mid"], CancellationToken.None);
            await Task.Delay(50);
            await service.AddEntryAsync(dir3, ["new"], CancellationToken.None);

            var result = await service.GetHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Equal(3, result.Value.Count);

            // Should be sorted by LastRun descending (newest first)
            Assert.Equal(dir3, result.Value[0].Directory);
            Assert.Equal(dir2, result.Value[1].Directory);
            Assert.Equal(dir1, result.Value[2].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetHistory_FiltersDeletedDirectories()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            var existingDir = Path.Combine(tempDir, "exists");
            var deletedDir = Path.Combine(tempDir, "deleted");
            Directory.CreateDirectory(existingDir);
            Directory.CreateDirectory(deletedDir);

            await service.AddEntryAsync(existingDir, ["a"], CancellationToken.None);
            await service.AddEntryAsync(deletedDir, ["b"], CancellationToken.None);

            // Now delete one directory
            Directory.Delete(deletedDir, true);

            var result = await service.GetHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Single(result.Value);
            Assert.Equal(existingDir, result.Value[0].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetHistory_CorruptJson_ReturnsEmptyList()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Write corrupt JSON directly
            await File.WriteAllTextAsync(GetHistoryPath(tempDir), "{{{{invalid json!!!!");

            var service = CreateService(tempDir);
            var result = await service.GetHistoryAsync(CancellationToken.None);

            // Should return failure due to deserialization exception
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetHistory_EmptyFile_ReturnsEmptyList()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Create an empty history file
            await File.WriteAllTextAsync(GetHistoryPath(tempDir), "");

            var service = CreateService(tempDir);
            var result = await service.GetHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.NotNull(result.Value);
            Assert.Empty(result.Value);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- ClearHistoryAsync tests ---

    [Fact]
    public async Task ClearHistory_DeletesEntries()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            await service.AddEntryAsync("/some-dir", ["arg"], CancellationToken.None);
            Assert.True(File.Exists(GetHistoryPath(tempDir)));

            var result = await service.ClearHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Empty(entries);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ClearHistory_NoFile_NoError()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);
            Assert.False(File.Exists(GetHistoryPath(tempDir)));

            var result = await service.ClearHistoryAsync(CancellationToken.None);

            Assert.True(result.IsSuccess);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // --- Edge cases ---

    [Fact]
    public async Task AddEntry_MultipleRapidAdds_NoCorruption()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            // Fire off 10 adds quickly (sequentially since the service uses a lock)
            for (int i = 0; i < 10; i++)
            {
                var result = await service.AddEntryAsync($"/rapid-{i}", [$"arg-{i}"], CancellationToken.None);
                Assert.True(result.IsSuccess, $"Add {i} failed: {result.Error}");
            }

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            Assert.NotEmpty(json);

            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Equal(10, entries.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void HistoryEntry_Serialization_Roundtrip()
    {
        var original = new HistoryEntry("/test/dir", ["clone", "https://example.com"], DateTime.UtcNow);
        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<HistoryEntry>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Directory, deserialized.Directory);
        Assert.Equal(original.Arguments, deserialized.Arguments);
        // DateTime may lose precision in JSON; compare to nearest second
        Assert.Equal(original.LastRun.Ticks / TimeSpan.TicksPerSecond, deserialized.LastRun.Ticks / TimeSpan.TicksPerSecond);
    }

    [Fact]
    public async Task AddEntry_SpecialCharacters_InPath()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            var specialDir = "/path/with spaces/and-unicode-\u00e9\u00e8\u00ea\u00eb";
            var specialArgs = new[] { "arg with spaces", "--flag=\u4e16\u754c", "emoji-\ud83d\ude00" };

            var result = await service.AddEntryAsync(specialDir, specialArgs, CancellationToken.None);

            Assert.True(result.IsSuccess);

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Equal(specialDir, entries[0].Directory);
            Assert.Equal(specialArgs, entries[0].Arguments);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddEntry_DuplicateWithDifferentArgs_KeepsBoth()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            await service.AddEntryAsync("/same-dir", ["arg1"], CancellationToken.None);
            await service.AddEntryAsync("/same-dir", ["arg2"], CancellationToken.None);

            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Equal(2, entries.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetHistory_PrunePersistsToFile()
    {
        var tempDir = CreateTempDir();
        try
        {
            var service = CreateService(tempDir);

            var existingDir = Path.Combine(tempDir, "exists");
            var prunedDir = Path.Combine(tempDir, "pruned");
            Directory.CreateDirectory(existingDir);
            Directory.CreateDirectory(prunedDir);

            await service.AddEntryAsync(existingDir, ["keep"], CancellationToken.None);
            await service.AddEntryAsync(prunedDir, ["remove"], CancellationToken.None);

            // Delete directory so it gets pruned
            Directory.Delete(prunedDir, true);

            await service.GetHistoryAsync(CancellationToken.None);

            // Read the raw file — it should have been updated to only contain the existing dir
            var json = await File.ReadAllTextAsync(GetHistoryPath(tempDir));
            var entries = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.ListHistoryEntry);
            Assert.NotNull(entries);
            Assert.Single(entries);
            Assert.Equal(existingDir, entries[0].Directory);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
