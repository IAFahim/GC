using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using Xunit;

namespace gc.Tests.FeatureTests;

public class CliParserTests
{
    [Fact]
    public void Parse_CompactFlag_SetsMildLevel()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--compact" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CompactLevel.Mild, result.Value.Compact);
    }

    [Fact]
    public void Parse_CompactLevelFlag_SetsSpecifiedLevel()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Act & Assert - Mild
        var mildResult = parser.Parse(new[] { "--compact-level", "mild" }, config);
        Assert.True(mildResult.IsSuccess);
        Assert.Equal(CompactLevel.Mild, mildResult.Value.Compact);

        // Act & Assert - Aggressive
        var aggressiveResult = parser.Parse(new[] { "--compact-level", "aggressive" }, config);
        Assert.True(aggressiveResult.IsSuccess);
        Assert.Equal(CompactLevel.Aggressive, aggressiveResult.Value.Compact);

        // Act & Assert - None
        var noneResult = parser.Parse(new[] { "--compact-level", "none" }, config);
        Assert.True(noneResult.IsSuccess);
        Assert.Equal(CompactLevel.None, noneResult.Value.Compact);
    }

    [Fact]
    public void Parse_CompactLevelCaseInsensitive_Works()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Act & Assert
        var result1 = parser.Parse(new[] { "--compact-level", "MILD" }, config);
        Assert.True(result1.IsSuccess);
        Assert.Equal(CompactLevel.Mild, result1.Value.Compact);

        var result2 = parser.Parse(new[] { "--compact-level", "AgGrEsSiVe" }, config);
        Assert.True(result2.IsSuccess);
        Assert.Equal(CompactLevel.Aggressive, result2.Value.Compact);
    }

    [Fact]
    public void Parse_CompactLevelInvalid_DoesDefaultsToMild()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();

        // Act
        var result = parser.Parse(new[] { "--compact-level", "invalid" }, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CompactLevel.Mild, result.Value.Compact); // Should default to Mild
    }

    [Fact]
    public void Parse_AppendFlag_SetsAppendMode()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--append" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Append);
    }

    [Fact]
    public void Parse_CompactAndAppendTogether_BothSet()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--compact", "--append" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CompactLevel.Mild, result.Value.Compact);
        Assert.True(result.Value.Append);
    }

    [Fact]
    public void Parse_CompactLevelAndAppendTogether_BothSet()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "--compact-level", "aggressive", "--append" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CompactLevel.Aggressive, result.Value.Compact);
        Assert.True(result.Value.Append);
    }

    [Fact]
    public void Parse_NoCompactOrAppendFlags_Defaults()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new string[] { };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(CompactLevel.None, result.Value.Compact);
        Assert.False(result.Value.Append);
    }

    [Fact]
    public void Parse_CompactOverridesConfig()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        config = config with { Compact = CompactLevel.Aggressive }; // Config has Aggressive
        var args = new[] { "--compact-level", "mild" }; // CLI says Mild

        // Act
        var result = parser.Parse(args, config);

        // Assert - CLI flag should be in CliArguments, config unchanged
        Assert.True(result.IsSuccess);
        Assert.Equal(CompactLevel.Mild, result.Value.Compact); // CLI flag wins
    }

    [Fact]
    public void Parse_DepthFlag_SetsDepth()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "-d", "5" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value.Depth);
    }

    [Fact]
    public void Parse_DiscoveryFlag_SetsDiscoveryMode()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "-D", "git" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(DiscoveryMode.Git, result.Value.DiscoveryMode);
    }
}
