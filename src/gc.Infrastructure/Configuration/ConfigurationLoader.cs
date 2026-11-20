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

    public async Task<Result<GcConfiguration>> LoadConfigAsync(bool useCache = true, string? rootPath = null)
    {
        _logger.Debug("Loading configuration...");

        var config = BuiltInPresets.GetDefaultConfiguration();
        _logger.Debug("Loaded built-in default configuration");

        // Settings come from two files only: the user file, then the project file (project wins).
        var userSettings = await SettingsStore.LoadAsync(SettingsStore.UserPath(GetConfigDirectory()));
        if (userSettings.Settings != null)
            config = MergeConfiguration(config, userSettings.Settings, "User settings");

        var projectSettings = await SettingsStore.LoadAsync(
            SettingsStore.FindProjectPath(rootPath ?? Directory.GetCurrentDirectory()));
        if (projectSettings.Settings != null)
            config = MergeConfiguration(config, projectSettings.Settings, "Project settings");

        return Result<GcConfiguration>.Success(config);
    }

    public async Task<Result<GcConfiguration>> LoadConfigFromFileAsync(string filePath)
    {
        try
        {
            // Open first and take the length from the stream (one open+fstat) instead of
            // File.Exists + FileInfo.Length + open (three metadata syscalls). Missing/inaccessible
            // files surface as the typed exceptions handled below, preserving the same messages.
            await using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);

            // Hardcoded guard: the configurable MaxFileSize lives inside the very config being loaded,
            // so using it here would be circular. 4 MiB is far above any sane config.json.
            const long maxConfigBytes = 4L * 1024 * 1024;
            if (fs.Length > maxConfigBytes)
                return Result<GcConfiguration>.Failure(
                    $"Configuration file '{filePath}' is too large ({fs.Length} bytes); max {maxConfigBytes} bytes.");

            // Deserialize UTF-8 directly from the stream: no intermediate string, no UTF-16 transcode.
            var typeInfo = GcJsonSerializerContext.Default.GcConfiguration;
            var config = await JsonSerializer.DeserializeAsync(fs, typeInfo);
            return config != null
                ? Result<GcConfiguration>.Success(config)
                : Result<GcConfiguration>.Failure($"Failed to deserialize configuration from {filePath}");
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Result<GcConfiguration>.Failure($"Configuration file not found: {filePath}");
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
