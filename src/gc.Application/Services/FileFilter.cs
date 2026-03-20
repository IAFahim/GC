using gc.Domain.Common;
using gc.Domain.Models;
using gc.Domain.Models.Configuration;
using gc.Domain.Constants;

namespace gc.Application.Services;

public sealed class FileFilter
{
    public Result<IEnumerable<FileEntry>> FilterFiles(IEnumerable<string> rawFiles, GcConfiguration config, IEnumerable<string> searchPaths, IEnumerable<string> excludePatterns, IEnumerable<string> extensionFilters)
    {
        var activeExtensions = ResolveActiveExtensions(extensionFilters, config);
        var filtered = rawFiles
            .Where(path => IsValidPath(path, config, searchPaths, excludePatterns, activeExtensions))
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
        catch
        {
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

    private bool IsValidPath(string path, GcConfiguration config, IEnumerable<string> searchPaths, IEnumerable<string> excludePatterns, HashSet<string> extensions)
    {
        // Check extension filter
        if (extensions.Count > 0 && !extensions.Contains(GetFullExtension(path))) return false;

        // Check search paths
        var searchPathsList = searchPaths.ToList();
        if (searchPathsList.Count > 0)
        {
            var pathNormalized = path.Replace('\\', '/');
            var matchesSearchPath = searchPathsList.Any(searchPath =>
            {
                var searchNormalized = searchPath.Replace('\\', '/');
                return pathNormalized.StartsWith(searchNormalized, StringComparison.OrdinalIgnoreCase) ||
                       pathNormalized.Contains("/" + searchNormalized, StringComparison.OrdinalIgnoreCase);
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
