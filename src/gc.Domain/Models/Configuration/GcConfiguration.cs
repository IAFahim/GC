namespace gc.Domain.Models.Configuration;

public sealed record GcConfiguration
{
    public string Version { get; init; } = "1.0.0";
    public LimitsConfiguration Limits { get; init; } = new();
    public DiscoveryConfiguration Discovery { get; init; } = new();
    public FiltersConfiguration Filters { get; init; } = new();
    public Dictionary<string, PresetConfiguration> Presets { get; init; } = new();
    public Dictionary<string, string> LanguageMappings { get; init; } = new();
    public MarkdownConfiguration Markdown { get; init; } = new();
    public OutputConfiguration Output { get; init; } = new();
    public LoggingConfiguration Logging { get; init; } = new();
    public CompactLevel Compact { get; init; } = CompactLevel.None;
}
