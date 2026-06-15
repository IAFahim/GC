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

    public ConfigurationLoader(ILogger logger)
    {
        _logger = logger;
    }

    public string GetConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "gc");

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
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "gc");

        return "/etc/gc";
    }

    public async Task<Result<GcConfiguration>> LoadConfigAsync(bool useCache = true)
    {
        _logger.Debug("Loading configuration...");

        var config = BuiltInPresets.GetDefaultConfiguration();
        _logger.Debug("Loaded built-in default configuration");

        var systemConfigPath = Path.Combine(GetSystemConfigDirectory(), "config.json");
        if (File.Exists(systemConfigPath))
        {
            var systemResult = await LoadConfigFromFileAsync(systemConfigPath);
            if (systemResult.IsSuccess && systemResult.Value != null)
                config = MergeConfiguration(config, systemResult.Value, "System");
        }

        var userConfigPath = Path.Combine(GetConfigDirectory(), "config.json");
        if (File.Exists(userConfigPath))
        {
            var userResult = await LoadConfigFromFileAsync(userConfigPath);
            if (userResult.IsSuccess && userResult.Value != null)
                config = MergeConfiguration(config, userResult.Value, "User");
        }

        var projectConfigPath = FindProjectConfig();
        if (projectConfigPath != null)
        {
            var projectResult = await LoadConfigFromFileAsync(projectConfigPath);
            if (projectResult.IsSuccess && projectResult.Value != null)
                config = MergeConfiguration(config, projectResult.Value, "Project");
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
            // Hardcoded guard: the configurable MaxFileSize lives inside the very config being loaded,
            // so using it here would be circular. 4 MiB is far above any sane config.json.
            const long maxConfigBytes = 4L * 1024 * 1024;
            var length = new FileInfo(filePath).Length;
            if (length > maxConfigBytes)
                return Result<GcConfiguration>.Failure(
                    $"Configuration file '{filePath}' is too large ({length} bytes); max {maxConfigBytes} bytes.");

            // Deserialize UTF-8 directly from the stream: no intermediate string, no UTF-16 transcode.
            await using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);

            var typeInfo = GcJsonSerializerContext.Default.GcConfiguration;
            var config = await JsonSerializer.DeserializeAsync(fs, typeInfo);
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
        return ConfigurationMerger.Merge(target, source);
    }

    public void ClearCache()
    {
        // Stateless loader, caching is not used
    }
}