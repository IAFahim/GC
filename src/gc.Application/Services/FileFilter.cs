using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Domain.Constants;
using gc.Domain.Interfaces;

namespace gc.Application.Services;

public sealed class FileFilter
{
    private readonly ILogger _logger;

    public FileFilter(ILogger logger)
    {
        _logger = logger;
    }

    public Result<IEnumerable<FileEntry>> FilterFiles(
        IEnumerable<string> rawFiles,
        GcConfiguration config,
        IEnumerable<string> searchPaths,
        IEnumerable<string> excludePatterns,
        IEnumerable<string> extensionFilters,
        string[]? excludePathPatterns = null,
        string[]? includePathPatterns = null)
    {
        var activeExtensions = ResolveActiveExtensions(extensionFilters);

        var systemIgnored = config.Filters?.SystemIgnoredPatterns ?? Array.Empty<string>();
        var allExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in systemIgnored) allExcludes.Add(p.Replace('\\', '/'));
        foreach (var p in excludePatterns) allExcludes.Add(p.Replace('\\', '/'));

        var excludeSearchValues = System.Buffers.SearchValues.Create(allExcludes.ToArray(), StringComparison.OrdinalIgnoreCase);

        var normalizedSearchPaths = searchPaths.Select(p => p.Replace('\\', '/').TrimEnd('/')).ToArray();

        // Merge CLI and config path patterns
        var mergedExcludePath = MergePatterns(excludePathPatterns, config.Filters?.ExcludePathPatterns);
        var mergedIncludePath = MergePatterns(includePathPatterns, config.Filters?.IncludePathPatterns);

        var filtered = rawFiles
            .Where(path => IsValidPath(path, normalizedSearchPaths, excludeSearchValues, activeExtensions, mergedExcludePath, mergedIncludePath))
            .Select(path => CreateFileEntry(path, config))
            .Where(entry => entry != null)
            .Cast<FileEntry>()
            .ToList();

        var maxFiles = config.Limits.MaxFiles;
        if (maxFiles > 0 && filtered.Count > maxFiles)
        {
            return Result<IEnumerable<FileEntry>>.Success(filtered.Take(maxFiles));
        }

        return Result<IEnumerable<FileEntry>>.Success(filtered);
    }

    private static string[] MergePatterns(string[]? cliPatterns, string[]? configPatterns)
    {
        if ((cliPatterns == null || cliPatterns.Length == 0) &&
            (configPatterns == null || configPatterns.Length == 0))
            return Array.Empty<string>();

        var list = new List<string>();
        if (configPatterns != null) list.AddRange(configPatterns);
        if (cliPatterns != null) list.AddRange(cliPatterns);
        return list.ToArray();
    }

    private FileEntry? CreateFileEntry(string path, GcConfiguration config)
    {
        try
        {
            var extension = GetFullExtension(path);
            var fileName = GetFileNameSpan(path);
            var languageKey = string.IsNullOrEmpty(extension) ? fileName.ToString().ToLowerInvariant() : extension;
            var language = ResolveLanguage(languageKey, config);

            return new FileEntry(path, extension, language, -1);
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error creating file entry for {path}", ex);
            return null;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFullExtension(string path)
    {
        var span = path.AsSpan();
        var dotIdx = span.LastIndexOf('.');
        if (dotIdx < 0) return string.Empty;

        var afterDot = span[(dotIdx + 1)..];
        if (afterDot.IsEmpty) return string.Empty;
        if (afterDot.Contains('/') || afterDot.Contains('\\')) return string.Empty;

        return string.Create(afterDot.Length, (path, dotIdx + 1), static (dest, state) =>
        {
            var src = state.path.AsSpan(state.Item2);
            src.CopyTo(dest);
            for (int i = 0; i < dest.Length; i++)
            {
                if (dest[i] >= 'A' && dest[i] <= 'Z')
                    dest[i] = (char)(dest[i] | 0x20);
            }
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<char> GetFileNameSpan(ReadOnlySpan<char> path)
    {
        var lastSep = path.LastIndexOfAny('/', '\\');
        return lastSep >= 0 ? path[(lastSep + 1)..] : path;
    }

    private static string ResolveLanguage(string key, GcConfiguration config)
    {
        if (config.LanguageMappings.TryGetValue(key, out var language)) return language;
        if (BuiltInPresets.LanguageMappings.TryGetValue(key, out var builtIn)) return builtIn;
        return key;
    }

    private static FrozenSet<string> ResolveActiveExtensions(IEnumerable<string> extensions)
    {
        return extensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidPath(string path, string[] normalizedSearchPaths, System.Buffers.SearchValues<string> excludeSearchValues, FrozenSet<string> extensions, string[] excludePathPatterns, string[] includePathPatterns)
    {
        var pathSpan = path.AsSpan();

        if (extensions.Count > 0)
        {
            var fileNameSpan = GetFileNameSpan(pathSpan);
            var dotIdx = fileNameSpan.LastIndexOf('.');
            bool matchesExtension = false;

            var lookup = extensions.GetAlternateLookup<ReadOnlySpan<char>>();

            if (lookup.Contains(fileNameSpan))
            {
                matchesExtension = true;
            }
            else if (dotIdx >= 0 && dotIdx < fileNameSpan.Length - 1)
            {
                var extSpan = fileNameSpan[(dotIdx + 1)..];
                if (lookup.Contains(extSpan))
                {
                    matchesExtension = true;
                }
            }

            if (!matchesExtension) return false;
        }

        string pathNormalized;
        if (path.Contains('\\'))
        {
            pathNormalized = path.Replace('\\', '/');
        }
        else
        {
            pathNormalized = path;
        }
        var normalizedSpan = pathNormalized.AsSpan();

        if (normalizedSpan.ContainsAny(excludeSearchValues))
        {
            return false;
        }

        // Glob-based path exclude patterns (e.g., "*/test/*", "*.bench.*", "**/benchmark/**")
        if (excludePathPatterns.Length > 0 && GlobMatcher.MatchesAny(pathNormalized, excludePathPatterns))
        {
            return false;
        }

        // Glob-based path include patterns — whitelist (e.g., "src/**", "lib/core/**")
        if (includePathPatterns.Length > 0 && !GlobMatcher.MatchesAny(pathNormalized, includePathPatterns))
        {
            return false;
        }

        if (normalizedSearchPaths.Length > 0)
        {
            bool matchesSearchPath = false;
            foreach (var searchPath in normalizedSearchPaths)
            {
                var searchSpan = searchPath.AsSpan();
                if (normalizedSpan.Equals(searchSpan, StringComparison.OrdinalIgnoreCase) ||
                    normalizedSpan.StartsWith(searchPath + "/", StringComparison.OrdinalIgnoreCase) ||
                    normalizedSpan.Contains(("/" + searchPath + "/").AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    matchesSearchPath = true;
                    break;
                }
            }
            if (!matchesSearchPath) return false;
        }

        return true;
    }
}
