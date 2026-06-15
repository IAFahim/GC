using System.Text.Json;
using System.Text.Json.Serialization;

namespace gc.Domain.Models.Configuration;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(GcConfiguration))]
[JsonSerializable(typeof(LimitsConfiguration))]
[JsonSerializable(typeof(DiscoveryConfiguration))]
[JsonSerializable(typeof(ClusterConfiguration))]
[JsonSerializable(typeof(FiltersConfiguration))]
[JsonSerializable(typeof(PresetConfiguration))]
[JsonSerializable(typeof(MarkdownConfiguration))]
[JsonSerializable(typeof(OutputConfiguration))]
[JsonSerializable(typeof(LoggingConfiguration))]
[JsonSerializable(typeof(Dictionary<string, PresetConfiguration>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(DefaultConfigOptions))]
[JsonSerializable(typeof(List<HistoryEntry>))]
public partial class GcJsonSerializerContext : JsonSerializerContext
{
}

// Indented variant for human-editable artifacts (profiles, directory defaults, `config dump`).
// A dedicated source-generated context keeps WriteIndented output fully NativeAOT-safe — the
// reflection-based JsonSerializer overloads throw NotSupportedException under PublishAot.
[JsonSourceGenerationOptions(WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(GcConfiguration))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
public partial class GcIndentedJsonContext : JsonSerializerContext
{
}