using gc.CLI.Models;
using gc.CLI.Services;
using gc.Domain.Constants;
using gc.Domain.Models.Configuration;
using Xunit;

namespace gc.Tests.FeatureTests;

public class CliParserTests
{
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
        Assert.True(result.Value!.Append);
    }

    [Fact]
    public void Parse_NoFlags_Defaults()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new string[] { };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Append);
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
        Assert.Equal(5, result.Value!.Depth);
    }

    [Fact]
    public void Parse_ForceFlag_SetsForce()
    {
        // Arrange
        var parser = new CliParser();
        var config = BuiltInPresets.GetDefaultConfiguration();
        var args = new[] { "-f" };

        // Act
        var result = parser.Parse(args, config);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Force);
    }
}
