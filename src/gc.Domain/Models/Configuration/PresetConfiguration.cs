namespace gc.Domain.Models.Configuration;

public sealed record PresetConfiguration
{
    public string[] Extensions { get; init; } = Array.Empty<string>();
    public string Description { get; init; } = string.Empty;
}
