using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using System.Text;

namespace gc.Tests;

public class HistoryMenuTests
{
    /// <summary>
    /// Manual stub for IHistoryService.
    /// </summary>
    private class StubHistoryService : IHistoryService
    {
        private readonly Result<IReadOnlyList<HistoryEntry>> _getResult;
        private readonly Result _addResult;

        public StubHistoryService(Result<IReadOnlyList<HistoryEntry>> getResult, Result? addResult = null)
        {
            _getResult = getResult;
            _addResult = addResult ?? Result.Success();
        }

        public Task<Result> AddEntryAsync(string directory, string[] arguments, CancellationToken ct = default)
            => Task.FromResult(_addResult);

        public Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(CancellationToken ct = default)
            => Task.FromResult(_getResult);

        public Task<Result> ClearHistoryAsync(CancellationToken ct = default)
            => Task.FromResult(Result.Success());
    }

    private static HistoryEntry MakeEntry(string dir, string[] args, DateTime? lastRun = null)
        => new(dir, args, lastRun ?? DateTime.UtcNow);

    private static (string output, int exitCode) RunShowAsync(
        IReadOnlyList<HistoryEntry>? history,
        string? consoleInput = null,
        int? preselectedIndex = null,
        bool historyFails = false,
        string? historyError = null)
    {
        var parser = new CliParser();
        var config = new GcConfiguration();

        var historyService = historyFails
            ? new StubHistoryService(
                Result<IReadOnlyList<HistoryEntry>>.Failure(historyError ?? "load error"))
            : new StubHistoryService(
                Result<IReadOnlyList<HistoryEntry>>.Success(history ?? Array.Empty<HistoryEntry>()));

        var originalOut = Console.Out;
        var originalIn = Console.In;
        var originalCwd = Environment.CurrentDirectory;

        try
        {
            var sb = new StringBuilder();
            var stringWriter = new StringWriter(sb);
            Console.SetOut(stringWriter);

            if (consoleInput != null)
            {
                Console.SetIn(new StringReader(consoleInput + Environment.NewLine));
            }

            var task = HistoryMenu.ShowAsync(
                historyService, parser, config,
                preselectedIndex, CancellationToken.None);

            // Synchronous wait is fine since our stub completes immediately
            var exitCode = task.GetAwaiter().GetResult();

            stringWriter.Flush();
            return (sb.ToString(), exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetIn(originalIn);
            Environment.CurrentDirectory = originalCwd;
        }
    }

    // =========================================================
    // 1. Basic menu: empty / single / multiple entries
    // =========================================================

    [Fact]
    public void ShowAsync_EmptyHistory_ReturnsZero_AndPrintsNoHistory()
    {
        var (output, exitCode) = RunShowAsync(Array.Empty<HistoryEntry>());

        Assert.Equal(0, exitCode);
        Assert.Contains("No history found", output);
    }

    [Fact]
    public void ShowAsync_HistoryServiceFails_ReturnsOne_AndPrintsError()
    {
        var (output, exitCode) = RunShowAsync(
            history: null,
            historyFails: true,
            historyError: "disk corrupted");

        Assert.Equal(1, exitCode);
        Assert.Contains("disk corrupted", output);
    }

    [Fact]
    public void ShowAsync_SingleEntry_DisplaysEntry()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/test", ["--verbose"])
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "");

