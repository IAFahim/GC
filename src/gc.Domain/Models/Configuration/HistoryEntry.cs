using System.Text.Json.Serialization;

namespace gc.Domain.Models.Configuration;

public sealed record HistoryEntry
{
    public string Directory { get; set; } = string.Empty;
    public string[] Arguments { get; set; } = [];
    public DateTime LastRun { get; set; }

    [JsonConstructor]
    public HistoryEntry() { }

    public HistoryEntry(string directory, string[] arguments, DateTime lastRun)
    {
        Directory = directory;
        Arguments = arguments;
        LastRun = lastRun;
    }
}
