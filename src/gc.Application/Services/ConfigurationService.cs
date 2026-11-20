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

    /// <summary>Creates an empty project settings file (settings, defaults, profiles) at <paramref name="path" />.</summary>
    public async Task<Result> InitializeConfigAsync(string path = ".gc.json")
    {
        if (File.Exists(path))
        {
            _logger.Info($"Settings file already exists: {path}");
            return Result.Failure($"Settings file already exists: {path}. Manual intervention required to overwrite.");
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.Info($"Created directory: {directory}");
            }

            var skeleton = string.Join('\n', "{", "  \"settings\": {},", "  \"defaults\": [],", "  \"profiles\": {}", "}", "");
            await File.WriteAllTextAsync(path, skeleton, new UTF8Encoding(false));
            _logger.Info($"✓ Settings file created: {path}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to create settings file: {ex.Message}");
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