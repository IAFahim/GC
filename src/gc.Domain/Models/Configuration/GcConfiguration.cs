namespace gc.Domain.Models.Configuration;

public sealed record GcConfiguration
{
    public string Version { get; init; } = "1.0.0";
    public LimitsConfiguration Limits { get; init; } = new();
    public DiscoveryConfiguration Discovery { get; init; } = new();
    public FiltersConfiguration Filters { get; init; } = new();
    public IReadOnlyDictionary<string, PresetConfiguration> Presets { get; init; } = new Dictionary<string, PresetConfiguration>();
    public IReadOnlyDictionary<string, string> LanguageMappings { get; init; } = new Dictionary<string, string>();
    public MarkdownConfiguration Markdown { get; init; } = new();
    public OutputConfiguration Output { get; init; } = new();
    public LoggingConfiguration Logging { get; init; } = new();
    public PerformanceConfiguration Performance { get; init; } = new();
}
