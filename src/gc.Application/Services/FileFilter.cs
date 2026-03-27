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

    public Result<IEnumerable<FileEntry>> FilterFiles(IEnumerable<string> rawFiles, GcConfiguration config, IEnumerable<string> searchPaths, IEnumerable<string> excludePatterns, IEnumerable<string> extensionFilters)
    {
        // Phase 1.1: FrozenSet for O(1) extension lookups with perfect hashing
        // Phase 4.2: Extensions lookup already uses FrozenSet
        var activeExtensions = ResolveActiveExtensions(extensionFilters);

        var systemIgnored = config.Filters?.SystemIgnoredPatterns ?? Array.Empty<string>();
        var allExcludes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in systemIgnored) allExcludes.Add(p.Replace('\\', '/'));
        foreach (var p in excludePatterns) allExcludes.Add(p.Replace('\\', '/'));

        // Phase 4.1: Aho-Corasick automaton for substring checks (O(L) instead of O(P*L))
        var excludeSearchValues = System.Buffers.SearchValues.Create(allExcludes.ToArray(), StringComparison.OrdinalIgnoreCase);

        var normalizedSearchPaths = searchPaths.Select(p => p.Replace('\\', '/').TrimEnd('/')).ToArray();

        var filtered = rawFiles
            .Where(path => IsValidPath(path, normalizedSearchPaths, excludeSearchValues, activeExtensions))
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

    // Phase 0.3: Defer FileInfo stat() — don't call new FileInfo(path) during filtering.
    // The generator already does its own FileInfo check when reading.
    // This eliminates N stat() syscalls during filtering.
    private FileEntry? CreateFileEntry(string path, GcConfiguration config)
    {
        try
        {
            // Phase 1.1: Avoid Path.GetExtension + ToLowerInvariant allocations
            // by extracting extension from the path span directly
            var extension = GetFullExtension(path);
            var fileName = GetFileNameSpan(path);
            var languageKey = string.IsNullOrEmpty(extension) ? fileName.ToString().ToLowerInvariant() : extension;
            var language = ResolveLanguage(languageKey, config);

            // Size = -1 signals "not yet resolved" — the generator resolves it when reading
            return new FileEntry(path, extension, language, -1);
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error creating file entry for {path}", ex);
            return null;
        }
    }

    /// <summary>
    /// Extracts extension without unnecessary allocation.
    /// Returns lowercased extension (without dot).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetFullExtension(string path)
    {
        var span = path.AsSpan();
        var dotIdx = span.LastIndexOf('.');
        if (dotIdx < 0) return string.Empty;

        // Don't return extension if the dot is at a path separator
        var afterDot = span[(dotIdx + 1)..];
        if (afterDot.IsEmpty) return string.Empty;
        if (afterDot.Contains('/') || afterDot.Contains('\\')) return string.Empty;

        // Single allocation: create lowered string directly
        return string.Create(afterDot.Length, (path, dotIdx + 1), static (dest, state) =>
        {
            var src = state.path.AsSpan(state.Item2);
            src.CopyTo(dest);
            // In-place lowercase (ASCII extensions are the common case)
            for (int i = 0; i < dest.Length; i++)
            {
                if (dest[i] >= 'A' && dest[i] <= 'Z')
                    dest[i] = (char)(dest[i] | 0x20);
            }
        });
    }

    /// <summary>
    /// Gets filename portion as a span — zero allocation.
    /// </summary>
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

    /// <summary>
    /// Phase 1.1: Use FrozenSet for O(1) extension matching with perfect hashing.
    /// </summary>
    private static FrozenSet<string> ResolveActiveExtensions(IEnumerable<string> extensions)
    {
        return extensions.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [System.Runtime.CompilerServices.SkipLocalsInit]
    private static bool IsValidPath(string path, string[] normalizedSearchPaths, System.Buffers.SearchValues<string> excludeSearchValues, FrozenSet<string> extensions)
    {
        var pathSpan = path.AsSpan();

        // Phase 1.1 / 4.2: Extension check using FrozenSet (O(1)) — zero allocation
        if (extensions.Count > 0)
        {
            var fileNameSpan = GetFileNameSpan(pathSpan);
            var dotIdx = fileNameSpan.LastIndexOf('.');
            bool matchesExtension = false;

            var lookup = extensions.GetAlternateLookup<ReadOnlySpan<char>>();

            // Check exact filename match first (e.g., "Dockerfile")
            if (lookup.Contains(fileNameSpan))
            {
                matchesExtension = true;
            }
            // Check extension match
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

        // Phase 1.1: Normalize path without allocation when no backslashes present
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

        // Phase 4.1: Single pass SIMD Aho-Corasick check for all ignores and excludes
        if (normalizedSpan.ContainsAny(excludeSearchValues))
        {
            return false;
        }

        // Check search paths
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
