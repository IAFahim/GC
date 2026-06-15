using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using gc.Domain.Models.Configuration;

namespace gc.CLI.Services;

public static class CliArgumentResolver
{
    private static readonly string ProfileFile = "profiles.json";
    private static readonly string DirDefaultsFile = "directory_defaults.json";

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string NormalizeDirKey(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

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
        // 1. Handle "init" command
        if (originalArgs.Length > 0 && originalArgs[0].Equals("init", StringComparison.OrdinalIgnoreCase))
        {
            var localGcPath = Path.Combine(currentDirectory, ".gc");
            static string EscapeArg(string a)
            {
                var esc = a.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var needsQuote = a.Length == 0 || a.AsSpan().IndexOfAny(" \t\"'".AsSpan()) >= 0;
                return needsQuote ? $"\"{esc}\"" : esc;
            }
            var content = string.Join(" ", originalArgs.Skip(1).Select(EscapeArg));
            try
            {
                await File.WriteAllTextAsync(localGcPath, content);
                Console.WriteLine($"✓ Initialized local configuration in {localGcPath}");
                return (Array.Empty<string>(), true, 0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error initializing .gc file: {ex.Message}");
                return (Array.Empty<string>(), true, 1);
            }
        }

        // 2. Handle profile saving & listing before applying default directories / profiles
        if (originalArgs.Length > 0)
        {
            if (originalArgs[0].Equals("--save-profile", StringComparison.OrdinalIgnoreCase))
            {
                if (originalArgs.Length < 3)
                {
                    Console.Error.WriteLine("Error: --save-profile requires a profile name and at least one argument.");
                    return (Array.Empty<string>(), true, 1);
                }
                var profileName = originalArgs[1];
                var profileArgs = originalArgs.Skip(2).ToArray();
                var success = await SaveProfileAsync(configDir, profileName, profileArgs);
                return (Array.Empty<string>(), true, success ? 0 : 1);
            }

            if (originalArgs[0].Equals("--list-profiles", StringComparison.OrdinalIgnoreCase))
            {
                await ListProfilesAsync(configDir);
                return (Array.Empty<string>(), true, 0);
            }
        }

        // 3. Resolve profiles if --profile is present
        var finalArgs = new List<string>();
        for (var i = 0; i < originalArgs.Length; i++)
        {
            if (originalArgs[i].Equals("--profile", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= originalArgs.Length)
                {
                    Console.Error.WriteLine("Error: --profile requires a name.");
                    return (Array.Empty<string>(), true, 1);
                }
                var profileName = originalArgs[i + 1];
                var profileArgs = await LoadProfileAsync(configDir, profileName);
                if (profileArgs == null)
                {
                    Console.Error.WriteLine($"Error: Profile '{profileName}' not found.");
                    return (Array.Empty<string>(), true, 1);
                }
                finalArgs.AddRange(profileArgs);
                i++; // Skip name
            }
            else
            {
                finalArgs.Add(originalArgs[i]);
            }
        }

        // Check if saving directory default (flag "--save").
        // Note: "-s" is the short form of "spit"/--output and must NOT be treated as save.
        var saveDirDefault = false;
        var cleanedArgs = new List<string>();
        foreach (var arg in finalArgs)
        {
            if (arg.Equals("--save", StringComparison.OrdinalIgnoreCase))
            {
                saveDirDefault = true;
            }
            else
            {
                cleanedArgs.Add(arg);
            }
        }

        // If no arguments remain (or we have arguments but not overriding defaults),
        // we check local .gc file first, and directory default second.
        var baseArgs = new List<string>();

        // Find local .gc file in current directory or parents
        var localGcArgs = LoadLocalGcFile(currentDirectory);
        if (localGcArgs != null && localGcArgs.Length > 0)
        {
            baseArgs.AddRange(localGcArgs);
        }
        else
        {
            // If no local .gc file, fall back to directory-specific default from directory_defaults.json
            var dirDefaultArgs = await LoadDirectoryDefaultAsync(configDir, currentDirectory);
            if (dirDefaultArgs != null)
            {
                baseArgs.AddRange(dirDefaultArgs);
            }
        }

        // Merge: base args + explicitly passed command line arguments
        var mergedArgs = new List<string>(baseArgs);
        mergedArgs.AddRange(cleanedArgs);

        // If the save flag was specified, persist the merged args as directory default
        if (saveDirDefault)
        {
            // Do not save the save flag itself
            var success = await SaveDirectoryDefaultAsync(configDir, currentDirectory, mergedArgs.ToArray());
            if (!success)
            {
                return (Array.Empty<string>(), true, 1);
            }
            Console.WriteLine($"✓ Default arguments saved for directory: {currentDirectory}");
        }

        return (mergedArgs.ToArray(), false, 0);
    }

    private static string[]? LoadLocalGcFile(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            var localGcPath = Path.Combine(current.FullName, ".gc");
            if (File.Exists(localGcPath))
            {
                try
                {
                    var text = File.ReadAllText(localGcPath);
                    return SplitArguments(text);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to read .gc file at {localGcPath}: {ex.Message}");
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private static async Task<bool> SaveProfileAsync(string configDir, string name, string[] args)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            var path = Path.Combine(configDir, ProfileFile);
            var profiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                var loaded = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.DictionaryStringStringArray);
                if (loaded != null)
                {
                    profiles = new Dictionary<string, string[]>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
            profiles[name] = args;
            var newJson = JsonSerializer.Serialize(profiles, GcIndentedJsonContext.Default.DictionaryStringStringArray);
            await File.WriteAllTextAsync(path, newJson);
            Console.WriteLine($"✓ Profile '{name}' saved successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving profile: {ex.Message}");
            return false;
        }
    }

    private static async Task<string[]?> LoadProfileAsync(string configDir, string name)
    {
        var path = Path.Combine(configDir, ProfileFile);
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var profiles = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.DictionaryStringStringArray);
            if (profiles != null && profiles.TryGetValue(name, out var args))
            {
                return args;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading profile: {ex.Message}");
        }
        return null;
    }

    private static async Task ListProfilesAsync(string configDir)
    {
        var path = Path.Combine(configDir, ProfileFile);
        if (!File.Exists(path))
        {
            Console.WriteLine("No profiles found.");
            return;
        }
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var profiles = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.DictionaryStringStringArray);
            if (profiles == null || profiles.Count == 0)
            {
                Console.WriteLine("No profiles found.");
                return;
            }
            Console.WriteLine("Available Profiles:");
            foreach (var kvp in profiles)
            {
                Console.WriteLine($"  {kvp.Key}: {string.Join(" ", kvp.Value)}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing profiles: {ex.Message}");
        }
    }

