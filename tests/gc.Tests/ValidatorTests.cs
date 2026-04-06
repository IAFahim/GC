using gc.Application.Validators;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;

namespace gc.Tests;

public class ValidatorTests
{
    // ── Cluster validation ──────────────────────────────────────────────

    [Fact]
    public void Validate_ClusterConfig_MaxDepthNegative_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with
            {
                Cluster = new ClusterConfiguration { MaxDepth = -1 }
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxDepth") && e.Contains("non-negative"));
    }

    [Fact]
    public void Validate_ClusterConfig_MaxDepthAbove10_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with
            {
                Cluster = new ClusterConfiguration { MaxDepth = 15 }
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("MaxDepth") && w.Contains("very high"));
    }

    [Fact]
    public void Validate_ClusterConfig_MaxParallelReposNegative_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with
            {
                Cluster = new ClusterConfiguration { MaxParallelRepos = -5 }
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxParallelRepos") && e.Contains("non-negative"));
    }

    [Fact]
    public void Validate_ClusterConfig_EmptyRepoSeparator_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with
            {
                Cluster = new ClusterConfiguration { RepoSeparator = "" }
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("RepoSeparator") && w.Contains("empty"));
    }

    [Fact]
    public void Validate_ClusterConfig_EmptySkipDirectoryEntries_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with
            {
                Cluster = new ClusterConfiguration
                {
                    SkipDirectories = ["valid-dir", "", "  ", "another"]
                }
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        // Should have warnings for the empty/whitespace entries
        Assert.True(result.Warnings.Count >= 2);
        Assert.Contains(result.Warnings, w => w.Contains("SkipDirectories") && w.Contains("Empty"));
    }

    [Fact]
    public void Validate_ClusterConfig_ValidConfig_NoErrors()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Discovery = config.Discovery with
            {
                Cluster = new ClusterConfiguration
                {
                    MaxDepth = 3,
                    MaxParallelRepos = 4,
                    RepoSeparator = "---",
                    SkipDirectories = ["archive", "deprecated"]
                }
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    // ── Null sub-configs produce warnings ───────────────────────────────

    [Fact]
    public void Validate_NullLimits_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Limits = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Limits") && w.Contains("null")));
    }

    [Fact]
    public void Validate_NullFilters_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Filters = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Filters") && w.Contains("null")));
    }

    [Fact]
    public void Validate_NullMarkdown_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Markdown = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Markdown") && w.Contains("null")));
    }

    [Fact]
    public void Validate_NullOutput_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Output = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Output") && w.Contains("null")));
    }

    [Fact]
    public void Validate_NullLogging_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Logging = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Logging") && w.Contains("null")));
    }

    // ── Markdown validation ─────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyFence_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Markdown = config.Markdown with { Fence = "" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("fence") && e.Contains("empty"));
    }

    [Fact]
    public void Validate_LongProjectStructureHeader_ReturnsWarning()
    {
        var longHeader = new string('A', 201);
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Markdown = config.Markdown with { ProjectStructureHeader = longHeader } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("ProjectStructureHeader") && w.Contains("very long"));
    }

    [Fact]
    public void Validate_FileHeaderTemplateNoPath_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Markdown = config.Markdown with { FileHeaderTemplate = "no-placeholder-here" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("FileHeaderTemplate") && w.Contains("{path}"));
    }

    [Fact]
    public void Validate_InvalidLanguageDetection_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Markdown = config.Markdown with { LanguageDetection = "invalid-mode" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("LanguageDetection") && w.Contains("invalid-mode"));
    }

    // ── Output validation ───────────────────────────────────────────────

    [Fact]
    public void Validate_InvalidDefaultFormat_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Output = config.Output with { DefaultFormat = "xml" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("DefaultFormat") && w.Contains("xml"));
    }

    // ── Logging validation ──────────────────────────────────────────────

    [Fact]
    public void Validate_InvalidLogLevel_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Logging = config.Logging with { Level = "trace" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("LogLevel") && w.Contains("trace"));
    }

    // ── Language mapping validation ─────────────────────────────────────

    [Fact]
    public void Validate_EmptyMappingKey_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var mappings = new Dictionary<string, string>(config.LanguageMappings, StringComparer.OrdinalIgnoreCase)
        {
            [""] = "somelang"
        };
        config = config with { LanguageMappings = mappings };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty key"));
    }

    [Fact]
    public void Validate_EmptyMappingValue_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var mappings = new Dictionary<string, string>(config.LanguageMappings, StringComparer.OrdinalIgnoreCase)
        {
            ["xyz"] = ""
        };
        config = config with { LanguageMappings = mappings };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("xyz") && e.Contains("empty value"));
    }

    // ── Filters validation ──────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyIgnoredPatterns_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Filters = config.Filters with
            {
                SystemIgnoredPatterns = ["valid-pattern", "", "  "]
            }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("SystemIgnoredPatterns") && w.Contains("Empty pattern"));
    }

    // ── Presets validation ──────────────────────────────────────────────

    [Fact]
    public void Validate_NullPreset_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var presets = new Dictionary<string, PresetConfiguration>(config.Presets, StringComparer.OrdinalIgnoreCase)
        {
            ["broken"] = null!
        };
        config = config with { Presets = presets };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("broken") && e.Contains("null"));
    }

    [Fact]
    public void Validate_DuplicatePresetExtensions_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var presets = new Dictionary<string, PresetConfiguration>(config.Presets, StringComparer.OrdinalIgnoreCase)
        {
            ["dupe"] = new PresetConfiguration
            {
                Extensions = ["cs", "CS", "cs"],
                Description = "Duplicate test"
            }
        };
        config = config with { Presets = presets };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("dupe") && w.Contains("duplicate extension"));
    }

    [Fact]
    public void Validate_NullPresets_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Presets = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("presets") && w.Contains("No presets")));
    }

    // ── ValidateMemorySize edge cases ───────────────────────────────────

    [Fact]
    public void ValidateMemorySize_JustNumber_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize("100"));
    }

    [Fact]
    public void ValidateMemorySize_JustSuffix_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize("MB"));
    }

    [Fact]
    public void ValidateMemorySize_Negative_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize("-100MB"));
    }

    [Fact]
    public void ValidateMemorySize_ZeroB_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize("0B"));
    }

    [Fact]
    public void ValidateMemorySize_0_5MB_ReturnsTrue()
    {
        Assert.True(ConfigurationValidator.ValidateMemorySize("0.5MB"));
    }

    [Fact]
    public void ValidateMemorySize_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize(null));
        Assert.False(ConfigurationValidator.ValidateMemorySize(""));
        Assert.False(ConfigurationValidator.ValidateMemorySize("   "));
    }

    [Fact]
    public void ValidateMemorySize_ValidSuffixes_ReturnExpected()
    {
        Assert.True(ConfigurationValidator.ValidateMemorySize("100B"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("100KB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("100MB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("1GB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("1.5GB"));
    }

    [Fact]
    public void ValidateMemorySize_UnsupportedSuffix_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize("100TB"));
        Assert.False(ConfigurationValidator.ValidateMemorySize("100PB"));
    }

    [Fact]
    public void ValidateMemorySize_ZeroMB_ReturnsFalse()
    {
        Assert.False(ConfigurationValidator.ValidateMemorySize("0MB"));
    }

    // ── ValidationResult ToString ───────────────────────────────────────

    [Fact]
    public void ValidationResult_WithOnlyWarnings_ToString()
    {
        var result = new ValidationResult
        {
            IsValid = true,
            Errors = Array.Empty<string>(),
            Warnings = new List<string> { "Warning A", "Warning B" }
        };

        var output = result.ToString();

        Assert.Contains("valid", output.ToLowerInvariant());
        Assert.Contains("Warning A", output);
        Assert.Contains("Warning B", output);
        Assert.DoesNotContain("Errors:", output);
    }

    // ── Additional edge-case coverage ───────────────────────────────────

    [Fact]
    public void Validate_Limits_MaxFilesZero_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Limits = config.Limits with { MaxFiles = 0 } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxFiles") && e.Contains("at least 1"));
    }

    [Fact]
    public void Validate_Limits_InvalidMaxClipboardSize_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Limits = config.Limits with { MaxClipboardSize = "bad" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxClipboardSize"));
    }

    [Fact]
    public void Validate_Limits_InvalidMaxMemoryBytes_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Limits = config.Limits with { MaxMemoryBytes = "xyz" } };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxMemoryBytes"));
    }

    [Fact]
    public void Validate_Discovery_NullDiscovery_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Discovery = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Discovery") && w.Contains("null")));
    }

    [Fact]
    public void Validate_NullLanguageMappings_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { LanguageMappings = null! };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("Language mappings") && w.Contains("null")));
    }

    [Fact]
    public void Validate_EmptyPresetsDictionary_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration() with { Presets = new Dictionary<string, PresetConfiguration>() };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.Warnings.Any(w => w.Contains("presets") && w.Contains("default")));
    }

    [Fact]
    public void Validate_PresetWithEmptyExtensions_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var presets = new Dictionary<string, PresetConfiguration>(config.Presets, StringComparer.OrdinalIgnoreCase)
        {
            ["empty-ext"] = new PresetConfiguration
            {
                Extensions = Array.Empty<string>(),
                Description = "No extensions"
            }
        };
        config = config with { Presets = presets };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty-ext") && e.Contains("no extensions"));
    }

    [Fact]
    public void Validate_PresetWithNullExtensions_ReturnsError()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var presets = new Dictionary<string, PresetConfiguration>(config.Presets, StringComparer.OrdinalIgnoreCase)
        {
            ["null-ext"] = new PresetConfiguration
            {
                Extensions = null!,
                Description = "Null extensions"
            }
        };
        config = config with { Presets = presets };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("null-ext") && e.Contains("no extensions"));
    }

    [Fact]
    public void Validate_PresetWithEmptyExtensionEntries_ReturnsWarning()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        var presets = new Dictionary<string, PresetConfiguration>(config.Presets, StringComparer.OrdinalIgnoreCase)
        {
            ["ws"] = new PresetConfiguration
            {
                Extensions = ["cs", ""],
                Description = "Has whitespace ext"
            }
        };
        config = config with { Presets = presets };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.Contains(result.Warnings, w => w.Contains("ws") && w.Contains("empty extension"));
    }

    [Fact]
    public void Validate_MultipleErrors_AllReported()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with
        {
            Limits = config.Limits with { MaxFileSize = "bad", MaxFiles = -1 },
            Markdown = config.Markdown with { Fence = "" }
        };

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
    }

    [Fact]
    public void Validate_ValidDefaultConfig_IsValid()
    {
        var config = BuiltInPresets.GetDefaultConfiguration();

        var result = new ConfigurationValidator().Validate(config).Value;

        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NullConfig_ReturnsFailure()
    {
        var result = new ConfigurationValidator().Validate(null);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("null", result.Error);
    }

    [Fact]
    public void ValidateMemorySize_WhitespacePadding_IsTrimmed()
    {
        Assert.True(ConfigurationValidator.ValidateMemorySize("  100MB  "));
    }

    [Fact]
    public void ValidateMemorySize_LowerCaseSuffix_ReturnsTrue()
    {
        Assert.True(ConfigurationValidator.ValidateMemorySize("100mb"));
    }

    [Fact]
    public void ValidateMemorySize_MixedCaseSuffix_ReturnsTrue()
    {
        Assert.True(ConfigurationValidator.ValidateMemorySize("100Mb"));
    }
}
