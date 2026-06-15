using System.Text;
using System.Text.Json;
using gc.Application.Validators;
using gc.Domain.Common;
using gc.Domain.Interfaces;
using gc.Domain.Models.Configuration;

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
            return Result.Failure(
                $"Configuration file already exists: {configPath}. Manual intervention required to overwrite.");
        }

        return await WriteDefaultConfigAsync(configPath);
    }

    private async Task<Result> WriteDefaultConfigAsync(string configPath)
    {
        var defaultConfig = new DefaultConfigOptions();

        try
        {
            var json = JsonSerializer.Serialize(defaultConfig, GcJsonSerializerContext.Default.DefaultConfigOptions);
            await File.WriteAllTextAsync(configPath, json, new UTF8Encoding(false));
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
            // Use the indented source-gen context so the dump is human-readable AND NativeAOT-safe.
            // (The previous local JsonSerializerOptions was ignored by the JsonTypeInfo overload.)
            var json = JsonSerializer.Serialize(config, GcIndentedJsonContext.Default.GcConfiguration);
            _logger.Info(json);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to dump configuration: {ex.Message}");
        }
    }
}