using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;

namespace gc.CLI.Services;

/// <summary>
///     Turns the typed command line into the final argument list.
///     Settings live in two files of one format (<see cref="SettingsStore" />): the user file and the
///     project file. Each can hold <c>defaults</c> (arguments always applied) and <c>profiles</c>
///     (name to arguments, used as <c>gc @name</c>). Built-in presets are the lowest-priority profiles.
///     Older sources are not read; <c>gc --migrate-settings</c> moves them into the two files.
/// </summary>
public static class CliArgumentResolver
{
    public static string[] SplitArguments(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];

            if (c == '\\' && i + 1 < commandLine.Length)
            {
                current.Append(commandLine[++i]);
            }
            else if ((c == '"' || c == '\'') && !inQuotes)
            {
                inQuotes = true;
                quoteChar = c;
            }
            else if (c == quoteChar && inQuotes)
            {
                inQuotes = false;
                quoteChar = '\0';
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args.ToArray();
    }

    public static async Task<(string[] ResolvedArgs, bool ShouldExit, int ExitCode)> ResolveAsync(
        string[] originalArgs,
        string configDir,
        string currentDirectory)
    {
        var userPath = SettingsStore.UserPath(configDir);
        var projectPath = SettingsStore.FindProjectPath(currentDirectory);

        // 1. `gc init [args]` writes the project file in the current folder.
        if (originalArgs.Length > 0 && originalArgs[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            var target = Path.Combine(currentDirectory, SettingsStore.ProjectFileName);
            var existing = await SettingsStore.LoadAsync(File.Exists(target) ? target : null);
            var saved = await SettingsStore.SaveAsync(target, existing with { Defaults = originalArgs.Skip(1).ToArray() });
            if (!saved) return (Array.Empty<string>(), true, 1);
            Console.WriteLine($"✓ Initialized project settings in {target}");
            return (Array.Empty<string>(), true, 0);
        }

        // 2. Migration and profile saving/listing.
        if (originalArgs.Length > 0)
        {
            if (originalArgs[0].Equals("--migrate-settings", StringComparison.OrdinalIgnoreCase))
                return (Array.Empty<string>(), true, await SettingsMigrator.RunAsync(configDir, currentDirectory));

            if (originalArgs[0].Equals("--save-profile", StringComparison.OrdinalIgnoreCase))
            {
                if (originalArgs.Length < 3)
                {
                    Console.Error.WriteLine("Error: --save-profile requires a profile name and at least one argument.");
                    return (Array.Empty<string>(), true, 1);
                }

                var profileName = originalArgs[1].TrimStart('@');
                var user = await SettingsStore.LoadAsync(userPath);
                var profiles = new Dictionary<string, string[]>(user.Profiles, StringComparer.OrdinalIgnoreCase)
                {
                    [profileName] = originalArgs.Skip(2).ToArray()
                };
                var ok = await SettingsStore.SaveAsync(userPath, user with { Profiles = new Dictionary<string, string[]>(profiles) });
                if (ok) Console.WriteLine($"✓ Profile '{profileName}' saved. Use it as: gc @{profileName}");
                return (Array.Empty<string>(), true, ok ? 0 : 1);
            }

            if (originalArgs[0].Equals("--list-profiles", StringComparison.OrdinalIgnoreCase))
            {
                await ListProfilesAsync(userPath, projectPath);
                return (Array.Empty<string>(), true, 0);
            }
        }

        // 3. Expand @name (and the older --profile name) wherever they appear.
        var userFile = await SettingsStore.LoadAsync(userPath);
        var projectFile = await SettingsStore.LoadAsync(projectPath);
        var finalArgs = new List<string>();
        for (var i = 0; i < originalArgs.Length; i++)
        {
            var arg = originalArgs[i];
            string? profileName = null;
            if (arg.Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= originalArgs.Length)
                {
                    Console.Error.WriteLine("Error: --profile requires a name.");
                    return (Array.Empty<string>(), true, 1);
                }

                profileName = originalArgs[++i].TrimStart('@');
            }
            else if (arg.Length > 1 && arg[0] == '@')
            {
                profileName = arg[1..];
            }

            if (profileName == null)
            {
                finalArgs.Add(arg);
                continue;
            }

            var profileArgs = FindProfile(profileName, projectFile, userFile);
            if (profileArgs == null)
            {
                Console.Error.WriteLine($"Error: Profile '{profileName}' not found. Run gc --list-profiles to see the choices.");
                return (Array.Empty<string>(), true, 1);
            }

            finalArgs.AddRange(profileArgs);
        }

        // --save writes the merged arguments as this folder's defaults.
        // Note: "-s" is an old spelling of -o/--output and must NOT be treated as save.
        var saveDirDefault = false;
        var cleanedArgs = new List<string>();
        foreach (var arg in finalArgs)
        {
            if (arg.Equals("--save", StringComparison.OrdinalIgnoreCase)) saveDirDefault = true;
            else cleanedArgs.Add(arg);
        }

        // 4. Defaults: user file, then project file.
        var baseArgs = new List<string>(userFile.Defaults);
        var projectDefaults = projectFile.Defaults;
        baseArgs.AddRange(projectDefaults);

        var mergedArgs = new List<string>(baseArgs);
        mergedArgs.AddRange(cleanedArgs);

        if (saveDirDefault)
        {
            var target = projectPath ?? Path.Combine(currentDirectory, SettingsStore.ProjectFileName);
            var existing = projectFile;
            // Save only what was typed on top of the user defaults, so the user file is not copied into the project.
            var saved = await SettingsStore.SaveAsync(target,
                existing with { Defaults = projectDefaults.Concat(cleanedArgs).ToArray() });
            if (!saved) return (Array.Empty<string>(), true, 1);
            Console.WriteLine($"✓ Default arguments saved in {target}");
        }

        return (mergedArgs.ToArray(), false, 0);
    }

    /// <summary>Profile lookup order: project file, user file, then built-in presets.</summary>
    private static string[]? FindProfile(string name, GcSettingsFile project, GcSettingsFile user)
    {
        if (SettingsStore.TryGetProfile(project, name, out var args)) return args;
        if (SettingsStore.TryGetProfile(user, name, out args)) return args;
        return BuiltInProfile(name);
    }

    /// <summary>Built-in presets are the lowest-priority profiles: <c>@web</c> is <c>--preset web</c>.</summary>
    internal static string[]? BuiltInProfile(string name)
    {
        if (name.Equals("unity-source", StringComparison.OrdinalIgnoreCase)) return ["-u"];
        return BuiltInPresets.GetAllPresets().ContainsKey(name) ? ["--preset", name.ToLowerInvariant()] : null;
    }

    private static async Task ListProfilesAsync(string userPath, string? projectPath)
    {
        var project = await SettingsStore.LoadAsync(projectPath);
        var user = await SettingsStore.LoadAsync(userPath);
        Console.WriteLine("Profiles (use as gc @name):");
        foreach (var (key, value) in project.Profiles) Console.WriteLine($"  @{key} (project): {string.Join(" ", value)}");
        foreach (var (key, value) in user.Profiles) Console.WriteLine($"  @{key} (user): {string.Join(" ", value)}");
        foreach (var (key, _) in BuiltInPresets.GetAllPresets())
            Console.WriteLine($"  @{key} (built-in): {string.Join(" ", BuiltInProfile(key)!)}");
    }
}
