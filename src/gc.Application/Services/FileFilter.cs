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
        var activeExtensions = ResolveActiveExtensions(extensionFilters, config);
        
        // Pre-normalize patterns ONCE to avoid O(N²) performance
        var normalizedIgnorePatterns = config.Filters?.SystemIgnoredPatterns?
            .Select(p => p.Replace('\\', '/'))
            .ToList() ?? new List<string>();
        var normalizedSearchPaths = searchPaths.Select(p => p.Replace('\\', '/').TrimEnd('/')).ToList();
        
        var filtered = rawFiles
            .Where(path => IsValidPath(path, config, normalizedSearchPaths, excludePatterns, activeExtensions, normalizedIgnorePatterns))
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

    private FileEntry? CreateFileEntry(string path, GcConfiguration config)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists) return null;

            var extension = GetFullExtension(path).ToLowerInvariant();
            var fileName = Path.GetFileName(path).ToLowerInvariant();
            var languageKey = string.IsNullOrEmpty(extension) ? fileName : extension;
            var language = ResolveLanguage(languageKey, config);

            return new FileEntry(path, extension, language, fileInfo.Length);
        }
        catch (IOException ex)
        {
            _logger.Error($"Failed to access file info for {path} (file may be locked)", ex);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Error($"Access denied to {path}", ex);
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected error creating file entry for {path}", ex);
            return null;
        }
    }

    private string GetFullExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return string.IsNullOrEmpty(extension) ? string.Empty : extension.TrimStart('.');
    }

    private string ResolveLanguage(string key, GcConfiguration config)
    {
        if (config.LanguageMappings.TryGetValue(key, out var language)) return language;
        if (BuiltInPresets.LanguageMappings.TryGetValue(key, out var builtIn)) return builtIn;
        return key;
    }

    private HashSet<string> ResolveActiveExtensions(IEnumerable<string> extensions, GcConfiguration config)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in extensions) set.Add(ext);
        return set;
    }

    private bool IsValidPath(string path, GcConfiguration config, List<string> normalizedSearchPaths, IEnumerable<string> excludePatterns, HashSet<string> extensions, List<string> normalizedIgnorePatterns)
    {
        // Check extension filter
        if (extensions.Count > 0)
        {
            var fileName = Path.GetFileName(path);
            var matchesExtension = extensions.Any(ext => 
                fileName.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase) || 
                fileName.Equals(ext, StringComparison.OrdinalIgnoreCase));
            if (!matchesExtension) return false;
        }

        // Pre-normalize path once
        var pathNormalized = path.Replace('\\', '/');

        // Check system ignored patterns from config (patterns already normalized)
        foreach (var patternNormalized in normalizedIgnorePatterns)
        {
            // Check if path matches ignored pattern
            if (pathNormalized.Contains(patternNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Also check extension-specific patterns (e.g., ".bin")
            if (patternNormalized.StartsWith('.') && pathNormalized.EndsWith(patternNormalized, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Check search paths (paths already normalized)
        if (normalizedSearchPaths.Count > 0)
        {
            var matchesSearchPath = normalizedSearchPaths.Any(searchNormalized =>
            {
                // Exact match or directory prefix match with path separator boundary
                return pathNormalized.Equals(searchNormalized, StringComparison.OrdinalIgnoreCase) ||
                       pathNormalized.StartsWith(searchNormalized + "/", StringComparison.OrdinalIgnoreCase) ||
                       pathNormalized.Contains("/" + searchNormalized + "/", StringComparison.OrdinalIgnoreCase);
            });
            if (!matchesSearchPath) return false;
        }

        // Check exclude patterns
        foreach (var exclude in excludePatterns)
        {
            if (path.Contains(exclude, StringComparison.OrdinalIgnoreCase)) return false;
        }

        return true;
    }
}
