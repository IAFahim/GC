using System.Globalization;

namespace gc.Domain.Common;

public static class MemorySizeParser
{
    private const long DefaultMemoryBytes = 104857600;

    public static long Parse(string sizeString)
    {
        if (string.IsNullOrWhiteSpace(sizeString))
            return DefaultMemoryBytes;

        return Parse(sizeString.AsSpan());
    }

    public static long Parse(ReadOnlySpan<char> size)
    {
        size = size.Trim();
        if (size.IsEmpty)
            return DefaultMemoryBytes;

        long multiplier = 1;
        bool hasValidUnit = false;
        bool isKB = false;
        bool isB = false;

        if (size.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024;
            size = size[..^2];
            hasValidUnit = true;
            isKB = true;
        }
        else if (size.EndsWith("MB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1048576;
            size = size[..^2];
            hasValidUnit = true;
        }
        else if (size.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1073741824;
            size = size[..^2];
            hasValidUnit = true;
        }
        else if (size.EndsWith("B", StringComparison.OrdinalIgnoreCase))
        {
            size = size[..^1];
            hasValidUnit = true;
            isB = true;
        }

        if (!hasValidUnit)
            return DefaultMemoryBytes;

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            double threshold = isB ? 100000000000 : (isKB ? 100000000 : 100000);
            if (value > threshold)
                return DefaultMemoryBytes;

            if (double.IsInfinity(value) || double.IsNaN(value))
                return DefaultMemoryBytes;

            if (value > (double)long.MaxValue / multiplier)
                return DefaultMemoryBytes;

            double result = value * multiplier;
            if (result > long.MaxValue || double.IsInfinity(result) || result < 0)
                return DefaultMemoryBytes;

            long finalResult = (long)result;
            if (finalResult == 0 && value > 0)
                return DefaultMemoryBytes;

            return finalResult;
        }

        return DefaultMemoryBytes;
    }
}
