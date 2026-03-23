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
        bool hasValidUnit = false;

        if (size.EndsWith("KB", StringComparison.Ordinal))
        {
            multiplier = 1024;
            size = size[..^2];
            hasValidUnit = true;
        }
        else if (size.EndsWith("MB", StringComparison.Ordinal))
        {
            multiplier = 1048576;
            size = size[..^2];
            hasValidUnit = true;
        }
        else if (size.EndsWith("GB", StringComparison.Ordinal))
        {
            multiplier = 1073741824;
            size = size[..^2];
            hasValidUnit = true;
        }
        else if (size.EndsWith("B", StringComparison.Ordinal) && !size.EndsWith("KB", StringComparison.Ordinal) && !size.EndsWith("MB", StringComparison.Ordinal) && !size.EndsWith("GB", StringComparison.Ordinal))
        {
            size = size[..^1];
            hasValidUnit = true;
        }

        // If no valid unit suffix was found, return default
        if (!hasValidUnit)
            return DefaultMemoryBytes;

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            // Check for overflow before multiplication to prevent crashes on massive inputs
            double result = value * multiplier;
            if (result > long.MaxValue || double.IsInfinity(result))
                return DefaultMemoryBytes;

            return (long)result;
        }

        return DefaultMemoryBytes;
    }
}
