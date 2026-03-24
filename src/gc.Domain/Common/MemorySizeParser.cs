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
        bool isKB = false;

        if (size.EndsWith("KB", StringComparison.Ordinal))
        {
            multiplier = 1024;
            size = size[..^2];
            hasValidUnit = true;
            isKB = true;
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
            // Check for unreasonably large numeric inputs before even multiplying
            // Different thresholds for different units: KB can go higher (1M KB = 1GB is ok)
            // but MB/GB with huge numbers suggest errors (1M MB = 976GB is too much)
            double threshold = isKB ? 100000000 : 100000; // 100M for KB, 100K for MB/GB/B
            if (value > threshold)
                return DefaultMemoryBytes;
            
            // Check for overflow before multiplication to prevent crashes on massive inputs
            if (double.IsInfinity(value) || double.IsNaN(value))
                return DefaultMemoryBytes;
            
            // Check if multiplication would overflow a long
            if (value > (double)long.MaxValue / multiplier)
                return DefaultMemoryBytes;
            
            double result = value * multiplier;
            if (result > long.MaxValue || double.IsInfinity(result) || result < 0)
                return DefaultMemoryBytes;

            return (long)result;
        }

        return DefaultMemoryBytes;
    }
}
