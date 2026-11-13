using System.Globalization;

namespace gc.Domain.Common;

/// <summary>Parses final-output byte budgets using decimal KB and explicit binary KiB units.</summary>
public static class ChunkSize
{
    /// <summary>The default attachment budget, including all Markdown framing.</summary>
    public const int DefaultBytes = 50_000;

    /// <summary>Parses a positive budget. A bare number denotes kilobytes.</summary>
    public static bool TryParse(string? value, out int bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.AsSpan().Trim();
        var unitStart = 0;
        while (unitStart < text.Length && (char.IsAsciiDigit(text[unitStart]) || text[unitStart] == '.'))
            unitStart++;
        if (!decimal.TryParse(text[..unitStart], NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number)) return false;
        var unit = text[unitStart..].Trim();
        var multiplier = unit.ToString().ToUpperInvariant() switch
        {
            "" or "K" or "KB" => 1_000,
            "KIB" => 1_024,
            "M" or "MB" => 1_000_000,
            "MIB" => 1_048_576,
            "B" => 1,
            _ => 0
        };
        if (multiplier == 0 || number < 256m / multiplier || number > 1_000_000_000m / multiplier)
            return false;
        bytes = (int)(number * multiplier);
        return true;
    }
}
