using System.Text.Json.Serialization;

namespace gc.Domain.Models.Configuration;

/// <summary>
///     The one settings-file format. A user file (<c>gc.json</c> in the config folder) and a project
///     file (<c>.gc.json</c> in or above the project root) both hold this shape.
/// </summary>
public sealed record GcSettingsFile
{
    /// <summary>Configuration overrides, merged over the built-in defaults (project over user).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GcConfiguration? Settings { get; init; }

    /// <summary>Arguments applied on every run, before the ones typed on the command line.</summary>
    public string[] Defaults { get; init; } = [];

    /// <summary>Named argument lists, used as <c>gc @name</c>.</summary>
    public Dictionary<string, string[]> Profiles { get; init; } = new();
}
