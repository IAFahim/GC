using System.Text.Json;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.Configuration;

/// <summary>
///     Reads and writes the two settings files: the user file (<c>gc.json</c> in the config folder)
///     and the project file (<c>.gc.json</c> in or above the project root). Both use
///     <see cref="GcSettingsFile" />. Older sources (config.json layers, the <c>.gc</c> arguments file,
///     directory_defaults.json, profiles.json) are still read for compatibility and reported by
///     <see cref="FindLegacyFiles" />.
/// </summary>
public static class SettingsStore
{
    public const string UserFileName = "gc.json";
    public const string ProjectFileName = ".gc.json";

    private const long MaxBytes = 4L * 1024 * 1024;

    public static string UserPath(string configDir)
    {
        return Path.Combine(configDir, UserFileName);
    }

    /// <summary>Walks up from <paramref name="startDir" /> to the Git root (or the drive root) looking for the project file.</summary>
    public static string? FindProjectPath(string? startDir)
    {
        var current = string.IsNullOrEmpty(startDir) ? null : new DirectoryInfo(startDir);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, ProjectFileName);
            if (File.Exists(candidate)) return candidate;
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
                break;
            current = current.Parent;
        }

        return null;
    }

    /// <summary>Loads a settings file. A missing file is an empty file; an unreadable one is reported and treated as empty.</summary>
    public static async Task<GcSettingsFile> LoadAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new GcSettingsFile();
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            if (stream.Length > MaxBytes)
            {
                Console.Error.WriteLine($"Warning: settings file '{path}' is too large; ignoring it.");
                return new GcSettingsFile();
            }

            var file = await JsonSerializer.DeserializeAsync(stream, GcJsonSerializerContext.Default.GcSettingsFile);
            return Normalize(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Warning: could not read settings file '{path}': {ex.Message}");
            return new GcSettingsFile();
        }
    }

    public static async Task<bool> SaveAsync(string path, GcSettingsFile file)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(file, GcIndentedJsonContext.Default.GcSettingsFile);
            await File.WriteAllTextAsync(path, json + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error writing settings file '{path}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Case-insensitive profile lookup in one file.</summary>
    public static bool TryGetProfile(GcSettingsFile file, string name, out string[] args)
    {
        foreach (var (key, value) in file.Profiles)
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                args = value;
                return true;
            }

        args = [];
        return false;
    }

    /// <summary>Lists settings sources from before the two-file format that still exist.</summary>
    public static IReadOnlyList<string> FindLegacyFiles(string configDir, string startDir)
    {
        var found = new List<string>();
        foreach (var name in new[] { "config.json", "profiles.json", "directory_defaults.json" })
        {
            var path = Path.Combine(configDir, name);
            if (File.Exists(path)) found.Add(path);
        }

        for (var dir = new DirectoryInfo(startDir); dir != null; dir = dir.Parent)
        {
            var args = Path.Combine(dir.FullName, ".gc");
            if (File.Exists(args)) found.Add(args);
            else if (File.Exists(Path.Combine(args, "config.json"))) found.Add(Path.Combine(args, "config.json"));
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                break;
        }

        return found;
    }

    private static GcSettingsFile Normalize(GcSettingsFile? file)
    {
        if (file == null) return new GcSettingsFile();
        return file with
        {
            Defaults = file.Defaults ?? [],
            Profiles = file.Profiles ?? new Dictionary<string, string[]>()
        };
    }
}
