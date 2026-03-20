using System.Text;

namespace gc.Data;

/// <summary>
/// Validates GC configuration objects and files.
/// Provides detailed error reporting for invalid configurations.
/// </summary>
public static class ConfigurationValidator
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (IsValid)
            {
                sb.AppendLine("✓ Configuration is valid");
                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  ⚠ {warning}");
                    }
                }
            }
            else
            {
                sb.AppendLine("✗ Configuration is invalid");
                sb.AppendLine();
                sb.AppendLine("Errors:");
                foreach (var error in Errors)
                {
                    sb.AppendLine($"  ✗ {error}");
                }

                if (Warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var warning in Warnings)
                    {
                        sb.AppendLine($"  ⚠ {warning}");
                    }
                }
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Validate a complete configuration object.
    /// </summary>
    public static ValidationResult ValidateConfiguration(GcConfiguration? config)
    {
        var result = new ValidationResult { IsValid = true };

        if (config == null)
        {
            result.IsValid = false;
            result.Errors.Add("Configuration is null");
            return result;
        }

        // Validate limits
        ValidateLimits(config.Limits, result);

        // Validate discovery
        ValidateDiscovery(config.Discovery, result);

        // Validate filters
        ValidateFilters(config.Filters, result);

        // Validate presets
        ValidatePresets(config.Presets, result);

        // Validate language mappings
        ValidateLanguageMappings(config.LanguageMappings, result);

        // Validate markdown configuration
        ValidateMarkdown(config.Markdown, result);

        // Validate output configuration
        ValidateOutput(config.Output, result);

        // Validate logging configuration
        ValidateLogging(config.Logging, result);

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private static void ValidateLimits(LimitsConfiguration? limits, ValidationResult result)
    {
        if (limits == null)
        {
            result.Warnings.Add("Limits configuration is null, using defaults");
            return;
        }

        if (!ValidateMemorySize(limits.MaxFileSize))
            result.Errors.Add($"Invalid MaxFileSize format: '{limits.MaxFileSize}'. Expected format: 100MB, 1GB, etc.");

        if (!ValidateMemorySize(limits.MaxClipboardSize))
            result.Errors.Add($"Invalid MaxClipboardSize format: '{limits.MaxClipboardSize}'. Expected format: 100MB, 1GB, etc.");

        if (!ValidateMemorySize(limits.MaxMemoryBytes))
            result.Errors.Add($"Invalid MaxMemoryBytes format: '{limits.MaxMemoryBytes}'. Expected format: 100MB, 1GB, etc.");

        if (limits.MaxFiles < 1)
            result.Errors.Add($"MaxFiles must be at least 1, got: {limits.MaxFiles}");
    }

    private static void ValidateDiscovery(DiscoveryConfiguration? discovery, ValidationResult result)
    {
        if (discovery == null)
        {
            result.Warnings.Add("Discovery configuration is null, using defaults");
            return;
        }

        if (discovery.Mode != null)
        {
            var mode = discovery.Mode.ToLowerInvariant();
            if (mode != "auto" && mode != "git" && mode != "filesystem")
                result.Errors.Add($"Invalid DiscoveryMode: '{discovery.Mode}'. Must be: auto, git, or filesystem");
        }
    }

    private static void ValidateFilters(FiltersConfiguration? filters, ValidationResult result)
    {
        if (filters == null)
        {
            result.Warnings.Add("Filters configuration is null, using defaults");
            return;
        }

        if (filters.SystemIgnoredPatterns != null)
        {
            foreach (var pattern in filters.SystemIgnoredPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                    result.Warnings.Add("Empty pattern in SystemIgnoredPatterns");
            }
        }
    }

    private static void ValidatePresets(Dictionary<string, PresetConfiguration>? presets, ValidationResult result)
    {
        if (presets == null || presets.Count == 0)
        {
            result.Warnings.Add("No presets defined, using built-in defaults");
            return;
        }

        foreach (var presetKvp in presets)
        {
            var presetName = presetKvp.Key;
            var preset = presetKvp.Value;

            if (preset == null)
            {
                result.Errors.Add($"Preset '{presetName}' is null");
                continue;
            }

            if (preset.Extensions == null || preset.Extensions.Length == 0)
            {
                result.Errors.Add($"Preset '{presetName}' has no extensions defined");
            }
            else
            {
                // Check for empty or whitespace extensions
                foreach (var ext in preset.Extensions)
                {
                    if (string.IsNullOrWhiteSpace(ext))
                    {
                        result.Warnings.Add($"Preset '{presetName}' contains empty extension");
                    }
                }
            }

            // Check for duplicate extensions within preset
            if (preset.Extensions != null)
            {
                var duplicates = preset.Extensions
                    .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key);

                foreach (var duplicate in duplicates)
                {
                    result.Warnings.Add($"Preset '{presetName}' contains duplicate extension: '{duplicate}'");
                }
            }
        }
    }

    private static void ValidateLanguageMappings(Dictionary<string, string>? mappings, ValidationResult result)
    {
        if (mappings == null)
        {
            result.Warnings.Add("Language mappings are null, using built-in defaults");
            return;
        }

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key))
                result.Errors.Add("Language mapping has empty key");

            if (string.IsNullOrWhiteSpace(mapping.Value))
                result.Errors.Add($"Language mapping for '{mapping.Key}' has empty value");
        }
    }

    private static void ValidateMarkdown(MarkdownConfiguration? markdown, ValidationResult result)
    {
        if (markdown == null)
        {
            result.Warnings.Add("Markdown configuration is null, using defaults");
            return;
        }

        if (string.IsNullOrWhiteSpace(markdown.Fence))
            result.Errors.Add("Markdown fence cannot be empty");

        if (markdown.ProjectStructureHeader != null && markdown.ProjectStructureHeader.Length > 200)
            result.Warnings.Add("ProjectStructureHeader is very long, consider shortening");

        if (markdown.FileHeaderTemplate != null && !markdown.FileHeaderTemplate.Contains("{path}"))
            result.Warnings.Add("FileHeaderTemplate does not contain '{path}' placeholder");

        if (markdown.LanguageDetection != null)
        {
            var detection = markdown.LanguageDetection.ToLowerInvariant();
            if (detection != "extension" && detection != "content" && detection != "filename")
                result.Warnings.Add($"Invalid LanguageDetection: '{markdown.LanguageDetection}'. Expected: extension, content, or filename");
        }
    }

    private static void ValidateOutput(OutputConfiguration? output, ValidationResult result)
    {
        if (output == null)
        {
            result.Warnings.Add("Output configuration is null, using defaults");
            return;
        }

        if (output.DefaultFormat != null)
        {
            var format = output.DefaultFormat.ToLowerInvariant();
            if (format != "markdown" && format != "plain" && format != "json")
                result.Warnings.Add($"Invalid DefaultFormat: '{output.DefaultFormat}'. Expected: markdown, plain, or json");
        }
    }

    private static void ValidateLogging(LoggingConfiguration? logging, ValidationResult result)
    {
        if (logging == null)
        {
            result.Warnings.Add("Logging configuration is null, using defaults");
            return;
        }

        if (logging.Level != null)
        {
            var level = logging.Level.ToLowerInvariant();
            if (level != "normal" && level != "verbose" && level != "debug" && level != "quiet")
                result.Warnings.Add($"Invalid LogLevel: '{logging.Level}'. Expected: normal, verbose, debug, or quiet");
        }
    }

    /// <summary>
    /// Validate memory size format (e.g., "100MB", "1GB").
    /// </summary>
    public static bool ValidateMemorySize(string? size)
    {
        if (string.IsNullOrWhiteSpace(size))
            return false;

        size = size.Trim().ToUpperInvariant();

        // Check suffix
        bool hasValidSuffix = size.EndsWith("B") ||
                             size.EndsWith("KB") ||
                             size.EndsWith("MB") ||
                             size.EndsWith("GB");

        if (!hasValidSuffix)
            return false;

        // Remove suffix and validate number
        if (size.Length > 2 && (size.EndsWith("KB") || size.EndsWith("MB") || size.EndsWith("GB")))
            size = size.Substring(0, size.Length - 2);
        else if (size.EndsWith("B") && !size.EndsWith("KB") && !size.EndsWith("MB") && !size.EndsWith("GB"))
            size = size.Substring(0, size.Length - 1);

        return double.TryParse(size, out var value) && value > 0;
    }
}
