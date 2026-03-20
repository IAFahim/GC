namespace gc.Domain.Models.Configuration;

public sealed record MarkdownConfiguration
{
    public string Fence { get; init; } = "```";
    public string ProjectStructureHeader { get; init; } = "_Project Structure:_";
    public string FileHeaderTemplate { get; init; } = "## File: {path}";
    public string LanguageDetection { get; init; } = "extension";
}
