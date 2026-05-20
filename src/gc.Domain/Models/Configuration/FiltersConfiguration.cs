namespace gc.Domain.Models.Configuration;

public sealed record FiltersConfiguration
{
    public string[] SystemIgnoredPatterns { get; init; } = Array.Empty<string>();
    public string[] AdditionalExtensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Glob patterns for paths to exclude (e.g., "*/test/*", "*.bench.*", "**/benchmark/**").
    /// Supports *, ?, ** wildcards.
    /// </summary>
    public string[] ExcludePathPatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Glob patterns for paths to include. When set, ONLY matching paths are included.
    /// Supports *, ?, ** wildcards.
    /// </summary>
    public string[] IncludePathPatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Keywords/patterns to search file content. Files whose content contains a match are EXCLUDED.
    /// Supports *, ? wildcards.
    /// </summary>
    public string[] ExcludeContentPatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Keywords/patterns to search file content. When set, ONLY files whose content contains a match are INCLUDED.
    /// Supports *, ? wildcards.
    /// </summary>
    public string[] IncludeContentPatterns { get; init; } = Array.Empty<string>();
}
