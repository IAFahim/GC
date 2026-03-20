using System.Runtime.InteropServices;
using System.Text.Json;
using gc.Utilities;

namespace gc.Data;

/// <summary>
/// Loads and merges GC configuration from multiple sources.
/// Configuration cascade: System → User → Project → Built-in Defaults
/// CLI arguments have highest priority and override all config files.
/// </summary>
public static class ConfigurationLoader
{
    private static GcConfiguration? _cachedConfiguration;
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Get the configuration directory for the current platform.
    /// </summary>
    public static string GetConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "gc");
        }
        else
        {
            // Linux/macOS: use XDG config directory or ~/.config
            var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrEmpty(configDir))
            {
                return Path.Combine(configDir, "gc");
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config",
                "gc");
        }
    }

    /// <summary>
    /// Get the system configuration directory for the current platform.
    /// </summary>
    public static string GetSystemConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "gc");
        }
        else
        {
            return "/etc/gc";
        }
    }

    /// <summary>
    /// Load configuration from all sources and merge with proper priority.
    /// Cascade: System → User → Project → Built-in Defaults
    /// </summary>
    public static GcConfiguration LoadConfiguration(bool useCache = true)
    {
        // Use cached configuration if available
        if (useCache && _cachedConfiguration != null)
        {
            return _cachedConfiguration;
        }

        Logger.LogDebug("Loading configuration...");

        // Start with built-in defaults
        var config = BuiltInPresets.GetDefaultConfiguration();
        Logger.LogDebug("Loaded built-in default configuration");

        // Load system configuration
        var systemConfigPath = Path.Combine(GetSystemConfigDirectory(), "config.json");
        if (File.Exists(systemConfigPath))
        {
            try
            {
                var systemConfig = LoadConfigFromFile(systemConfigPath);
                if (systemConfig != null)
                {
                    MergeConfiguration(config, systemConfig, "System");
                    Logger.LogDebug($"Loaded system configuration from: {systemConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"Failed to load system configuration from {systemConfigPath}: {ex.Message}");
            }
        }
        else
        {
            Logger.LogDebug($"No system configuration found at: {systemConfigPath}");
        }

        // Load user configuration
        var userConfigPath = Path.Combine(GetConfigDirectory(), "config.json");
        if (File.Exists(userConfigPath))
        {
            try
            {
                var userConfig = LoadConfigFromFile(userConfigPath);
                if (userConfig != null)
                {
                    MergeConfiguration(config, userConfig, "User");
                    Logger.LogDebug($"Loaded user configuration from: {userConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"Failed to load user configuration from {userConfigPath}: {ex.Message}");
            }
        }
        else
        {
            Logger.LogDebug($"No user configuration found at: {userConfigPath}");
        }

        // Load project configuration
        var projectConfigPath = FindProjectConfig();
        if (projectConfigPath != null)
        {
            try
            {
                var projectConfig = LoadConfigFromFile(projectConfigPath);
                if (projectConfig != null)
                {
                    MergeConfiguration(config, projectConfig, "Project");
                    Logger.LogDebug($"Loaded project configuration from: {projectConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"Failed to load project configuration from {projectConfigPath}: {ex.Message}");
            }
        }
        else
        {
            Logger.LogDebug("No project configuration found (.gc/config.json)");
        }

        // Cache the merged configuration
        if (useCache)
        {
            lock (_cacheLock)
            {
                _cachedConfiguration = config;
            }
        }

        return config;
    }

    /// <summary>
    /// Search upward from current directory for .gc/config.json.
    /// Stops at .git root or filesystem root.
    /// </summary>
    public static string? FindProjectConfig()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var gitRoot = FindGitRoot(currentDir);

        // Search from current directory upward
        var searchDir = currentDir;
        while (searchDir != null && searchDir != Path.GetPathRoot(searchDir))
        {
            var configPath = Path.Combine(searchDir, ".gc", "config.json");
            if (File.Exists(configPath))
            {
                Logger.LogDebug($"Found project config at: {configPath}");
                return configPath;
            }

            // Stop at git root
            if (gitRoot != null && string.Equals(searchDir, gitRoot, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogDebug($"Stopped config search at git root: {gitRoot}");
                break;
            }

            // Move to parent directory
            var parentDir = Directory.GetParent(searchDir)?.FullName;
            if (parentDir == null)
                break;

            searchDir = parentDir;
        }

        return null;
    }

    /// <summary>
    /// Find the .git root directory.
    /// </summary>
    private static string? FindGitRoot(string startDir)
    {
        var currentDir = startDir;
        while (currentDir != null && currentDir != Path.GetPathRoot(currentDir))
        {
            var gitDir = Path.Combine(currentDir, ".git");
            if (Directory.Exists(gitDir) || File.Exists(gitDir)) // File check for git worktrees
            {
                return currentDir;
            }

            currentDir = Directory.GetParent(currentDir)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Load configuration from a specific file.
    /// </summary>
    public static GcConfiguration? LoadConfigFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = File.ReadAllText(filePath);

            // Get the source-generated JsonTypeInfo
            var typeInfo = GcJsonSerializerContext.Default.GcConfiguration;

            // Configure the options on the JsonTypeInfo for AOT compatibility
            // Note: ReadCommentHandling and AllowTrailingCommas are set via the source generator
            var options = typeInfo.Options;
            options.ReadCommentHandling = JsonCommentHandling.Skip;
            options.AllowTrailingCommas = true;

            var config = JsonSerializer.Deserialize(json, typeInfo);

            return config;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in configuration file '{filePath}': {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load configuration file '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Merge source configuration into target configuration.
    /// Source has higher priority and overrides target values.
    /// </summary>
    private static void MergeConfiguration(GcConfiguration target, GcConfiguration source, string sourceName)
    {
        Logger.LogDebug($"Merging {sourceName} configuration...");

        // Merge simple properties
        if (source.Version != null)
            target.Version = source.Version;

        // Merge limits
        if (source.Limits != null)
        {
            MergeLimits(target.Limits, source.Limits);
        }

        // Merge discovery
        if (source.Discovery != null)
        {
            MergeDiscovery(target.Discovery, source.Discovery);
        }

        // Merge filters
        if (source.Filters != null)
        {
            MergeFilters(target.Filters, source.Filters);
        }

        // Merge presets (deep merge with deduplication)
        if (source.Presets != null && source.Presets.Count > 0)
        {
            MergePresets(target.Presets, source.Presets);
        }

        // Merge language mappings (source overrides target)
        if (source.LanguageMappings != null && source.LanguageMappings.Count > 0)
        {
            foreach (var mapping in source.LanguageMappings)
            {
                target.LanguageMappings[mapping.Key] = mapping.Value;
            }
        }

        // Merge markdown configuration
        if (source.Markdown != null)
        {
            MergeMarkdown(target.Markdown, source.Markdown);
        }

        // Merge output configuration
        if (source.Output != null)
        {
            MergeOutput(target.Output, source.Output);
        }

        // Merge logging configuration
        if (source.Logging != null)
        {
            MergeLogging(target.Logging, source.Logging);
        }
    }

    private static void MergeLimits(LimitsConfiguration target, LimitsConfiguration source)
    {
        if (source.MaxFileSize != null)
            target.MaxFileSize = source.MaxFileSize;
        if (source.MaxClipboardSize != null)
            target.MaxClipboardSize = source.MaxClipboardSize;
        if (source.MaxMemoryBytes != null)
            target.MaxMemoryBytes = source.MaxMemoryBytes;
        if (source.MaxFiles > 0)
            target.MaxFiles = source.MaxFiles;
    }

    private static void MergeDiscovery(DiscoveryConfiguration target, DiscoveryConfiguration source)
    {
        if (source.Mode != null)
            target.Mode = source.Mode;
        target.UseGit = source.UseGit;
        target.FollowSymlinks = source.FollowSymlinks;
    }

    private static void MergeFilters(FiltersConfiguration target, FiltersConfiguration source)
    {
        // Source completely overrides target for arrays to allow removing default patterns
        if (source.SystemIgnoredPatterns != null)
        {
            target.SystemIgnoredPatterns = source.SystemIgnoredPatterns;
        }

        if (source.AdditionalExtensions != null)
        {
            target.AdditionalExtensions = source.AdditionalExtensions;
        }
    }

    private static void MergePresets(Dictionary<string, PresetConfiguration> target, Dictionary<string, PresetConfiguration> source)
    {
        foreach (var presetKvp in source)
        {
            var presetName = presetKvp.Key;
            var sourcePreset = presetKvp.Value;

            if (target.TryGetValue(presetName, out var targetPreset))
            {
                // Merge extensions with deduplication
                var mergedExtensions = new HashSet<string>(targetPreset.Extensions, StringComparer.OrdinalIgnoreCase);
                foreach (var ext in sourcePreset.Extensions)
                {
                    mergedExtensions.Add(ext);
                }
                targetPreset.Extensions = mergedExtensions.ToArray();

                // Override description if source has one
                if (!string.IsNullOrWhiteSpace(sourcePreset.Description))
                {
                    targetPreset.Description = sourcePreset.Description;
                }
            }
            else
            {
                // Add new preset
                target[presetName] = sourcePreset;
            }
        }
    }

    private static void MergeMarkdown(MarkdownConfiguration target, MarkdownConfiguration source)
    {
        if (source.Fence != null)
            target.Fence = source.Fence;
        if (source.ProjectStructureHeader != null)
            target.ProjectStructureHeader = source.ProjectStructureHeader;
        if (source.FileHeaderTemplate != null)
            target.FileHeaderTemplate = source.FileHeaderTemplate;
        if (source.LanguageDetection != null)
            target.LanguageDetection = source.LanguageDetection;
    }

    private static void MergeOutput(OutputConfiguration target, OutputConfiguration source)
    {
        if (source.DefaultFormat != null)
            target.DefaultFormat = source.DefaultFormat;
        target.IncludeStats = source.IncludeStats;
        target.SortByPath = source.SortByPath;
    }

    private static void MergeLogging(LoggingConfiguration target, LoggingConfiguration source)
    {
        if (source.Level != null)
            target.Level = source.Level;
        target.IncludeTimestamps = source.IncludeTimestamps;
    }

    /// <summary>
    /// Clear the configuration cache.
    /// </summary>
    public static void ClearCache()
    {
        lock (_cacheLock)
        {
            _cachedConfiguration = null;
        }
        Logger.LogDebug("Configuration cache cleared");
    }

    /// <summary>
    /// Get all configuration file paths for the current environment.
    /// </summary>
    public static ConfigPaths GetConfigPaths()
    {
        return new ConfigPaths
        {
            SystemConfig = Path.Combine(GetSystemConfigDirectory(), "config.json"),
            UserConfig = Path.Combine(GetConfigDirectory(), "config.json"),
            ProjectConfig = FindProjectConfig()
        };
    }

    public class ConfigPaths
    {
        public string SystemConfig { get; set; } = string.Empty;
        public string UserConfig { get; set; } = string.Empty;
        public string? ProjectConfig { get; set; }
    }
}
