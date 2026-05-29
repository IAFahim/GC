using System.Text;
using gc.CLI.Services;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

public class HistoryMenuTests
{
    private static HistoryEntry MakeEntry(string dir, string[] args, DateTime? lastRun = null)
    {
        return new HistoryEntry(dir, args, lastRun ?? DateTime.UtcNow);
    }

    private static async Task<(string output, int exitCode)> RunShowAsync(
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

        var mockConsole = new MockConsole(consoleInput);

        var exitCode = await HistoryMenu.ShowAsync(
            historyService, parser, config,
            preselectedIndex, mockConsole, CancellationToken.None);

        return (mockConsole.Output, exitCode);
    }

    [Fact]
    public async Task ShowAsync_EmptyHistory_ReturnsZero_AndPrintsNoHistory()
    {
        var (output, exitCode) = await RunShowAsync(Array.Empty<HistoryEntry>());

        Assert.Equal(0, exitCode);
        Assert.Contains("No history found", output);
    }

    [Fact]
    public async Task ShowAsync_HistoryServiceFails_ReturnsOne_AndPrintsError()
    {
        var (output, exitCode) = await RunShowAsync(
            null,
            historyFails: true,
            historyError: "disk corrupted");

        Assert.Equal(1, exitCode);
        Assert.Contains("disk corrupted", output);
    }

    [Fact]
    public async Task ShowAsync_SingleEntry_DisplaysEntry()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/test", ["--verbose"])
        };

        var (output, exitCode) = await RunShowAsync(
            entries, "");

        Assert.Equal(0, exitCode);
        Assert.Contains("Recent GC Runs", output);
        Assert.Contains("/project/test", output);
        Assert.Contains("--verbose", output);
    }

    [Fact]
    public async Task ShowAsync_MultipleEntries_DisplaysAll()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/project/alpha", ["clone", "repo-a"]),
            MakeEntry("/project/beta", ["clone", "repo-b"]),
            MakeEntry("/project/gamma", ["clone", "repo-c"])
        };

        var (output, exitCode) = await RunShowAsync(
            entries, "");

        Assert.Equal(0, exitCode);
        Assert.Contains("/project/alpha", output);
        Assert.Contains("/project/beta", output);
        Assert.Contains("/project/gamma", output);
    }

    [Fact]
    public async Task ShowAsync_PreselectedIndexTooLow_ReturnsOne()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/a", ["run"]) };

        var (output, exitCode) = await RunShowAsync(entries, preselectedIndex: 0);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid history index", output);
    }

    [Fact]
    public async Task ShowAsync_PreselectedIndexTooHigh_ReturnsOne()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/a", ["run"]) };

        var (output, exitCode) = await RunShowAsync(entries, preselectedIndex: 5);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid history index", output);
    }

    [Fact]
    public async Task ShowAsync_PreselectedIndex_Negative_ReturnsOne()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/a", ["run"]) };

        var (output, exitCode) = await RunShowAsync(entries, preselectedIndex: -1);

        Assert.Equal(1, exitCode);
        Assert.Contains("Invalid history index", output);
    }

    [Fact]
    public async Task ShowAsync_PreselectedIndexOne_Accepted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gc-test-hm-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var entries = new List<HistoryEntry> { MakeEntry(tempDir, ["--verbose"]) };

            var (output, exitCode) = await RunShowAsync(entries, preselectedIndex: 1);

            Assert.DoesNotContain("Invalid history index", output);
            Assert.Contains("Re-running", output);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ShowAsync_InteractiveEmptyInput_ReturnsZero()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/a", ["run"]) };

        var (output, exitCode) = await RunShowAsync(entries, "");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ShowAsync_InteractiveInvalidNumber_ReturnsZero()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/a", ["run"]) };

        var (output, exitCode) = await RunShowAsync(entries, "abc");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ShowAsync_InteractiveOutOfRangeNumber_ReturnsZero()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/a", ["run"]) };

        var (output, exitCode) = await RunShowAsync(entries, "99");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ShowAsync_EntryWithNoArgs_DisplaysNoArgs()
    {
        var entries = new List<HistoryEntry> { MakeEntry("/project/empty", Array.Empty<string>()) };

        var (output, exitCode) = await RunShowAsync(entries, "");

        Assert.Equal(0, exitCode);
        Assert.Contains("(no args)", output);
    }

    [Fact]
    public async Task ShowAsync_SpecialCharactersInPath_DisplaysCorrectly()
    {
        var specialPath = "/path/with spaces/and-unicode-\u00e9\u00e8\u00ea\u00eb";
        var entries = new List<HistoryEntry>
        {
            MakeEntry(specialPath, ["--flag=\u4e16\u754c", "emoji-\ud83d\ude00"])
        };

        var (output, exitCode) = await RunShowAsync(entries, "");

        Assert.Equal(0, exitCode);
        Assert.Contains(specialPath, output);
        Assert.Contains("\u4e16\u754c", output);
        Assert.Contains("\ud83d\ude00", output);
    }

    [Fact]
    public async Task ShowAsync_LongHistory_DisplaysAllEntries()
    {
        const int count = 50;
        var entries = new List<HistoryEntry>();
        for (var i = 0; i < count; i++) entries.Add(MakeEntry($"/long/project-{i}", [$"arg-{i}"]));

        var (output, exitCode) = await RunShowAsync(entries, "");

        Assert.Equal(0, exitCode);
        Assert.Contains("[1]", output);
        Assert.Contains($"[{count}]", output);
        Assert.Contains("/long/project-0", output);
        Assert.Contains($"/long/project-{count - 1}", output);
    }

    [Fact]
    public async Task ShowAsync_PrintsCorrectIndexFormat()
    {
        var entries = new List<HistoryEntry>
        {
            MakeEntry("/first", ["a1"]),
            MakeEntry("/second", ["a2"])
        };

        var (output, _) = await RunShowAsync(entries, "");

        Assert.Contains("[1] /first", output);
        Assert.Contains("[2] /second", output);
    }

    private class MockConsole : IConsole
    {
        private readonly StringReader? _reader;
        private readonly StringBuilder _sb = new();

        public MockConsole(string? input = null)
        {
            _reader = input != null ? new StringReader(input + Environment.NewLine) : null;
        }

        public string Output => _sb.ToString();

        public void WriteLine(string? value = null)
        {
            _sb.AppendLine(value);
        }

        public void Write(string? value)
        {
            _sb.Append(value);
        }

        public void WriteErrorLine(string? value)
        {
            _sb.AppendLine(value);
        }

        public string? ReadLine()
        {
            return _reader?.ReadLine();
        }
    }

    private class StubHistoryService : IHistoryService
    {
        private readonly Result _addResult;
        private readonly Result<IReadOnlyList<HistoryEntry>> _getResult;

        public StubHistoryService(Result<IReadOnlyList<HistoryEntry>> getResult, Result? addResult = null)
        {
            _getResult = getResult;
            _addResult = addResult ?? Result.Success();
        }

        public Task<Result> AddEntryAsync(string directory, string[] arguments, CancellationToken ct = default)
        {
            return Task.FromResult(_addResult);
        }

        public Task<Result<IReadOnlyList<HistoryEntry>>> GetHistoryAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_getResult);
        }

        public Task<Result> ClearHistoryAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Result.Success());
        }
    }
}