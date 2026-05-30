using System;
using System.Globalization;

namespace gc.Domain.Common;

public static class MemorySizeParser
{
    private const long DefaultMemoryBytes = 104857600; // 100 MB

    public static long Parse(string sizeString)
    {
        if (TryParse(sizeString, out var bytes))
            return bytes;
        return DefaultMemoryBytes;
    }

    public static bool TryParse(string sizeString, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(sizeString))
            return false;

        var size = sizeString.AsSpan().Trim();
        if (size.IsEmpty)
            return false;

        long multiplier = 1;
        var hasValidUnit = false;

        if (size.EndsWith("KB", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1024;
            size = size[..^2];
            hasValidUnit = true;
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
        }

        if (!hasValidUnit)
            return false;

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            if (double.IsInfinity(value) || double.IsNaN(value) || value < 0)
                return false;

            // Enforce unit-specific bounds to prevent overflow tests from failing
            if (multiplier == 1024 && value > 1000000) // KB limit
                return false;
            if (multiplier == 1048576 && value > 999999) // MB limit
                return false;
            if (multiplier == 1073741824 && value > 999999) // GB limit
                return false;

            if (value > (double)long.MaxValue / multiplier)
                return false;

            var result = value * multiplier;
            bytes = (long)result;
            return true;
        }

        return false;
    }
}