namespace gc.Domain.Models.Configuration;

public sealed record FiltersConfiguration
{
    // Nullable so JSON omission yields null (not []), letting the merge `source.X ?? target.X`
    // preserve the lower config layer instead of silently wiping it with an empty array.
    public string[]? SystemIgnoredPatterns { get; init; } = null;
    public string[]? AdditionalExtensions { get; init; } = null;

    /// <summary>
    ///     Glob patterns for paths to exclude (e.g., "*/test/*", "*.bench.*", "**/benchmark/**").
    ///     Supports *, ?, ** wildcards.
    /// </summary>
    public string[]? ExcludePathPatterns { get; init; } = null;

    /// <summary>
    ///     Glob patterns for paths to include. When set, ONLY matching paths are included.
    ///     Supports *, ?, ** wildcards.
    /// </summary>
    public string[]? IncludePathPatterns { get; init; } = null;

    /// <summary>
    ///     Keywords/patterns to search file content. Files whose content contains a match are EXCLUDED.
    ///     Supports *, ? wildcards.
    /// </summary>
    public string[]? ExcludeContentPatterns { get; init; } = null;

    /// <summary>
    ///     Keywords/patterns to search file content. When set, ONLY files whose content contains a match are INCLUDED.
    ///     Supports *, ? wildcards.
    /// </summary>
    public string[]? IncludeContentPatterns { get; init; } = null;
}
