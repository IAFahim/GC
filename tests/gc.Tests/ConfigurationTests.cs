using gc.Application.Validators;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using gc.Infrastructure.Configuration;
using gc.Infrastructure.Logging;

namespace gc.Tests;

public class ConfigurationTests
{
    [Fact]
    public void GetDefaultConfiguration_ReturnsValidConfiguration()
    {
        // Act
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("1.0.0", config.Version);
        Assert.NotNull(config.Limits);
        Assert.NotNull(config.Discovery);
        Assert.NotNull(config.Filters);
        Assert.NotNull(config.Presets);
        Assert.NotNull(config.LanguageMappings);
        Assert.NotNull(config.Markdown);
        Assert.NotNull(config.Output);
        Assert.NotNull(config.Logging);
    }

    [Fact]
    public void GetDefaultConfiguration_HasAllBuiltInPresets()
    {
        // Act
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Assert
        Assert.True(config.Presets.ContainsKey("web"));
        Assert.True(config.Presets.ContainsKey("backend"));
        Assert.True(config.Presets.ContainsKey("dotnet"));
        Assert.True(config.Presets.ContainsKey("unity"));
        Assert.True(config.Presets.ContainsKey("java"));
        Assert.True(config.Presets.ContainsKey("cpp"));
        Assert.True(config.Presets.ContainsKey("script"));
        Assert.True(config.Presets.ContainsKey("data"));
        Assert.True(config.Presets.ContainsKey("config"));
        Assert.True(config.Presets.ContainsKey("build"));
        Assert.True(config.Presets.ContainsKey("docs"));
    }

    [Fact]
    public void GetDefaultConfiguration_PresetsHaveValidExtensions()
    {
        // Act
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Assert
        foreach (var preset in config.Presets)
        {
            Assert.NotNull(preset.Value.Extensions);
            Assert.NotEmpty(preset.Value.Extensions);

            // Check for empty or null extensions
            foreach (var ext in preset.Value.Extensions)
            {
                Assert.False(string.IsNullOrWhiteSpace(ext), $"Preset '{preset.Key}' contains empty extension");
            }
        }
    }

    [Fact]
    public void GetDefaultConfiguration_LanguageMappingsAreValid()
    {
        // Act
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Assert
        Assert.True(config.LanguageMappings.ContainsKey("js"));
        Assert.Equal("javascript", config.LanguageMappings["js"]);
        Assert.True(config.LanguageMappings.ContainsKey("ts"));
        Assert.Equal("typescript", config.LanguageMappings["ts"]);
        Assert.True(config.LanguageMappings.ContainsKey("py"));
        Assert.Equal("python", config.LanguageMappings["py"]);
        Assert.True(config.LanguageMappings.ContainsKey("cs"));
        Assert.Equal("csharp", config.LanguageMappings["cs"]);
    }

    [Fact]
    public void ValidateConfiguration_DefaultConfiguration_IsValid()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Act
        var result = new ConfigurationValidator().Validate(config).Value;

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateConfiguration_NullConfiguration_ReturnsInvalid()
    {
        // Act
        var result = new ConfigurationValidator().Validate(null);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Contains("null", result.Error);
    }

