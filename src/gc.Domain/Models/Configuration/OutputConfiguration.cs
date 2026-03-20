namespace gc.Domain.Models.Configuration;

public sealed record OutputConfiguration
{
    public string DefaultFormat { get; init; } = "markdown";
    public bool IncludeStats { get; init; } = true;
    public bool SortByPath { get; init; } = true;
}
