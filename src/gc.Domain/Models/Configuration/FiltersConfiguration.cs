namespace gc.Domain.Models.Configuration;

public sealed record FiltersConfiguration
{
    public string[] SystemIgnoredPatterns { get; init; } = Array.Empty<string>();
    public string[] AdditionalExtensions { get; init; } = Array.Empty<string>();
}
