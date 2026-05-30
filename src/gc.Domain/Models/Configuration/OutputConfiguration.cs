namespace gc.Domain.Models.Configuration;

public sealed record OutputConfiguration
{
    public string DefaultFormat { get; init; } = "markdown";
    public bool? IncludeStats { get; init; }
    public bool? SortByPath { get; init; }
    public bool? NoClipboard { get; init; }
}