using System.Globalization;
using System.Text.Json.Serialization;

namespace gc.Data;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GcConfiguration))]
[JsonSerializable(typeof(LimitsConfiguration))]
[JsonSerializable(typeof(DiscoveryConfiguration))]
[JsonSerializable(typeof(FiltersConfiguration))]
[JsonSerializable(typeof(PresetConfiguration))]
[JsonSerializable(typeof(MarkdownConfiguration))]
[JsonSerializable(typeof(OutputConfiguration))]
[JsonSerializable(typeof(LoggingConfiguration))]
[JsonSerializable(typeof(Dictionary<string, PresetConfiguration>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class GcJsonSerializerContext : JsonSerializerContext
{
}

/// <summary>
/// Main configuration class for GC tool.
/// Supports cascading configuration from system, user, and project levels.
/// </summary>
public class GcConfiguration
{
    public string Version { get; set; } = "1.0.0";
    public LimitsConfiguration Limits { get; set; } = new();
    public DiscoveryConfiguration Discovery { get; set; } = new();
    public FiltersConfiguration Filters { get; set; } = new();
    public Dictionary<string, PresetConfiguration> Presets { get; set; } = new();
    public Dictionary<string, string> LanguageMappings { get; set; } = new();
    public MarkdownConfiguration Markdown { get; set; } = new();
    public OutputConfiguration Output { get; set; } = new();
    public LoggingConfiguration Logging { get; set; } = new();
}

/// <summary>
/// Memory and file size limits configuration.
/// </summary>
public class LimitsConfiguration
{
    public string MaxFileSize { get; set; } = "1MB";
    public string MaxClipboardSize { get; set; } = "10MB";
    public string MaxMemoryBytes { get; set; } = "100MB";
    public int MaxFiles { get; set; } = 100000;

    public long GetMaxFileSizeBytes() => ParseMemorySize(MaxFileSize);
    public long GetMaxClipboardSizeBytes() => ParseMemorySize(MaxClipboardSize);
    public long GetMaxMemoryBytesValue() => ParseMemorySize(MaxMemoryBytes);

    private static long ParseMemorySize(string size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return 104857600; // 100MB default

        size = size.Trim().ToUpperInvariant();
        long multiplier = 1;

        if (size.EndsWith("KB", StringComparison.Ordinal))
        {
            multiplier = 1024;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("MB", StringComparison.Ordinal))
        {
            multiplier = 1048576;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("GB", StringComparison.Ordinal))
        {
            multiplier = 1073741824;
            size = size.Substring(0, size.Length - 2);
        }
        else if (size.EndsWith("B", StringComparison.Ordinal))
        {
            size = size.Substring(0, size.Length - 1);
        }

        if (double.TryParse(size, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return (long)(value * multiplier);
        }

        return 104857600; // 100MB default if parsing fails
    }
}

/// <summary>
/// File discovery mode configuration.
/// </summary>
public class DiscoveryConfiguration
{
    public string Mode { get; set; } = "auto";
    public bool UseGit { get; set; } = true;
    public bool FollowSymlinks { get; set; } = false;
}

/// <summary>
/// File filtering configuration.
/// </summary>
public class FiltersConfiguration
{
    public string[] SystemIgnoredPatterns { get; set; } = Array.Empty<string>();
    public string[] AdditionalExtensions { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Preset configuration for file extensions.
/// </summary>
public class PresetConfiguration
{
    public string[] Extensions { get; set; } = Array.Empty<string>();
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Markdown formatting configuration.
/// </summary>
public class MarkdownConfiguration
{
    public string Fence { get; set; } = "```";
    public string ProjectStructureHeader { get; set; } = "_Project Structure:_";
    public string FileHeaderTemplate { get; set; } = "## File: {path}";
    public string LanguageDetection { get; set; } = "extension";
}

/// <summary>
/// Output configuration.
/// </summary>
public class OutputConfiguration
{
    public string DefaultFormat { get; set; } = "markdown";
    public bool IncludeStats { get; set; } = true;
    public bool SortByPath { get; set; } = true;
}

/// <summary>
/// Logging configuration.
/// </summary>
public class LoggingConfiguration
{
    public string Level { get; set; } = "normal";
    public bool IncludeTimestamps { get; set; } = false;
}
