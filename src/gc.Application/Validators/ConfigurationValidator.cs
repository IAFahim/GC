using gc.Domain.Common;
using gc.Domain.Models.Configuration;

namespace gc.Application.Validators;

public sealed class ConfigurationValidator
{
    public Result<ValidationResult> Validate(GcConfiguration? config)
    {
        if (config == null)
        {
            return Result<ValidationResult>.Failure("Configuration is null");
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateLimits(config.Limits, errors, warnings);
        ValidateDiscovery(config.Discovery, errors, warnings);
        ValidateFilters(config.Filters, errors, warnings);
        ValidatePresets(config.Presets, errors, warnings);
        ValidateLanguageMappings(config.LanguageMappings, errors, warnings);
        ValidateMarkdown(config.Markdown, errors, warnings);
        ValidateOutput(config.Output, errors, warnings);
        ValidateLogging(config.Logging, errors, warnings);

        return Result<ValidationResult>.Success(new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.AsReadOnly(),
            Warnings = warnings.AsReadOnly()
        });
    }

    private static void ValidateLimits(LimitsConfiguration? limits, List<string> errors, List<string> warnings)
    {
        if (limits == null)
        {
            warnings.Add("Limits configuration is null, using defaults");
            return;
        }

        if (!ValidateMemorySize(limits.MaxFileSize))
            errors.Add($"Invalid MaxFileSize format: '{limits.MaxFileSize}'. Expected format: 100MB, 1GB, etc.");

        if (!ValidateMemorySize(limits.MaxClipboardSize))
            errors.Add($"Invalid MaxClipboardSize format: '{limits.MaxClipboardSize}'. Expected format: 100MB, 1GB, etc.");

        if (!ValidateMemorySize(limits.MaxMemoryBytes))
            errors.Add($"Invalid MaxMemoryBytes format: '{limits.MaxMemoryBytes}'. Expected format: 100MB, 1GB, etc.");

        if (limits.MaxFiles < 1)
            errors.Add($"MaxFiles must be at least 1, got: {limits.MaxFiles}");
    }

    private static void ValidateDiscovery(DiscoveryConfiguration? discovery, List<string> errors, List<string> warnings)
    {
        if (discovery == null)
        {
            warnings.Add("Discovery configuration is null, using defaults");
            return;
        }

        if (discovery.Mode != null)
        {
            var mode = discovery.Mode.ToLowerInvariant();
            if (mode != "auto" && mode != "git" && mode != "filesystem")
                errors.Add($"Invalid DiscoveryMode: '{discovery.Mode}'. Must be: auto, git, or filesystem");
        }

        // Validate cluster configuration if present
        if (discovery.Cluster != null)
        {
            ValidateCluster(discovery.Cluster, errors, warnings);
        }
    }

    private static void ValidateCluster(ClusterConfiguration cluster, List<string> errors, List<string> warnings)
    {
        if (cluster.MaxDepth < 0)
            errors.Add($"Cluster MaxDepth must be non-negative, got: {cluster.MaxDepth}");

        if (cluster.MaxDepth > 10)
            warnings.Add($"Cluster MaxDepth is very high ({cluster.MaxDepth}), this may be slow");

        if (cluster.MaxParallelRepos < 0)
            errors.Add($"Cluster MaxParallelRepos must be non-negative, got: {cluster.MaxParallelRepos}");

        if (string.IsNullOrWhiteSpace(cluster.RepoSeparator))
            warnings.Add("Cluster RepoSeparator is empty, repos will be concatenated without visual separation");

        if (cluster.SkipDirectories != null)
        {
            foreach (var skip in cluster.SkipDirectories)
            {
                if (string.IsNullOrWhiteSpace(skip))
                    warnings.Add("Empty entry in Cluster SkipDirectories");
            }
        }
    }

    private static void ValidateFilters(FiltersConfiguration? filters, List<string> errors, List<string> warnings)
    {
        if (filters == null)
        {
            warnings.Add("Filters configuration is null, using defaults");
            return;
        }

        if (filters.SystemIgnoredPatterns != null)
            foreach (var pattern in filters.SystemIgnoredPatterns)
                if (string.IsNullOrWhiteSpace(pattern))
                    warnings.Add("Empty pattern in SystemIgnoredPatterns");
    }

