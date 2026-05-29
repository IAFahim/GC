using System.Text.Json.Serialization;

namespace gc.Domain.Models.Configuration;

public sealed record DefaultConfigOptions
{
    [JsonPropertyName("version")] public string Version { get; init; } = "1.0.0";
    [JsonPropertyName("limits")] public LimitsOptions Limits { get; init; } = new();
    [JsonPropertyName("discovery")] public DiscoveryOptions Discovery { get; init; } = new();
    [JsonPropertyName("markdown")] public MarkdownOptions Markdown { get; init; } = new();
}

public sealed record LimitsOptions
{
    [JsonPropertyName("maxFileSize")] public string MaxFileSize { get; init; } = "1MB";
    [JsonPropertyName("maxClipboardSize")] public string MaxClipboardSize { get; init; } = "10MB";
    [JsonPropertyName("maxMemoryBytes")] public string MaxMemoryBytes { get; init; } = "100MB";
    [JsonPropertyName("maxFiles")] public int MaxFiles { get; init; } = 100000;
}

public sealed record DiscoveryOptions
{
    [JsonPropertyName("mode")] public string Mode { get; init; } = "auto";
    [JsonPropertyName("useGit")] public bool UseGit { get; init; } = true;
    [JsonPropertyName("followSymlinks")] public bool FollowSymlinks { get; init; } = false;
}

public sealed record MarkdownOptions
{
    [JsonPropertyName("fence")] public string Fence { get; init; } = "```";
    [JsonPropertyName("projectStructureHeader")] public string ProjectStructureHeader { get; init; } = "_Project Structure:_ ";
    [JsonPropertyName("fileHeaderTemplate")] public string FileHeaderTemplate { get; init; } = "## File: {path}";
}