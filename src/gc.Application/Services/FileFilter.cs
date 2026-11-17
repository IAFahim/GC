using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;

namespace gc.Application.Services;

public sealed class FileFilter
{
    private static readonly SearchValues<char> PathSeparators = SearchValues.Create("/\\");

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
        string[]? includePathPatterns = null,
        string? rootPath = null,
        string? absoluteRootPath = null)
    {
        var activeExtensions = ResolveActiveExtensions(extensionFilters, config.Filters?.AdditionalExtensions);
        var extensionLookup = activeExtensions.GetAlternateLookup<ReadOnlySpan<char>>();

        var systemIgnored = config.Filters?.SystemIgnoredPatterns ?? Array.Empty<string>();
        var systemIgnoredNormalized = systemIgnored.Select(p => p.Replace('\\', '/')).ToArray();
        var excludePatternsNormalized = excludePatterns.Select(p => p.Replace('\\', '/')).ToArray();

        var normalizedSearchPaths = searchPaths.Select(p => p.Replace('\\', '/').TrimEnd('/')).ToArray();
        var searchPathContainsForms = new string[normalizedSearchPaths.Length];
        var exactFileSearchPaths = new bool[normalizedSearchPaths.Length];
        var globSearchPaths = new bool[normalizedSearchPaths.Length];
        for (var i = 0; i < normalizedSearchPaths.Length; i++)
        {
            searchPathContainsForms[i] = "/" + normalizedSearchPaths[i] + "/";
            globSearchPaths[i] = IsGlobPattern(normalizedSearchPaths[i]);
            exactFileSearchPaths[i] = !globSearchPaths[i] && rootPath != null &&
                File.Exists(Path.Combine(absoluteRootPath ?? rootPath, normalizedSearchPaths[i]));
        }

        // Merge CLI and config path patterns
        var mergedExcludePath = MergePatterns(excludePathPatterns, config.Filters?.ExcludePathPatterns);
        var mergedIncludePath = MergePatterns(includePathPatterns, config.Filters?.IncludePathPatterns);

        var filtered = new List<FileEntry>();
        foreach (var path in rawFiles)
        {
            var matchPath = path;
            if (rootPath != null && Path.IsPathFullyQualified(path))
                matchPath = Path.GetRelativePath(Path.GetFullPath(absoluteRootPath ?? rootPath), path).Replace('\\', '/');
            if (!IsValidPath(matchPath, normalizedSearchPaths, searchPathContainsForms, exactFileSearchPaths, globSearchPaths, systemIgnoredNormalized,
                    excludePatternsNormalized, activeExtensions, extensionLookup, mergedExcludePath, mergedIncludePath))
                continue;
            if (TryCreateFileEntry(path, config, rootPath, absoluteRootPath, out var entry))
                filtered.Add(entry);
        }

        var maxFiles = config.Limits.MaxFiles;
        if (maxFiles > 0 && filtered.Count > maxFiles)
            return Result<IEnumerable<FileEntry>>.Success(filtered.Take(maxFiles));

        return Result<IEnumerable<FileEntry>>.Success(filtered);
    }

    public void ExplainFilter(
        string targetPath,
        GcConfiguration config,
        IEnumerable<string> searchPaths,
        IEnumerable<string> excludePatterns,
        IEnumerable<string> extensionFilters,
        string[]? excludePathPatterns = null,
        string[]? includePathPatterns = null)
    {
        _logger.Info($"\n--- Analyzing filter pipeline for: {targetPath} ---");
        var pathNormalized = targetPath.Replace('\\', '/');

        var activeExtensions = ResolveActiveExtensions(extensionFilters, config.Filters?.AdditionalExtensions);
        if (activeExtensions.Count > 0)
        {
            var fileNameSpan = GetFileNameSpan(pathNormalized.AsSpan());
            var dotIdx = fileNameSpan.LastIndexOf('.');
            var matchesExtension = false;

            var lookup = activeExtensions.GetAlternateLookup<ReadOnlySpan<char>>();

            if (lookup.Contains(fileNameSpan))
            {
                matchesExtension = true;
            }
            else if (dotIdx >= 0 && dotIdx < fileNameSpan.Length - 1)
            {
                var extSpan = fileNameSpan[(dotIdx + 1)..];
                if (lookup.Contains(extSpan)) matchesExtension = true;
            }

            if (!matchesExtension)
            {
                _logger.Warning(
                    $"[EXCLUDED] Failed extension filter. Target: '{GetFileNameSpan(pathNormalized.AsSpan()).ToString()}', Required: {string.Join(", ", activeExtensions)}");
                return;
            }

            _logger.Success("[PASSED] Extension filter.");
        }
        else
        {
            _logger.Success("[PASSED] Extension filter (none specified).");
        }

        var systemIgnored = config.Filters?.SystemIgnoredPatterns ?? Array.Empty<string>();
        foreach (var p in systemIgnored)
        {
            var pNormalized = p.Replace('\\', '/');
            if (GlobMatcher.IsMatch(pathNormalized.AsSpan(), pNormalized.AsSpan()))
            {
                _logger.Warning($"[EXCLUDED] Matched system ignored pattern: {p}");
                return;
            }
        }

        foreach (var p in excludePatterns)
        {
            var pNormalized = p.Replace('\\', '/');
            if (GlobMatcher.IsMatch(pathNormalized.AsSpan(), pNormalized.AsSpan()))
            {
                _logger.Warning($"[EXCLUDED] Matched glob exclude pattern: {p}");
                return;
            }
        }

        _logger.Success("[PASSED] Exact/glob exclude checks.");

        var mergedExcludePath = MergePatterns(excludePathPatterns, config.Filters?.ExcludePathPatterns);
        if (mergedExcludePath.Length > 0)
            foreach (var p in mergedExcludePath)
                if (GlobMatcher.IsMatch(pathNormalized.AsSpan(), p.AsSpan()))
                {
                    _logger.Warning($"[EXCLUDED] Matched glob exclude pattern: {p}");
                    return;
                }

        _logger.Success("[PASSED] Glob exclude checks.");

        var mergedIncludePath = MergePatterns(includePathPatterns, config.Filters?.IncludePathPatterns);
        if (mergedIncludePath.Length > 0)
        {
            var matched = false;
            foreach (var p in mergedIncludePath)
                if (GlobMatcher.IsMatch(pathNormalized.AsSpan(), p.AsSpan()))
                {
                    matched = true;
                    _logger.Success($"[PASSED] Matched glob include pattern: {p}");
                    break;
                }

            if (!matched)
            {
                _logger.Warning(
                    $"[EXCLUDED] Failed glob include patterns (did not match any): {string.Join(", ", mergedIncludePath)}");
                return;
            }
        }
        else
        {
            _logger.Success("[PASSED] Glob include checks (none specified).");
        }

        var normalizedSearchPaths = searchPaths.Select(p => p.Replace('\\', '/').TrimEnd('/')).ToArray();
        if (normalizedSearchPaths.Length > 0)
        {
            var matchesSearchPath = false;
            foreach (var searchPath in normalizedSearchPaths)
            {
                var searchSpan = searchPath.AsSpan();
                var normalizedSpan = pathNormalized.AsSpan();
                if ((IsGlobPattern(searchPath) && GlobMatcher.IsMatch(normalizedSpan, searchSpan)) ||
                    normalizedSpan.Equals(searchSpan, StringComparison.OrdinalIgnoreCase) ||
                    normalizedSpan.StartsWith(searchPath + "/", StringComparison.OrdinalIgnoreCase) ||
                    normalizedSpan.Contains(("/" + searchPath + "/").AsSpan(), StringComparison.OrdinalIgnoreCase))
                {
                    matchesSearchPath = true;
                    _logger.Success($"[PASSED] Matched search path: {searchPath}");
                    break;
                }
            }

            if (!matchesSearchPath)
            {
                _logger.Warning(
                    $"[EXCLUDED] Failed search path checks. Active paths: {string.Join(", ", normalizedSearchPaths)}");
                return;
            }
        }
        else
        {
            _logger.Success("[PASSED] Search path checks (none specified).");
        }

        _logger.Success($"[INCLUDED] File '{targetPath}' passes all path filters.");
    }

    /// <summary>Reads the first line of a file and returns the interpreter named by a <c>#!</c> line, or null.</summary>
    internal static string? SniffShebang(string absolutePath)
    {
        try
        {
            using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> buffer = stackalloc byte[128];
            var read = stream.Read(buffer);
            if (read < 3 || buffer[0] != (byte)'#' || buffer[1] != (byte)'!') return null;
            var line = System.Text.Encoding.UTF8.GetString(buffer[2..read]);
            var end = line.IndexOfAny(['\r', '\n']);
            if (end >= 0) line = line[..end];
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            var program = parts[0].EndsWith("/env", StringComparison.Ordinal) && parts.Length > 1 ? parts[1] : parts[0];
            var name = program[(program.LastIndexOf('/') + 1)..].TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.');
            return name switch
            {
                "node" or "nodejs" => "js",
                "python" => "py",
                "bash" or "sh" or "zsh" or "dash" => "sh",
                "ruby" => "rb",
                "perl" => "pl",
                "" => null,
                _ => name
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when a path entry should be matched as a glob rather than as a folder or file.</summary>
    internal static bool IsGlobPattern(string path)
    {
        return path.AsSpan().IndexOfAny('*', '?', '[') >= 0;
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

    private bool TryCreateFileEntry(string path, GcConfiguration config, string? rootPath,
        string? absoluteRootPath, out FileEntry entry)
    {
        try
        {
            var extension = GetFullExtension(path);
            var fileName = GetFileNameSpan(path);
            var languageKey = string.IsNullOrEmpty(extension) ? fileName.ToString().ToLowerInvariant() : extension;
            var language = ResolveLanguage(languageKey, config);
            var detection = config.Markdown?.LanguageDetection;
            if (detection == "filename")
            {
                // markdown.languageDetection = "filename": a known whole file name (Makefile, Dockerfile...) wins over the extension.
                var byName = fileName.ToString().ToLowerInvariant();
                if (config.LanguageMappings.ContainsKey(byName) || BuiltInPresets.LanguageMappings.ContainsKey(byName))
                    language = ResolveLanguage(byName, config);
            }

            // Compute root and relative path properly:
            // - rootPath is the repo root
            // - absoluteRootPath is the absolute version of rootPath
            // - path may be absolute or relative
            var root = string.Empty;
            var relative = path;

            if (!string.IsNullOrEmpty(rootPath))
            {
                var absRoot = !string.IsNullOrEmpty(absoluteRootPath)
                    ? absoluteRootPath
                    : Path.GetFullPath(rootPath);

                if (Path.IsPathFullyQualified(path))
                {
                    // path is absolute - compute relative from root
                    root = absRoot;
                    relative = Path.GetRelativePath(absRoot, path).Replace('\\', '/');
                }
                else
                {
                    // path is already relative to root
                    root = absRoot;
                    relative = path.Replace('\\', '/');
                }
            }

            // Populate file size during filtering to avoid redundant stat calls downstream.
            // Use the absolute path to stat correctly.
            long size = -1;
            try
            {
                var absPath = string.IsNullOrEmpty(root)
                    ? Path.GetFullPath(path)
                    : Path.Combine(root, relative);
                size = new FileInfo(absPath).Length;
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to get file size for {path}: {ex.Message}");
            }

            if (detection == "content" && string.IsNullOrEmpty(extension) &&
                !config.LanguageMappings.ContainsKey(languageKey) && !BuiltInPresets.LanguageMappings.ContainsKey(languageKey))
            {
                // markdown.languageDetection = "content": an extensionless, unknown file is typed by its shebang line.
                var sniffed = SniffShebang(string.IsNullOrEmpty(root) ? Path.GetFullPath(path) : Path.Combine(root, relative));
                if (sniffed != null) language = ResolveLanguage(sniffed, config);
            }

            entry = new FileEntry(
                root,
                relative,
                extension,
                language,
                size);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error creating file entry for {path}", ex);
            entry = default;
            return false;
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
        if (afterDot.ContainsAny(PathSeparators)) return string.Empty;

        return string.Create(afterDot.Length, (path, dotIdx + 1), static (dest, state) =>
        {
            var src = state.path.AsSpan(state.Item2);
            src.CopyTo(dest);
            for (var i = 0; i < dest.Length; i++)
                if (dest[i] >= 'A' && dest[i] <= 'Z')
                    dest[i] = (char)(dest[i] | 0x20);
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

    // filters.additionalExtensions are always allowed on top of whatever extension filter is active
    // (-e or a preset). With no extension filter everything is already allowed, so they add nothing.
    private static FrozenSet<string> ResolveActiveExtensions(IEnumerable<string> extensions, string[]? additional)
    {
        var all = extensions.ToList();
        if (all.Count > 0 && additional is { Length: > 0 })
            all.AddRange(additional.Select(e => e.Trim().TrimStart('.')).Where(e => e.Length > 0));
        return all.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsValidPath(string path, string[] normalizedSearchPaths, string[] searchPathContainsForms, bool[] exactFileSearchPaths,
        bool[] globSearchPaths, string[] systemIgnoredPatterns, string[] excludePatterns, FrozenSet<string> extensions,
        FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> lookup, string[] excludePathPatterns,
        string[] includePathPatterns)
    {
        var pathSpan = path.AsSpan();

        if (extensions.Count > 0)
        {
            var fileNameSpan = GetFileNameSpan(pathSpan);
            var dotIdx = fileNameSpan.LastIndexOf('.');
            var matchesExtension = false;

            if (lookup.Contains(fileNameSpan))
            {
                matchesExtension = true;
            }
            else if (dotIdx >= 0 && dotIdx < fileNameSpan.Length - 1)
            {
                var extSpan = fileNameSpan[(dotIdx + 1)..];
                if (lookup.Contains(extSpan)) matchesExtension = true;
            }

            if (!matchesExtension) return false;
        }

        string pathNormalized;
        if (path.Contains('\\'))
            pathNormalized = path.Replace('\\', '/');
        else
            pathNormalized = path;
        var normalizedSpan = pathNormalized.AsSpan();

        if (systemIgnoredPatterns.Length > 0 && GlobMatcher.MatchesAny(pathNormalized, systemIgnoredPatterns))
            return false;

        if (excludePatterns.Length > 0 && GlobMatcher.MatchesAny(pathNormalized, excludePatterns))
            return false;

        // Glob-based path exclude patterns (e.g., "*/test/*", "*.bench.*", "**/benchmark/**")
        if (excludePathPatterns.Length > 0 && GlobMatcher.MatchesAny(pathNormalized, excludePathPatterns)) return false;

        // Glob-based path include patterns — whitelist (e.g., "src/**", "lib/core/**")
        if (includePathPatterns.Length > 0 && !GlobMatcher.MatchesAny(pathNormalized, includePathPatterns))
            return false;

        if (normalizedSearchPaths.Length > 0)
        {
            var matchesSearchPath = false;
            for (var i = 0; i < normalizedSearchPaths.Length; i++)
            {
                var searchSpan = normalizedSearchPaths[i].AsSpan();
                if (globSearchPaths[i])
                {
                    // A plain path that contains * ? or [ selects by glob.
                    if (GlobMatcher.IsMatch(normalizedSpan, searchSpan))
                    {
                        matchesSearchPath = true;
                        break;
                    }
                    continue;
                }
                if (exactFileSearchPaths[i])
                {
                    if (normalizedSpan.Equals(searchSpan, StringComparison.OrdinalIgnoreCase))
                    {
                        matchesSearchPath = true;
                        break;
                    }
                    continue;
                }
                if (normalizedSpan.Equals(searchSpan, StringComparison.OrdinalIgnoreCase) ||
                    (normalizedSpan.Length > searchSpan.Length &&
                     normalizedSpan[searchSpan.Length] == '/' &&
                     normalizedSpan.StartsWith(searchSpan, StringComparison.OrdinalIgnoreCase)) ||
                    normalizedSpan.Contains(searchPathContainsForms[i].AsSpan(), StringComparison.OrdinalIgnoreCase))
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
