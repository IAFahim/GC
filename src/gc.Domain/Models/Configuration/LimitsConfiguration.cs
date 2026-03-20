using System.Globalization;

namespace gc.Domain.Models.Configuration;

public sealed record LimitsConfiguration
{
    public string MaxFileSize { get; init; } = "1MB";
    public string MaxClipboardSize { get; init; } = "10MB";
    public string MaxMemoryBytes { get; init; } = "100MB";
    public int MaxFiles { get; init; } = 100000;

    public long GetMaxFileSizeBytes()
    {
        return ParseMemorySize(MaxFileSize);
    }

    public long GetMaxClipboardSizeBytes()
    {
        return ParseMemorySize(MaxClipboardSize);
    }

    public long GetMaxMemoryBytesValue()
    {
        return ParseMemorySize(MaxMemoryBytes);
    }

    private static long ParseMemorySize(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 104857600;

        size = size.Trim().ToUpperInvariant();
        long multiplier = 1;

        if (size.EndsWith("KB", StringComparison.Ordinal))
        {
            multiplier = 1024;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("MB", StringComparison.Ordinal))
        {
            multiplier = 1048576;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("GB", StringComparison.Ordinal))
        {
            multiplier = 1073741824;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("B", StringComparison.Ordinal))
        {
            size = size.Substring(0, size.Length - 1);
        }

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return (long)(value * multiplier);

        return 104857600;
    }
}
