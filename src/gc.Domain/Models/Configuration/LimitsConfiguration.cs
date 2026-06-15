using gc.Domain.Common;

namespace gc.Domain.Models.Configuration;

public sealed record LimitsConfiguration
{
    // Nullable so JSON omission yields null and the merge `source.X ?? target.X` preserves the
    // lower layer. Concrete defaults live in BuiltInPresets.GetDefaultConfiguration() (base layer).
    public string? MaxFileSize { get; init; }
    public string? MaxClipboardSize { get; init; }
    public string? MaxMemoryBytes { get; init; }
    public int MaxFiles { get; init; } = 100000;

    public long GetMaxFileSizeBytes()
    {
        return MemorySizeParser.Parse(MaxFileSize ?? "1MB");
    }

    public long GetMaxClipboardSizeBytes()
    {
        return MemorySizeParser.Parse(MaxClipboardSize ?? "10MB");
    }

    public long GetMaxMemoryBytesValue()
    {
        return MemorySizeParser.Parse(MaxMemoryBytes ?? "100MB");
    }
}
