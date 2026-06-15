namespace gc.Domain.Models.Configuration;

public sealed record MarkdownConfiguration
{
    // Nullable so JSON omission yields null and the merge `source.X ?? target.X` preserves the
    // lower layer. Concrete defaults live in BuiltInPresets.GetDefaultConfiguration() (base layer).
    public string? Fence { get; init; }
    public string? ProjectStructureHeader { get; init; }
    public string? FileHeaderTemplate { get; init; }
    public string? LanguageDetection { get; init; }
}
