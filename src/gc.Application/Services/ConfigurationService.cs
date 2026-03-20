using System.Text.Json;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;
using gc.Application.Validators;

namespace gc.Application.Services;

public sealed class ConfigurationService
{
    private readonly ILogger _logger;
    private readonly ConfigurationValidator _validator;

    public ConfigurationService(ILogger logger, ConfigurationValidator validator)
    {
        _logger = logger;
        _validator = validator;
    }

    public async Task<Result> InitializeConfigAsync(string configDir = ".gc")
    {
        var configPath = Path.Combine(configDir, "config.json");

        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
            _logger.Info($"Created directory: {configDir}");
        }

        if (File.Exists(configPath))
        {
            _logger.Info($"Configuration file already exists: {configPath}");
            // In a real CLI we would ask, but here we just fail if it exists and we can't overwrite
            // Or we could take an 'overwrite' parameter.
            return Result.Failure($"Configuration file already exists: {configPath}. Manual intervention required to overwrite.");
        }

        return await WriteDefaultConfigAsync(configPath);
    }

    private async Task<Result> WriteDefaultConfigAsync(string configPath)
    {
        var defaultConfig = new
        {
            version = "1.0.0",
            limits = new
            {
                maxFileSize = "1MB",
                maxClipboardSize = "10MB",
                maxMemoryBytes = "100MB",
                maxFiles = 100000
            },
            discovery = new
            {
                mode = "auto",
                useGit = true,
                followSymlinks = false
            },
            markdown = new
            {
                fence = "```",
                projectStructureHeader = "_Project Structure:_ ",
                fileHeaderTemplate = "## File: {path}"
            }
        };

        try
        {
            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json);
            _logger.Info($"✓ Configuration file created: {configPath}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to create configuration file: {ex.Message}");
        }
    }

    public Result<ValidationResult> ValidateConfig(GcConfiguration config)
    {
        var validationResult = _validator.Validate(config);
        if (!validationResult.IsSuccess) return validationResult;

        _logger.Info(validationResult.Value!.ToString());
        return validationResult;
    }

    public Result DumpConfig(GcConfiguration config)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            _logger.Info(json);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to dump configuration: {ex.Message}");
        }
    }
}