    private static async Task<bool> SaveDirectoryDefaultAsync(string configDir, string directory, string[] args)
    {
        try
        {
            Directory.CreateDirectory(configDir);
            var path = Path.Combine(configDir, DirDefaultsFile);
            var defaults = new Dictionary<string, string[]>(PathComparer);
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                var loaded = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.DictionaryStringStringArray);
                if (loaded != null)
                {
                    defaults = new Dictionary<string, string[]>(loaded, PathComparer);
                }
            }
            defaults[NormalizeDirKey(directory)] = args;
            var newJson = JsonSerializer.Serialize(defaults, GcIndentedJsonContext.Default.DictionaryStringStringArray);
            await File.WriteAllTextAsync(path, newJson);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error saving directory default: {ex.Message}");
            return false;
        }
    }

    private static async Task<string[]?> LoadDirectoryDefaultAsync(string configDir, string directory)
    {
        var path = Path.Combine(configDir, DirDefaultsFile);
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var loaded = JsonSerializer.Deserialize(json, GcJsonSerializerContext.Default.DictionaryStringStringArray);
            if (loaded != null)
            {
                var defaults = new Dictionary<string, string[]>(loaded, PathComparer);
                if (defaults.TryGetValue(NormalizeDirKey(directory), out var args))
                {
                    return args;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error loading directory default: {ex.Message}");
        }
        return null;
    }
}
