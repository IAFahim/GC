using System.Globalization;
using gc.Domain.Common;

namespace gc.Domain.Models.Configuration;

public sealed record LimitsConfiguration
{
    public string MaxFileSize { get; init; } = "1MB";
    public string MaxClipboardSize { get; init; } = "10MB";
    public string MaxMemoryBytes { get; init; } = "100MB";
    public int MaxFiles { get; init; } = 100000;

    public long GetMaxFileSizeBytes()
    {
        return MemorySizeParser.Parse(MaxFileSize);
    }

    public long GetMaxClipboardSizeBytes()
    {
        return MemorySizeParser.Parse(MaxClipboardSize);
    }

    public long GetMaxMemoryBytesValue()
    {
        return MemorySizeParser.Parse(MaxMemoryBytes);
    }
}