    private static void ValidatePresets(Dictionary<string, PresetConfiguration>? presets, List<string> errors, List<string> warnings)
    {
        if (presets == null || presets.Count == 0)
        {
            warnings.Add("No presets defined, using built-in defaults");
            return;
        }

        foreach (var presetKvp in presets)
        {
            ValidatePreset(presetKvp.Key, presetKvp.Value, errors, warnings);
        }
    }

    private static void ValidatePreset(string presetName, PresetConfiguration? preset, List<string> errors, List<string> warnings)
    {
        if (preset == null)
        {
            errors.Add($"Preset '{presetName}' is null");
            return;
        }

        if (preset.Extensions == null || preset.Extensions.Length == 0)
        {
            errors.Add($"Preset '{presetName}' has no extensions defined");
        }
        else
        {
            ValidatePresetExtensions(presetName, preset.Extensions, warnings);
        }
    }

    private static void ValidatePresetExtensions(string presetName, string[] extensions, List<string> warnings)
    {
        foreach (var ext in extensions)
            if (string.IsNullOrWhiteSpace(ext))
                warnings.Add($"Preset '{presetName}' contains empty extension");

        var duplicates = extensions
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);

        foreach (var duplicate in duplicates)
            warnings.Add($"Preset '{presetName}' contains duplicate extension: '{duplicate}'");
    }

    private static void ValidateLanguageMappings(Dictionary<string, string>? mappings, List<string> errors, List<string> warnings)
    {
        if (mappings == null)
        {
            warnings.Add("Language mappings are null, using built-in defaults");
            return;
        }

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key))
                errors.Add("Language mapping has empty key");

            if (string.IsNullOrWhiteSpace(mapping.Value))
                errors.Add($"Language mapping for '{mapping.Key}' has empty value");
        }
    }

    private static void ValidateMarkdown(MarkdownConfiguration? markdown, List<string> errors, List<string> warnings)
    {
        if (markdown == null)
        {
            warnings.Add("Markdown configuration is null, using defaults");
            return;
        }

        if (string.IsNullOrWhiteSpace(markdown.Fence))
            errors.Add("Markdown fence cannot be empty");

        if (markdown.ProjectStructureHeader != null && markdown.ProjectStructureHeader.Length > 200)
            warnings.Add("ProjectStructureHeader is very long, consider shortening");

        if (markdown.FileHeaderTemplate != null && !markdown.FileHeaderTemplate.Contains("{path}"))
            warnings.Add("FileHeaderTemplate does not contain '{path}' placeholder");

        ValidateLanguageDetection(markdown.LanguageDetection, warnings);
    }

    private static void ValidateLanguageDetection(string? detection, List<string> warnings)
    {
        if (detection != null)
        {
            var lower = detection.ToLowerInvariant();
            if (lower != "extension" && lower != "content" && lower != "filename")
                warnings.Add($"Invalid LanguageDetection: '{detection}'. Expected: extension, content, or filename");
        }
    }

    private static void ValidateOutput(OutputConfiguration? output, List<string> errors, List<string> warnings)
    {
        if (output == null)
        {
            warnings.Add("Output configuration is null, using defaults");
            return;
        }

        if (output.DefaultFormat != null)
        {
            var format = output.DefaultFormat.ToLowerInvariant();
            if (format != "markdown" && format != "plain" && format != "json")
                warnings.Add($"Invalid DefaultFormat: '{output.DefaultFormat}'. Expected: markdown, plain, or json");
        }
    }

    private static void ValidateLogging(LoggingConfiguration? logging, List<string> errors, List<string> warnings)
    {
        if (logging == null)
        {
            warnings.Add("Logging configuration is null, using defaults");
            return;
        }

        if (logging.Level != null)
        {
            var level = logging.Level.ToLowerInvariant();
            if (level != "normal" && level != "verbose" && level != "debug" && level != "quiet")
                warnings.Add($"Invalid LogLevel: '{logging.Level}'. Expected: normal, verbose, debug, or quiet");
        }
    }

    public static bool ValidateMemorySize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return false;

        size = size.Trim().ToUpperInvariant();

        var hasValidSuffix = size.EndsWith("B") ||
                             size.EndsWith("KB") ||
                             size.EndsWith("MB") ||
                             size.EndsWith("GB");

        if (!hasValidSuffix)
            return false;

        if (size.Length > 2 && (size.EndsWith("KB") || size.EndsWith("MB") || size.EndsWith("GB")))
            size = size[..^2];
        else if (size.EndsWith("B"))
            size = size[..^1];

        return double.TryParse(size, out var value) && value > 0;
    }
}
