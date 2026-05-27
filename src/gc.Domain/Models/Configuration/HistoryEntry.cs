using System.Text.Json.Serialization;

namespace gc.Domain.Models.Configuration;

public sealed class HistoryEntry
{
    public string Directory { get; init; } = string.Empty;
    public string[] Arguments { get; init; } = [];
    public DateTime LastRun { get; init; }

    [JsonConstructor]
    public HistoryEntry() { }

    public HistoryEntry(string directory, string[] arguments, DateTime lastRun)
    {
        Directory = directory;
        Arguments = arguments;
        LastRun = lastRun;
    }
}