    [Fact]
    public void ValidateConfiguration_InvalidMemorySize_ReturnsInvalid()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Limits = config.Limits with { MaxFileSize = "invalid" } };

        // Act
        var result = new ConfigurationValidator().Validate(config).Value;

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("MaxFileSize") && e.Contains("invalid"));
    }

    [Fact]
    public void ValidateConfiguration_EmptyPresetExtensions_ReturnsInvalid()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();
        var customPresets = new Dictionary<string, PresetConfiguration>(config.Presets)
        {
            ["test"] = new PresetConfiguration
            {
                Extensions = Array.Empty<string>(),
                Description = "Test preset"
            }
        };
        config = config with { Presets = customPresets };

        // Act
        var result = new ConfigurationValidator().Validate(config).Value;

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("test") && e.Contains("no extensions"));
    }

    [Fact]
    public void ValidateConfiguration_InvalidDiscoveryMode_ReturnsInvalid()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Discovery = config.Discovery with { Mode = "invalid" } };

        // Act
        var result = new ConfigurationValidator().Validate(config).Value;

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("DiscoveryMode") && e.Contains("invalid"));
    }

    [Fact]
    public void ValidateMemorySize_ValidSizes_ReturnsTrue()
    {
        // Assert
        Assert.True(ConfigurationValidator.ValidateMemorySize("100B"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("100KB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("100MB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("1GB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("1.5GB"));
        Assert.True(ConfigurationValidator.ValidateMemorySize("0.5MB"));
    }

    [Fact]
    public void ValidateMemorySize_InvalidSizes_ReturnsFalse()
    {
        // Assert
        Assert.False(ConfigurationValidator.ValidateMemorySize(""));
        Assert.False(ConfigurationValidator.ValidateMemorySize("100"));
        Assert.False(ConfigurationValidator.ValidateMemorySize("MB"));
        Assert.False(ConfigurationValidator.ValidateMemorySize("100TB"));
        Assert.False(ConfigurationValidator.ValidateMemorySize("-100MB"));
        Assert.False(ConfigurationValidator.ValidateMemorySize("0MB"));
    }

    [Fact]
    public void LimitsConfiguration_ParseMemorySize_ReturnsCorrectBytes()
    {
        // Arrange
        var limits = new LimitsConfiguration
        {
            MaxFileSize = "100MB",
            MaxClipboardSize = "1GB",
            MaxMemoryBytes = "500KB"
        };

        // Act & Assert
        Assert.Equal(100 * 1024 * 1024, limits.GetMaxFileSizeBytes());
        Assert.Equal(1L * 1024 * 1024 * 1024, limits.GetMaxClipboardSizeBytes());
        Assert.Equal(500 * 1024, limits.GetMaxMemoryBytesValue());
    }

    [Fact]
    public async Task ConfigurationLoader_LoadConfigFromNonExistentFile_ReturnsNull()
    {
        // Arrange
        var loader = new ConfigurationLoader(new ConsoleLogger());
        
        // Act
        var result = await loader.LoadConfigFromFileAsync("/nonexistent/config.json");

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ConfigurationLoader_LoadConfigFromInvalidJson_ReturnsFailure()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var loader = new ConfigurationLoader(new ConsoleLogger());
        try
        {
            File.WriteAllText(tempFile, "invalid json {");

            // Act
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.NotNull(result.Error);
            Assert.Contains("Failed to load configuration file", result.Error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ConfigurationLoader_LoadConfigFromValidJson_ReturnsConfiguration()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        var loader = new ConfigurationLoader(new ConsoleLogger());
        try
        {
            var json = @"{
                ""version"": ""1.0.0"",
                ""limits"": {
                    ""maxFileSize"": ""2MB""
                },
                ""presets"": {
                    ""custom"": {
                        ""extensions"": [""xyz""],
                        ""description"": ""Custom preset""
                    }
                }
            }";
            File.WriteAllText(tempFile, json);

            // Act
            var result = await loader.LoadConfigFromFileAsync(tempFile);

            // Assert
            Assert.True(result.IsSuccess);
            var config = result.Value;
            Assert.NotNull(config);
            Assert.Equal("1.0.0", config.Version);
            Assert.Equal("2MB", config.Limits.MaxFileSize);
            Assert.True(config.Presets.ContainsKey("custom"));
            Assert.Equal("xyz", config.Presets["custom"].Extensions[0]);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ConfigurationLoader_MergeConfiguration_DefaultsAreCorrect()
    {
        // Arrange
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Assert
        Assert.Equal("1MB", config.Limits.MaxFileSize);
        Assert.Equal("```", config.Markdown.Fence);
    }

    [Fact]
    public void ConfigurationLoader_GetConfigDirectory_ReturnsValidPath()
    {
        // Arrange
        var loader = new ConfigurationLoader(new ConsoleLogger());

        // Act
        var configDir = loader.GetConfigDirectory();

        // Assert
        Assert.NotNull(configDir);
        Assert.NotEmpty(configDir);
    }

    [Fact]
    public void ConfigurationLoader_GetSystemConfigDirectory_ReturnsValidPath()
    {
        // Arrange
        var loader = new ConfigurationLoader(new ConsoleLogger());

        // Act
        var systemConfigDir = loader.GetSystemConfigDirectory();

        // Assert
        Assert.NotNull(systemConfigDir);
        Assert.NotEmpty(systemConfigDir);
    }

    [Fact]
    public async Task ConfigurationLoader_LoadConfiguration_NoConfigFiles_ReturnsDefaults()
    {
        // Arrange - Ensure no config files exist
        var loader = new ConfigurationLoader(new ConsoleLogger());
        loader.ClearCache();

        // Act
        var result = await loader.LoadConfigAsync();
        var config = result.Value;

        // Assert
        Assert.NotNull(config);
        Assert.Equal("1MB", config.Limits.MaxFileSize);
        Assert.Equal("```", config.Markdown.Fence);
    }

    [Fact]
    public async Task BackwardCompatibility_NoConfigFiles_WorksAsBefore()
    {
        // Arrange
        var loader = new ConfigurationLoader(new ConsoleLogger());
        loader.ClearCache();

        // Act - Load configuration should return defaults
        var result = await loader.LoadConfigAsync();
        var config = result.Value;

        // Assert - Verify defaults match old Constants values
        Assert.NotNull(config);
        Assert.Equal("```", config.Markdown.Fence); // Constants.MarkdownFence
        Assert.Equal("_Project Structure:_", config.Markdown.ProjectStructureHeader); // Constants.ProjectStructureHeader

        // Verify all built-in presets are available
        Assert.True(config.Presets.ContainsKey("web"));
        Assert.True(config.Presets.ContainsKey("backend"));
        Assert.True(config.Presets.ContainsKey("dotnet"));

        // Verify language mappings
        Assert.Equal("javascript", config.LanguageMappings["js"]);
        Assert.Equal("typescript", config.LanguageMappings["ts"]);
        Assert.Equal("python", config.LanguageMappings["py"]);
    }

    [Fact]
    public void PresetConfiguration_CanBeCustomized()
    {
        // Arrange
        var customPreset = new PresetConfiguration
        {
            Extensions = new[] { "custom1", "custom2" },
            Description = "Custom language"
        };

        // Act & Assert
        Assert.Equal(2, customPreset.Extensions.Length);
        Assert.Contains("custom1", customPreset.Extensions);
        Assert.Contains("custom2", customPreset.Extensions);
        Assert.Equal("Custom language", customPreset.Description);
    }

    [Fact]
    public void BuiltInPresets_PresetWeb_HasExpectedExtensions()
    {
        // Act
        var webExtensions = BuiltInPresets.PresetWeb;

        // Assert
        Assert.Contains("html", webExtensions);
        Assert.Contains("css", webExtensions);
        Assert.Contains("js", webExtensions);
        Assert.Contains("jsx", webExtensions);
        Assert.Contains("ts", webExtensions);
        Assert.Contains("tsx", webExtensions);
    }

    [Fact]
    public void BuiltInPresets_PresetDotnet_HasExpectedExtensions()
    {
        // Act
        var dotnetExtensions = BuiltInPresets.PresetDotnet;

        // Assert
        Assert.Contains("cs", dotnetExtensions);
        Assert.Contains("razor", dotnetExtensions);
        Assert.Contains("csproj", dotnetExtensions);
        Assert.Contains("json", dotnetExtensions);
    }

    [Fact]
    public void BuiltInPresets_PresetUnity_HasExpectedExtensions()
    {
        // Act
        var unityExtensions = BuiltInPresets.PresetUnity;

        // Assert
        Assert.Contains("cs", unityExtensions);
        Assert.Contains("shader", unityExtensions);
        Assert.Contains("cginc", unityExtensions);
        Assert.Contains("hlsl", unityExtensions);
        Assert.Contains("glsl", unityExtensions);
    }

    [Fact]
    public void BuiltInPresets_SystemIgnoredPatterns_ContainsExpectedPatterns()
    {
        // Act
        var ignoredPatterns = BuiltInPresets.SystemIgnoredPatterns;

        // Assert
        Assert.Contains("node_modules/", ignoredPatterns);
        Assert.Contains("bin/", ignoredPatterns);
        Assert.Contains("obj/", ignoredPatterns);
        Assert.Contains(".git/", ignoredPatterns);
        Assert.Contains(".DS_Store", ignoredPatterns);
        Assert.Contains("Thumbs.db", ignoredPatterns);
    }

    [Fact]
    public void ConfigurationValidator_ValidationResult_ToString_IncludesErrors()
    {
        // Arrange
        var result = new ValidationResult
        {
            IsValid = false,
            Errors = new List<string> { "Error 1", "Error 2" },
            Warnings = new List<string> { "Warning 1" }
        };

        // Act
        var output = result.ToString();

        // Assert
        Assert.Contains("invalid", output.ToLowerInvariant());
        Assert.Contains("Error 1", output);
        Assert.Contains("Error 2", output);
        Assert.Contains("Warning 1", output);
    }

    [Fact]
    public void ConfigurationValidator_ValidationResult_ToString_IncludesSuccessMessage()
    {
        // Arrange
        var result = new ValidationResult
        {
            IsValid = true,
            Errors = new List<string>(),
            Warnings = new List<string>()
        };

        // Act
        var output = result.ToString();

        // Assert
        Assert.Contains("valid", output.ToLowerInvariant());
    }
}
