using System.Text.Json;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Logging;

namespace gc.CLI.Services;

/// <summary>
///     <c>gc --migrate-settings</c>: copies the settings sources from before the two-file format
///     (user <c>config.json</c>, <c>profiles.json</c>, <c>directory_defaults.json</c>, a project
///     <c>.gc</c> arguments file and <c>.gc/config.json</c>) into <c>gc.json</c> and <c>.gc.json</c>.
///     Each migrated file is renamed to <c>*.migrated</c> so it is neither read again nor lost.
/// </summary>
public static class SettingsMigrator
{
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static async Task<int> RunAsync(string configDir, string currentDirectory)
    {
        var loader = new ConfigurationLoader(new ConsoleLogger());
        var moved = 0;

        // ── user level ──
        var userPath = SettingsStore.UserPath(configDir);
        var user = await SettingsStore.LoadAsync(userPath);
        var userChanged = false;

        var legacyConfig = Path.Combine(configDir, "config.json");
        if (File.Exists(legacyConfig))
        {
            var loaded = await loader.LoadConfigFromFileAsync(legacyConfig);
            if (!loaded.IsSuccess)
            {
                Console.Error.WriteLine($"Skipped {legacyConfig}: {loaded.Error}");
            }
            else if (user.Settings != null)
            {
                Console.Error.WriteLine($"Skipped {legacyConfig}: {SettingsStore.UserFileName} already has settings. Merge by hand.");
            }
            else
            {
                user = user with { Settings = loaded.Value };
                userChanged = true;
                moved += Retire(legacyConfig);
            }
        }

        var legacyProfiles = Path.Combine(configDir, "profiles.json");
        if (File.Exists(legacyProfiles))
        {
            var profiles = await ReadMapAsync(legacyProfiles);
            if (profiles != null)
            {
                var merged = new Dictionary<string, string[]>(user.Profiles, StringComparer.OrdinalIgnoreCase);
                foreach (var (name, args) in profiles) merged.TryAdd(name, args);
                user = user with { Profiles = new Dictionary<string, string[]>(merged) };
                userChanged = true;
                moved += Retire(legacyProfiles);
            }
        }

        // ── project level: the folder's saved defaults, then .gc / .gc/config.json up to the Git root ──
        var projectTargets = new Dictionary<string, GcSettingsFile>(PathComparer);

        async Task<GcSettingsFile> ProjectFile(string directory)
        {
            var path = Path.Combine(directory, SettingsStore.ProjectFileName);
            if (!projectTargets.TryGetValue(path, out var file))
                projectTargets[path] = file = await SettingsStore.LoadAsync(File.Exists(path) ? path : null);
            return file;
        }

        var legacyDefaults = Path.Combine(configDir, "directory_defaults.json");
        if (File.Exists(legacyDefaults))
        {
            var map = await ReadMapAsync(legacyDefaults);
            if (map != null)
            {
                var defaults = new Dictionary<string, string[]>(map, PathComparer);
                var key = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory));
                if (defaults.TryGetValue(key, out var args))
                {
                    var file = await ProjectFile(currentDirectory);
                    if (file.Defaults.Length == 0)
                        projectTargets[Path.Combine(currentDirectory, SettingsStore.ProjectFileName)] = file with { Defaults = args };
                    defaults.Remove(key);
                }

                // Entries for other folders stay in the file until those folders are migrated.
                if (defaults.Count == 0) moved += Retire(legacyDefaults);
                else Console.WriteLine($"  {legacyDefaults}: {defaults.Count} entr{(defaults.Count == 1 ? "y" : "ies")} for other folders left in place; run --migrate-settings there.");
            }
        }

        for (var dir = new DirectoryInfo(currentDirectory); dir != null; dir = dir.Parent)
        {
            var argsFile = Path.Combine(dir.FullName, ".gc");
            if (File.Exists(argsFile))
            {
                var file = await ProjectFile(dir.FullName);
                var target = Path.Combine(dir.FullName, SettingsStore.ProjectFileName);
                if (file.Defaults.Length == 0)
                    projectTargets[target] = file with { Defaults = CliArgumentResolver.SplitArguments(await File.ReadAllTextAsync(argsFile)) };
                moved += Retire(argsFile);
            }

            var folderConfig = Path.Combine(argsFile, "config.json");
            if (File.Exists(folderConfig))
            {
                var file = await ProjectFile(dir.FullName);
                var loaded = await loader.LoadConfigFromFileAsync(folderConfig);
                if (loaded.IsSuccess && file.Settings == null)
                {
                    projectTargets[Path.Combine(dir.FullName, SettingsStore.ProjectFileName)] = file with { Settings = loaded.Value };
                    moved += Retire(folderConfig);
                }
                else
                {
                    Console.Error.WriteLine($"Skipped {folderConfig}: {(loaded.IsSuccess ? ".gc.json already has settings" : loaded.Error)}");
                }
            }

            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) break;
        }

        // ── write ──
        if (userChanged && !await SettingsStore.SaveAsync(userPath, user)) return 1;
        foreach (var (path, file) in projectTargets)
            if (!await SettingsStore.SaveAsync(path, file))
                return 1;

        Console.WriteLine(moved == 0
            ? "Nothing to migrate: no old settings files found."
            : $"✓ Migrated {moved} old settings file{(moved == 1 ? "" : "s")} into {SettingsStore.UserFileName} / {SettingsStore.ProjectFileName} (originals renamed to *.migrated).");
        return 0;
    }

    private static int Retire(string path)
    {
        try
        {
            File.Move(path, path + ".migrated", true);
            return 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not rename {path}: {ex.Message}");
            return 0;
        }
    }

    private static async Task<Dictionary<string, string[]>?> ReadMapAsync(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(await File.ReadAllTextAsync(path),
                GcJsonSerializerContext.Default.DictionaryStringStringArray);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Skipped {path}: {ex.Message}");
            return null;
        }
    }
}
