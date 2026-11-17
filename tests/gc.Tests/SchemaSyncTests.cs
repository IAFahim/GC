using System.Reflection;
using System.Text.Json;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

/// <summary>
///     schema.json is embedded and exported by --export-schema; it must describe every setting the
///     configuration records expose, and the settings-file wrapper (settings, defaults, profiles).
/// </summary>
public sealed class SchemaSyncTests
{
    private static JsonElement LoadSchema()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "schema.json"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "schema.json"))).RootElement.Clone();
    }

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static void CheckRecord(Type type, JsonElement schemaObject, string path, List<string> missing)
    {
        if (!schemaObject.TryGetProperty("properties", out var properties))
        {
            missing.Add(path + " (no properties)");
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = CamelCase(property.Name);
            if (!properties.TryGetProperty(name, out var child))
            {
                missing.Add(path + "." + name);
                continue;
            }

            if (property.PropertyType.Namespace == typeof(GcConfiguration).Namespace && property.PropertyType.IsClass)
            {
                if (child.TryGetProperty("$ref", out _)) continue; // shared definition
                CheckRecord(property.PropertyType, child, path + "." + name, missing);
            }
        }
    }

    [Fact]
    public void Schema_DescribesEveryConfigurationProperty()
    {
        var schema = LoadSchema();
        var configuration = schema.GetProperty("definitions").GetProperty("configuration");
        var missing = new List<string>();

        CheckRecord(typeof(GcConfiguration), configuration, "settings", missing);

        Assert.True(missing.Count == 0, "schema.json is missing: " + string.Join(", ", missing));
    }

    [Fact]
    public void Schema_DescribesTheSettingsFileWrapper()
    {
        var schema = LoadSchema();
        var properties = schema.GetProperty("properties");

        foreach (var property in typeof(GcSettingsFile).GetProperties())
            Assert.True(properties.TryGetProperty(CamelCase(property.Name), out _), "missing " + property.Name);
    }
}
