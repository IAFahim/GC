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
[JsonSerializable(typeof(List<HistoryEntry>))]
public partial class GcJsonSerializerContext : JsonSerializerContext
{
}
