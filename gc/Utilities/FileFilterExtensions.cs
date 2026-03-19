using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using gc.Data;

namespace gc.Utilities;

public static class FileFilterExtensions
{
    public static FileEntry[] FilterFiles(this string[] rawFiles, CliArguments args)
    {
        if (rawFiles == null) throw new ArgumentNullException(nameof(rawFiles));

        using var _ = Logger.TimeOperation("File filtering");

        Logger.LogVerbose($"Filtering {rawFiles.Length} raw files...");

        var activeExtensions = args.ResolveActiveExtensions();

        Logger.LogDebug($"Active extensions: [{string.Join(", ", activeExtensions)}]");
        Logger.LogDebug($"Path filters: [{string.Join(", ", args.Paths)}]");
        Logger.LogDebug($"Exclude patterns: [{string.Join(", ", args.Excludes)}]");

        var filtered = rawFiles
            .AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .Where(path => path.IsValidPath(args, activeExtensions))
            .Select(path =>
            {
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var fileName = Path.GetFileName(path).ToLowerInvariant();
                var languageKey = string.IsNullOrEmpty(extension) ? fileName : extension;
                var language = languageKey.ResolveLanguage(args);
                return new FileEntry(path, extension, language);
            })
            .ToArray();

        // Enforce MaxFiles limit
        var maxFiles = args.Configuration?.Limits?.MaxFiles ?? 100000;
        if (filtered.Length > maxFiles && maxFiles > 0)
        {
            Logger.LogVerbose($"Limiting files from {filtered.Length} to {maxFiles} (MaxFiles limit)");
            var limited = new FileEntry[maxFiles];
            Array.Copy(filtered, limited, maxFiles);
            filtered = limited;
        }

        Logger.LogVerbose($"Filtered to {filtered.Length} files");
        Logger.LogDebug($"Rejected {rawFiles.Length - filtered.Length} files");

        return filtered;
    }

    private static HashSet<string> ResolveActiveExtensions(this CliArguments args)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var str in args.Extensions) set.Add(str);

        foreach (var preset in args.Presets)
        {
            var mapped = preset.ResolvePreset(args);
            foreach (var str in mapped)
            {
                set.Add(str);
            }
        }

        return set;
    }

    private static string[] ResolvePreset(this string preset, CliArguments args)
    {
        // Try to get from configuration first
        if (args.Configuration?.Presets != null &&
            args.Configuration.Presets.TryGetValue(preset, out var presetConfig))
        {
            return presetConfig.Extensions;
        }

        // Fallback to built-in presets
        return preset switch
        {
            "web" => BuiltInPresets.PresetWeb,
            "backend" => BuiltInPresets.PresetBackend,
            "dotnet" => BuiltInPresets.PresetDotnet,
            "unity" => BuiltInPresets.PresetUnity,
            "java" => BuiltInPresets.PresetJava,
            "cpp" => BuiltInPresets.PresetCpp,
            "script" => BuiltInPresets.PresetScript,
            "data" => BuiltInPresets.PresetData,
            "config" => BuiltInPresets.PresetConfig,
            "build" => BuiltInPresets.PresetBuild,
            "docs" => BuiltInPresets.PresetDocs,
            _ => Array.Empty<string>()
        };
    }

    private static string ResolveLanguage(this string key, CliArguments args)
    {
        // Try to get from configuration first
        if (args.Configuration?.LanguageMappings != null &&
            args.Configuration.LanguageMappings.TryGetValue(key, out var language))
        {
            return language;
        }

        // Fallback to built-in mappings
        if (BuiltInPresets.LanguageMappings.TryGetValue(key, out var builtInLanguage))
        {
            return builtInLanguage;
        }

        return key;
    }

    private static bool IsValidPath(this string path, CliArguments args, HashSet<string> activeExtensions)
    {
        if (path.IsSystemIgnored(args))
        {
            return false;
        }

        if (!path.MatchesPaths(args.Paths))
        {
            return false;
        }

        if (path.IsExcluded(args.Excludes))
        {
            return false;
        }

        if (activeExtensions.Count > 0 && !path.MatchesExtensions(activeExtensions))
        {
            return false;
        }

        return true;
    }

    private static bool IsSystemIgnored(this string path, CliArguments args)
    {
        // Get system ignored patterns from configuration or built-in defaults
        var patterns = args.Configuration?.Filters?.SystemIgnoredPatterns ?? BuiltInPresets.SystemIgnoredPatterns;

        foreach (var str in patterns)
        {
            if (path.Contains(str, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (path.EndsWith(str, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesPaths(this string path, string[] paths)
    {
        if (paths.Length == 0)
        {
            return true;
        }

        foreach (var pathStr in paths)
        {
            // Normalize slashes for comparison
            var normalizedPathStr = pathStr.Replace('\\', '/').TrimEnd('/');
            
            if (path.Equals(normalizedPathStr, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.StartsWith(normalizedPathStr + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcluded(this string path, string[] excludes)
    {
        foreach (var exclude in excludes)
        {
            if (path.StartsWith(exclude, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.Contains($"/{exclude}", StringComparison.OrdinalIgnoreCase))
                return true;

            if (exclude.StartsWith("*.") && path.EndsWith(exclude.Substring(1), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesExtensions(this string path, HashSet<string> activeExtensions)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        var fileName = Path.GetFileName(path).ToLowerInvariant();

        return activeExtensions.Contains(ext) || activeExtensions.Contains(fileName);
    }
}