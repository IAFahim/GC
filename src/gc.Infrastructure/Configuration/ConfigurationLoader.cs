using System.Runtime.InteropServices;
using System.Text.Json;
using gc.Domain.Common;
using gc.Domain.Constants;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

namespace gc.Infrastructure.Configuration;

public sealed class ConfigurationLoader
{
    private readonly ILogger _logger;
    private GcConfiguration? _cachedConfiguration;
    private readonly object _cacheLock = new();

    public ConfigurationLoader(ILogger logger)
    {
        _logger = logger;
    }

    public string GetConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "gc");
        }

        var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(configDir)) return Path.Combine(configDir, "gc");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "gc");
    }

    public string GetSystemConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "gc");
        }

        return "/etc/gc";
    }

    public async Task<Result<GcConfiguration>> LoadConfigAsync(bool useCache = true)
    {
        if (useCache && _cachedConfiguration != null)
        {
            return Result<GcConfiguration>.Success(_cachedConfiguration);
        }

        _logger.Debug("Loading configuration...");

        var config = BuiltInPresets.GetDefaultConfiguration();
        _logger.Debug("Loaded built-in default configuration");

        // System Config
        var systemConfigPath = Path.Combine(GetSystemConfigDirectory(), "config.json");
        if (File.Exists(systemConfigPath))
        {
            var systemResult = await LoadConfigFromFileAsync(systemConfigPath);
            if (systemResult.IsSuccess && systemResult.Value != null)
            {
                config = MergeConfiguration(config, systemResult.Value, "System");
            }
        }

        // User Config
        var userConfigPath = Path.Combine(GetConfigDirectory(), "config.json");
        if (File.Exists(userConfigPath))
        {
            var userResult = await LoadConfigFromFileAsync(userConfigPath);
            if (userResult.IsSuccess && userResult.Value != null)
            {
                config = MergeConfiguration(config, userResult.Value, "User");
            }
        }

        // Project Config
        var projectConfigPath = FindProjectConfig();
        if (projectConfigPath != null)
        {
            var projectResult = await LoadConfigFromFileAsync(projectConfigPath);
            if (projectResult.IsSuccess && projectResult.Value != null)
            {
                config = MergeConfiguration(config, projectResult.Value, "Project");
            }
        }

        if (useCache)
        {
            lock (_cacheLock)
            {
                _cachedConfiguration = config;
            }
        }

        return Result<GcConfiguration>.Success(config);
    }

    public string? FindProjectConfig()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var gitRoot = FindGitRoot(currentDir);

        var searchDir = currentDir;
        while (searchDir != null && searchDir != Path.GetPathRoot(searchDir))
        {
            var configPath = Path.Combine(searchDir, ".gc", "config.json");
            if (File.Exists(configPath))
            {
                _logger.Debug($"Found project config at: {configPath}");
                return configPath;
            }

            if (gitRoot != null && string.Equals(searchDir, gitRoot, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug($"Stopped config search at git root: {gitRoot}");
                break;
            }

            var parentDir = Directory.GetParent(searchDir)?.FullName;
            if (parentDir == null) break;
            searchDir = parentDir;
        }

        return null;
    }

    private string? FindGitRoot(string startDir)
    {
        var currentDir = startDir;
        while (currentDir != null && currentDir != Path.GetPathRoot(currentDir))
        {
            var gitDir = Path.Combine(currentDir, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir)) return currentDir;
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return null;
    }

    public async Task<Result<GcConfiguration>> LoadConfigFromFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return Result<GcConfiguration>.Failure($"Configuration file not found: {filePath}");

        try
        {
            string json;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true))
            using (var reader = new StreamReader(fs))
            {
                json = await reader.ReadToEndAsync();
            }

            var typeInfo = GcJsonSerializerContext.Default.GcConfiguration;
            var config = JsonSerializer.Deserialize(json, typeInfo);
            return config != null 
                ? Result<GcConfiguration>.Success(config) 
                : Result<GcConfiguration>.Failure($"Failed to deserialize configuration from {filePath}");
        }
        catch (Exception ex)
        {
            return Result<GcConfiguration>.Failure($"Failed to load configuration file '{filePath}': {ex.Message}");
        }
    }

    private GcConfiguration MergeConfiguration(GcConfiguration target, GcConfiguration source, string sourceName)
    {
        _logger.Debug($"Merging {sourceName} configuration...");

        return target with
        {
            Version = source.Version ?? target.Version,
            Limits = MergeLimits(target.Limits, source.Limits),
            Discovery = MergeDiscovery(target.Discovery, source.Discovery),
            Filters = MergeFilters(target.Filters, source.Filters),
            Presets = MergePresets(target.Presets, source.Presets),
            LanguageMappings = MergeLanguageMappings(target.LanguageMappings, source.LanguageMappings),
            Markdown = MergeMarkdown(target.Markdown, source.Markdown),
            Output = MergeOutput(target.Output, source.Output),
            Logging = MergeLogging(target.Logging, source.Logging)
        };
    }

    private LimitsConfiguration MergeLimits(LimitsConfiguration target, LimitsConfiguration source)
    {
        return target with
        {
            MaxFileSize = source.MaxFileSize ?? target.MaxFileSize,
            MaxClipboardSize = source.MaxClipboardSize ?? target.MaxClipboardSize,
            MaxMemoryBytes = source.MaxMemoryBytes ?? target.MaxMemoryBytes,
            MaxFiles = source.MaxFiles > 0 ? source.MaxFiles : target.MaxFiles
        };
    }

    private DiscoveryConfiguration MergeDiscovery(DiscoveryConfiguration target, DiscoveryConfiguration source)
    {
        return target with
        {
            Mode = source.Mode ?? target.Mode,
            UseGit = source.UseGit,
            FollowSymlinks = source.FollowSymlinks,
            Cluster = MergeCluster(target.Cluster, source.Cluster)
        };
    }

    private ClusterConfiguration? MergeCluster(ClusterConfiguration? target, ClusterConfiguration? source)
    {
        if (source == null) return target;
        if (target == null) return source;

        return target with
        {
            Enabled = source.Enabled,
            MaxDepth = source.MaxDepth > 0 ? source.MaxDepth : target.MaxDepth,
            RepoSeparator = source.RepoSeparator ?? target.RepoSeparator,
            IncludeRepoHeader = source.IncludeRepoHeader,
            MaxParallelRepos = source.MaxParallelRepos > 0 ? source.MaxParallelRepos : target.MaxParallelRepos,
            SkipDirectories = source.SkipDirectories?.Length > 0 ? source.SkipDirectories : target.SkipDirectories,
            IncludeRootFiles = source.IncludeRootFiles,
            FailFast = source.FailFast
        };
    }

    private FiltersConfiguration MergeFilters(FiltersConfiguration target, FiltersConfiguration source)
    {
        return target with
        {
            SystemIgnoredPatterns = source.SystemIgnoredPatterns ?? target.SystemIgnoredPatterns,
            AdditionalExtensions = source.AdditionalExtensions ?? target.AdditionalExtensions
        };
    }

    private Dictionary<string, PresetConfiguration> MergePresets(
        Dictionary<string, PresetConfiguration> target,
        Dictionary<string, PresetConfiguration> source)
    {
        if (source == null || source.Count == 0) return target;

        var result = new Dictionary<string, PresetConfiguration>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source)
        {
            if (result.TryGetValue(kvp.Key, out var existing))
            {
                var mergedExtensions = new HashSet<string>(existing.Extensions, StringComparer.OrdinalIgnoreCase);
                if (kvp.Value.Extensions != null)
                {
                    foreach (var ext in kvp.Value.Extensions) mergedExtensions.Add(ext);
                }
                
                result[kvp.Key] = existing with
                {
                    Extensions = mergedExtensions.ToArray(),
                    Description = string.IsNullOrWhiteSpace(kvp.Value.Description) 
                        ? existing.Description 
                        : kvp.Value.Description
                };
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }

    private Dictionary<string, string> MergeLanguageMappings(
        Dictionary<string, string> target,
        Dictionary<string, string> source)
    {
        if (source == null || source.Count == 0) return target;
        var result = new Dictionary<string, string>(target, StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in source) result[kvp.Key] = kvp.Value;
        return result;
    }

    private MarkdownConfiguration MergeMarkdown(MarkdownConfiguration target, MarkdownConfiguration source)
    {
        return target with
        {
            Fence = source.Fence ?? target.Fence,
            ProjectStructureHeader = source.ProjectStructureHeader ?? target.ProjectStructureHeader,
            FileHeaderTemplate = source.FileHeaderTemplate ?? target.FileHeaderTemplate,
            LanguageDetection = source.LanguageDetection ?? target.LanguageDetection
        };
    }

    private OutputConfiguration MergeOutput(OutputConfiguration target, OutputConfiguration source)
    {
        return target with
        {
            DefaultFormat = source.DefaultFormat ?? target.DefaultFormat,
            IncludeStats = source.IncludeStats,
            SortByPath = source.SortByPath
        };
    }

    private LoggingConfiguration MergeLogging(LoggingConfiguration target, LoggingConfiguration source)
    {
        return target with
        {
            Level = source.Level ?? target.Level,
            IncludeTimestamps = source.IncludeTimestamps
        };
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedConfiguration = null;
        }
        _logger.Debug("Configuration cache cleared");
    }
}
