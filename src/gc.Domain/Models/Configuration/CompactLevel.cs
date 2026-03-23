namespace gc.Domain.Models.Configuration;

/// <summary>
/// Compression levels for compact mode - controls how aggressively to reduce token count.
/// </summary>
public enum CompactLevel
{
    /// <summary>
    /// No compression - standard markdown output
    /// </summary>
    None,

    /// <summary>
    /// Mild compression - removes empty lines and collapses whitespace
    /// </summary>
    Mild,

    /// <summary>
    /// Aggressive compression - removes empty lines, collapses whitespace, truncates long comments, removes metadata
    /// </summary>
    Aggressive
}
