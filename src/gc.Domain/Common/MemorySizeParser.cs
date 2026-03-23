using System.Globalization;

namespace gc.Domain.Common;

public static class MemorySizeParser
{
    private const long DefaultMemoryBytes = 104857600; // 100MB

    public static long Parse(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return DefaultMemoryBytes;

        size = size.Trim().ToUpperInvariant();
        long multiplier = 1;

        if (size.EndsWith("KB", StringComparison.Ordinal))
        {
            multiplier = 1024;
            size = size[..^2];
        }
        else if (size.EndsWith("MB", StringComparison.Ordinal))
        {
            multiplier = 1048576;
            size = size[..^2];
        }
        else if (size.EndsWith("GB", StringComparison.Ordinal))
        {
            multiplier = 1073741824;
            size = size[..^2];
        }
        else if (size.EndsWith("B", StringComparison.Ordinal))
        {
            size = size[..^1];
        }

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return (long)(value * multiplier);

        return DefaultMemoryBytes;
    }
}
