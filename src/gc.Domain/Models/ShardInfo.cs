namespace gc.Domain.Models;

/// <summary>
/// Represents a shard specification: process shard Slice/of Total parts.
/// All indices are 1-based (1st of 3, 2nd of 3, etc.)
/// </summary>
public sealed record ShardInfo(int Slice, int Of)
{
    public int Slice { get; init; } = Slice >= 1 ? Slice : 1;
    public int Of { get; init; } = Of >= 1 ? Of : 1;

    /// <summary>
    /// Parse "2.3" or "2/3" format.
    /// </summary>
    public static ShardInfo? TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        char sep = '/';
        if (!input.Contains(sep)) sep = '.';

        var parts = input.Split(sep);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var slice)
            && int.TryParse(parts[1], out var ofTotal)
            && slice >= 1
            && ofTotal >= 1
            && slice <= ofTotal)
        {
            return new ShardInfo(slice, ofTotal);
        }
        return null;
    }
}