        Assert.Equal(0, exitCode);
        Assert.Contains("Recent GC Runs", output);
        Assert.Contains("/project/test", output);
        Assert.Contains("--verbose", output);
    }

    [Fact]
    public void ShowAsync_MultipleEntries_DisplaysAll()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/alpha", ["clone", "repo-a"]),
            MakeEntry("/project/beta", ["clone", "repo-b"]),
            MakeEntry("/project/gamma", ["clone", "repo-c"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "");

        Assert.Equal(0, exitCode);
        Assert.Contains("/project/alpha", output);
        Assert.Contains("/project/beta", output);
        Assert.Contains("/project/gamma", output);
        Assert.Contains("[1]", output);
        Assert.Contains("[2]", output);
        Assert.Contains("[3]", output);
    }

    // =========================================================
    // 2. Preselected index
    // =========================================================

    [Fact]
    public void ShowAsync_PreselectedIndexTooLow_ReturnsOne()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/a", ["run"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, preselectedIndex: 0);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid history index", output);
    }

    [Fact]
    public void ShowAsync_PreselectedIndexTooHigh_ReturnsOne()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/a", ["run"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, preselectedIndex: 5);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid history index", output);
    }

    [Fact]
    public void ShowAsync_PreselectedIndex_Negative_ReturnsOne()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/a", ["run"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, preselectedIndex: -1);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid history index", output);
    }

    [Fact]
    public void ShowAsync_PreselectedIndexOne_Accepted()
    {
        // Preselected index 1 is valid (1-based).
        // Execution will attempt to change CWD to the entry's directory,
        // which likely doesn't exist on disk, so it should return 1 for
        // "Directory no longer exists" unless we create it.
        // We just verify it gets past the validation (no "Invalid history index").
        var tempDir = Path.Combine(Path.GetTempPath(), "gc-test-hm-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var entries = new List<HistoryEntry>
            {
                MakeEntry(tempDir, ["--verbose"]),
            };

            var (output, exitCode) = RunShowAsync(
                entries, preselectedIndex: 1);

            // Should NOT contain the invalid-index message
            Assert.DoesNotContain("Invalid history index", output);
            // Should contain "Re-running" since the directory exists
            Assert.Contains("Re-running", output);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // =========================================================
    // 3. Interactive mode input handling
    // =========================================================

    [Fact]
    public void ShowAsync_InteractiveEmptyInput_ReturnsZero()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/a", ["run"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ShowAsync_InteractiveInvalidNumber_ReturnsZero()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/a", ["run"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "abc");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void ShowAsync_InteractiveOutOfRangeNumber_ReturnsZero()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/a", ["run"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "99");

        Assert.Equal(0, exitCode);
    }

    // =========================================================
    // 4. Display formatting / edge cases
    // =========================================================

    [Fact]
    public void ShowAsync_EntryWithNoArgs_DisplaysNoArgs()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/empty", Array.Empty<string>()),
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "");

        Assert.Equal(0, exitCode);
        Assert.Contains("(no args)", output);
    }

    [Fact]
    public void ShowAsync_SpecialCharactersInPath_DisplaysCorrectly()
    {
        var specialPath = "/path/with spaces/and-unicode-\u00e9\u00e8\u00ea\u00eb";
        var entries = new List<HistoryEntry>
        {
            MakeEntry(specialPath, ["--flag=\u4e16\u754c", "emoji-\ud83d\ude00"]),
        };

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "");

        Assert.Equal(0, exitCode);
        Assert.Contains(specialPath, output);
        Assert.Contains("\u4e16\u754c", output);
        Assert.Contains("\ud83d\ude00", output);
    }

    [Fact]
    public void ShowAsync_LongHistory_DisplaysAllEntries()
    {
        const int count = 50;
        var entries = new List<HistoryEntry>();
        for (int i = 0; i < count; i++)
        {
            entries.Add(MakeEntry($"/long/project-{i}", [$"arg-{i}"]));
        }

        var (output, exitCode) = RunShowAsync(
            entries, consoleInput: "");

        Assert.Equal(0, exitCode);
        // All entries should be displayed (1-based indices)
        Assert.Contains("[1]", output);
        Assert.Contains($"[{count}]", output);
        Assert.Contains("/long/project-0", output);
        Assert.Contains($"/long/project-{count - 1}", output);
    }

    [Fact]
    public void ShowAsync_PrintsCorrectIndexFormat()
    {
        // Verify 1-based indexing in display
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/first", ["a1"]),
            MakeEntry("/second", ["a2"]),
        };

        var (output, _) = RunShowAsync(entries, consoleInput: "");

        // Should show [1] for first and [2] for second (1-based)
        Assert.Contains("[1] /first", output);
        Assert.Contains("[2] /second", output);
    }
}
