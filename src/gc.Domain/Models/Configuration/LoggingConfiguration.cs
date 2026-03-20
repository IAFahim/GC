namespace gc.Domain.Models.Configuration;

public sealed record LoggingConfiguration
{
    public string Level { get; init; } = "normal";
    public bool IncludeTimestamps { get; init; } = false;
}
