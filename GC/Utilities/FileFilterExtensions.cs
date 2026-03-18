using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GC.Data;

namespace GC.Utilities;

public static class FileFilterExtensions
{
    public static FileEntry[] FilterFiles(this string[] rawFiles, CliArguments args)
    {
        var activeExtensions = args.ResolveActiveExtensions();

        return rawFiles
            .AsParallel()
            .Where(path => path.IsValidPath(args, activeExtensions))
            .Select(path =>
            {
                var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                var fileName = Path.GetFileName(path).ToLowerInvariant();
                var languageKey = string.IsNullOrEmpty(extension) ? fileName : extension;
                var language = languageKey.ResolveLanguage();
                return new FileEntry(path, extension, language);
            })
            .ToArray();
    }

    private static HashSet<string> ResolveActiveExtensions(this CliArguments args)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var str in args.Extensions) set.Add(str);

        foreach (var preset in args.Presets)
        {
            var mapped = preset.ResolvePreset();
            foreach (var str in mapped)
            {
                set.Add(str);
            }
        }

        return set;
    }

    private static string[] ResolvePreset(this string preset)
    {
        return preset switch
        {
            "web" => Constants.PresetWeb,
            "backend" => Constants.PresetBackend,
            "dotnet" => Constants.PresetDotnet,
            "unity" => Constants.PresetUnity,
            "java" => Constants.PresetJava,
            "cpp" => Constants.PresetCpp,
            "script" => Constants.PresetScript,
            "data" => Constants.PresetData,
            "config" => Constants.PresetConfig,
            "build" => Constants.PresetBuild,
            "docs" => Constants.PresetDocs,
            _ =>[]
        };
    }

    private static string ResolveLanguage(this string key)
    {
        for (var i = 0; i < Constants.LangMapKeys.Length; i++)
        {
            if (string.Equals(Constants.LangMapKeys[i], key, StringComparison.OrdinalIgnoreCase))
            {
                return Constants.LangMapValues[i];
            }
        }
        return key;
    }

    private static bool IsValidPath(this string path, CliArguments args, HashSet<string> activeExtensions)
    {
        if (path.IsSystemIgnored())
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

    private static bool IsSystemIgnored(this string path)
    {
        foreach (var str in Constants.SystemIgnoredPatterns)
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
            if (path.StartsWith(pathStr, StringComparison.OrdinalIgnoreCase))
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