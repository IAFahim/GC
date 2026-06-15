namespace gc.Domain.Models.Configuration;

public sealed record OutputConfiguration
{
    public string? DefaultFormat { get; init; }
    public bool? IncludeStats { get; init; }
    public bool? SortByPath { get; init; }
    public bool? NoClipboard { get; init; }
}