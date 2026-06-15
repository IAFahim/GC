namespace gc.Domain.Models.Configuration;

public sealed record LoggingConfiguration
{
    public string? Level { get; init; }
    public bool? IncludeTimestamps { get; init; }
}